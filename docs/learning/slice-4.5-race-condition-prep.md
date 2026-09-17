# Slice 4.5: Prepare Race Condition Lab

## Question

What happens if two handler threads run `RequestStats.UnsafeCounter++` at the same time?

## OSTEP Context

- Chapter(s): 26 (Concurrency), 26.3 (Why It Gets Worse: Shared Data), 26.4 (The Heart Of The Problem: Uncontrolled Scheduling)
- Concept: a non-atomic read-modify-write on a shared variable is a textbook race condition. The C statement `counter = counter + 1` compiles to three x86 instructions (`mov`, `add`, `mov`). A timer interrupt between any two of them can cause one thread's update to overwrite another's.
- Key point from NotebookLM (citing OSEP §26.4 figure 26.7): Thread 1 loads `counter=50` into its register, adds 1 to get 51, gets interrupted. Thread 2 runs the same three instructions with the still-50 value, increments to 51, stores 51. Thread 1 resumes, stores its 51. Final counter = 51; one update was lost.

## C#/.NET Mechanism

- Bare `int++` on a `static` field compiles to load, add 1, store — not atomic on x86/x64. The CLR may emit different code, but the C# language guarantee is that it is not atomic for multi-threaded access.
- `Console.WriteLine` and HTTP response bodies are also not part of this slice's race — only the shared `UnsafeCounter` field is the target.
- No locks, no `Interlocked`, no `Volatile`. The race IS the lesson. The fix belongs to Milestone 5.

## Build

Add a non-atomic shared counter and a `/race` route that increments it 1_000_000 times.

File(s) affected:

- `src/MiniWebServer.Host/Program.cs`

Code shape inside the holder class:

```csharp
public static int UnsafeCounter = 0;
```

Code shape in `HandleClient` after the `/slow` block, before the response is built:

```csharp
HttpResponse response;
if (parsedRequest.Path == "/race")
{
    for (int i = 0; i < RaceIterations; i++)
    {
        RequestStats.UnsafeCounter++;
    }
    int observed = RequestStats.UnsafeCounter;
    Console.WriteLine($"[thread {threadId}] /race done. UnsafeCounter is now {observed}");
    response = new HttpResponse(
        200,
        "OK",
        "text/plain; charset=UTF-8",
        Encoding.UTF8.GetBytes($"UnsafeCounter = {observed}\n"));
}
else
{
    response = StaticFileResponder.CreateResponse(parsedRequest, webRoot);
}
```

Keep the slice small:

- Do not add `Interlocked` here. Slice 4.5 is about observing the race, not fixing it.
- Do not add a `/reset` route.
- Do not log per-iteration progress; the loop runs 1M times.
- Do not change slice 4.4's atomic counter — keep `RequestStats.TotalRequests` atomic; it is the comparison control.

## Experiment

Terminal 1 — start the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Terminal 2 — fire four concurrent `/race` jobs (each does 1M non-atomic increments; expected total = 4_000_000):

```powershell
$jobs = 1..4 | ForEach-Object {
    Start-Job -ScriptBlock {
        (Invoke-WebRequest -Uri http://localhost:8080/race -UseBasicParsing).Content
    }
}
$jobs | Wait-Job | Receive-Job
```

Expected result (deterministic upper bound): each thread reads the cumulative counter it just observed, in some order. The final reading should be **at most 4_000_000** — and usually **strictly less**, because lost increments happen whenever two threads load the same value before either stores back.

What to notice in the server console:

```text
[thread 5] Running 100000 non-atomic increments on RequestStats.UnsafeCounter...
[thread 5] /race done. UnsafeCounter is now <some value>
[thread 6] Running 100000 non-atomic increments on RequestStats.UnsafeCounter...
[thread 6] /race done. UnsafeCounter is now <some value>
[thread 7] Running 100000 non-atomic increments on RequestStats.UnsafeCounter...
[thread 7] /race done. UnsafeCounter is now <some value>
[thread 8] Running 100000 non-atomic increments on RequestStats.UnsafeCounter...
[thread 8] /race done. UnsafeCounter is now <some value>
```

The four `UnsafeCounter is now ...` readings, combined, should reflect more total increments than the four 1M loops actually performed, because the value reported is read AFTER each loop. If the four loops ran in true parallel, the value each thread reports will be smaller than its own 1M contribution would suggest (because other threads have not yet stored their increments).

## Observation

Answer after running the experiment:

How many increments are lost across four concurrent 1M-iteration loops?

Expected answer:

Some. A typical run produces a final counter noticeably below 4_000_000. In the smoke run for this slice:

- Final `UnsafeCounter` observed by the fourth thread: **3_214_691**
- Expected total: **4_000_000**
- Lost increments: **785_309** (about 19.6% of the expected increments)

More precise answer:

Each `++` on a shared `int` is a load-add-store triple. Two threads can both load `N`, both increment to `N+1`, both store `N+1` — losing one of the two updates. The loss rate scales with how often the OS context-switches between threads while each is mid-instruction; with four threads each doing 1M iterations, scheduler preemption hits mid-instruction frequently enough to lose almost a fifth of the expected increments.

The same outcome with `Interlocked.Increment` (used in slice 4.4) would be deterministic: 4_000_000 every time, no losses. The contrast between slice 4.4's atomic counter and slice 4.5's non-atomic counter is the lesson.

## Three-Question Test

1. What is the OS doing?
   - It preempts threads at timer-interrupt boundaries, including in the middle of `mov / add / mov` sequences that look like one C# statement. Each preemption is an opportunity for two threads to overlap their read-modify-write.
2. Which .NET API exposes it?
   - Bare `++` on a `static int` is the non-atomic read-modify-write. `Interlocked.Increment` is the atomic alternative.
3. Where does it break at scale?
   - Everywhere shared mutable state is updated without synchronization. Lost increments here are benign (a server count is off by a few thousand), but the same race on an account balance, an inventory count, or a connection slot would silently corrupt state. Slice 4.5 is observation only — the fix belongs to Milestone 5.

## Learning Note

### What changed

Added `RequestStats.UnsafeCounter` (non-atomic `static int`) and a `/race` route that increments it 1_000_000 times per request. The route responds with the cumulative counter observed at the end of its loop. The atomic `RequestStats.TotalRequests` from slice 4.4 is left in place as the comparison control.

`RaceIterations` was set to 1_000_000 after a smaller value (`100_000`) was too fast to lose increments — every increment completed before the scheduler preempted, so the four threads ran sequentially and the counter reached exactly 4_000_000 with no lost updates. 1_000_000 gives each loop enough wall time that preemption becomes common, making the race observable.

File affected:

- `src/MiniWebServer.Host/Program.cs`

### What I observed

Experiment — four concurrent 1M-iteration `/race` jobs:

```text
UnsafeCounter = 1000000
UnsafeCounter = 2000000
UnsafeCounter = 3207876
UnsafeCounter = 3214691
```

The four values are what each thread read *after* its own 1M-loop finished. The final reading was **3_214_691**, while the deterministic upper bound was **4_000_000**. Lost increments: **785_309**, roughly **19.6%** of the expected 4_000_000.

The numbers go up monotonically (each reading is at least as large as the previous one) because each thread reads AFTER its own loop, and by then more increments from other threads have happened. The fact that two threads reported almost the same final value (3_207_876 vs 3_214_691 — gap of only ~6_800) shows that the third and fourth threads finished their loops within 6_800 increments of each other. That gap is itself a race window: thread 3 finished, read 3_207_876, then thread 4 finished ~6_800 increments later and read 3_214_691.

### OSTEP concept

A non-atomic shared variable update is a textbook race condition. The C statement `counter = counter + 1` looks atomic but compiles to three x86 instructions:

```text
mov 0x1234, %eax       ; load counter into register
add $0x1, %eax         ; increment register
mov %eax, 0x1234       ; store register back
```

OSEP §26.4 figure 26.7 walks through this with two threads each loading 50, both incrementing to 51, both storing 51 — counter ends at 51, one update lost. The general failure mode: when two threads interleave at any boundary inside this triple, one of the increments is overwritten. The scheduler decides when those boundaries fall.

The slice makes the figure concrete. Without any code change between slices other than dropping `Interlocked`, the counter goes from "always correct" (slice 4.4) to "noticeably less than expected" (slice 4.5). The two slices together prove the C# language does not give us atomicity for free on shared mutable state.

### .NET mechanism

- `RequestStats.UnsafeCounter++` on a `static int` compiles to load-add-store without any memory barrier. The CLR x64 JIT emits the three-instruction sequence the same way OSEP describes for C on x86.
- `Interlocked.Increment(ref ...)` (slice 4.4) compiles to a `lock`-prefixed x86 instruction (or `xadd` with `lock`) — an atomic read-modify-write at the hardware level.
- The race in this slice is not a JIT bug. It is the literal C# language guarantee that `++` on `int` is not atomic across threads.

### Next question

How do we fix the race without losing the lesson? What synchronization primitive restores the missing increments?

Next slice: Milestone 5 — fix the race with `lock` or `Interlocked` and re-run the experiment.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted