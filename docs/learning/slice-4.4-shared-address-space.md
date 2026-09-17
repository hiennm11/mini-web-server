# Slice 4.4: Shared Address Space Appears

## Question

If handler threads share one process address space, which data is private to each thread and which data is shared by all threads?

## OSTEP Context

- Chapter(s): 13 (The Abstraction: Address Spaces), 26 (Concurrency: An Introduction)
- Concept: in a multi-threaded process, code, heap, and static data are shared across threads; each thread has its own stack and registers (TCB).
- Key point from NotebookLM: locals live on the calling thread's stack, so they are private by convention; static fields live in the process-wide data segment, so every thread that reaches them sees the same memory location. There is no MMU isolation between threads in the same process — only between processes (Ch. 13 §13.4, isolation).

## C#/.NET Mechanism

- A `static` field on a class lives once and is visible to every thread that touches it. Top-level `static` is not legal in C# top-level statements, so the slice introduces a small holder class `RequestStats`.
- A local variable inside a method lives on the executing thread's stack. No other thread can reach it.
- `Interlocked.Increment(ref ...)` does an atomic read-modify-write on an `int`. This slice uses it so the increment is well-defined; slice 4.5 will deliberately switch to non-atomic `++` to expose the race.

## Build

Add a shared counter plus a per-thread local id and log both next to the existing thread-id log line.

File(s) affected:

- `src/MiniWebServer.Host/Program.cs`

Code shape inside `HandleClient`, near the top:

```csharp
int threadId = Thread.CurrentThread.ManagedThreadId;

// Per-thread stack value. Only this handler thread can see it.
int localRequestId = Interlocked.Increment(ref RequestStats.TotalRequests);

Console.WriteLine();
Console.WriteLine($"[thread {threadId}] Accepted client socket from {clientSocket.RemoteEndPoint}");
Console.WriteLine($"[thread {threadId}] Local request id: {localRequestId}, shared total seen so far: {RequestStats.TotalRequests}");
```

Add the holder type near the end of the file:

```csharp
public static class RequestStats
{
    public static int TotalRequests = 0;
}
```

Keep the slice small:

- No new request ID logic, no logging abstraction, no shared mutable response cache.
- The atomic `Interlocked.Increment` is on purpose — slice 4.5 will replace it with non-atomic `++` to expose the race.

## Experiment

Terminal 1 — start the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Terminal 2 — fire five requests in quick succession from different processes:

```powershell
1..5 | ForEach-Object { Start-Job { curl http://localhost:8080/ } } | Wait-Job | Receive-Job
```

Expected console output:

```text
[thread 4] Accepted client socket from 127.0.0.1:64556
[thread 4] Local request id: 1, shared total seen so far: 1
...
[thread 8] Accepted client socket from 127.0.0.1:64562
[thread 8] Local request id: 5, shared total seen so far: 5
```

Each handler thread sees its own `localRequestId` (the value `Interlocked.Increment` returned to it) AND the shared total right after the increment. With five requests that arrive fast enough to interleave but not block, the two numbers happen to match because no other thread ran in between the increment and the read on any single thread.

A second experiment makes the difference visible — slow request first, then a fast one:

```powershell
$slow = Start-Job { curl http://localhost:8080/slow }
Start-Sleep -Milliseconds 50
$fast = Start-Job { curl http://localhost:8080/ }
@($slow) + $fast | Wait-Job | Receive-Job
```

Expected console:

```text
[thread 3] Local request id: 1, shared total seen so far: 1   <- /slow accepted first
[thread 3] Sleeping 5000 ms to simulate blocking I/O...

[thread 4] Local request id: 2, shared total seen so far: 2   <- / accepted while /slow sleeps
[thread 4] ...
[thread 4] Closed client socket.

[thread 3] Response: 404 Not Found
[thread 3] Closed client socket.
```

The fast request sees a shared total of `2` and a local id of `2`. The two values tell the same story from two angles: the local id is the count this thread observed for itself; the shared total is the count this thread observed for the process.

## Observation

Answer after running the experiment:

Which data is private to a handler thread, and which is shared by all handler threads?

Expected answer:

- Private: every local variable in `HandleClient`, including the new `localRequestId`, lives on the handler's stack. Other handlers cannot read it.
- Shared: `RequestStats.TotalRequests` is a single static field. Every handler thread reads and writes the same memory location. The OS does not give threads separate copies — that is the whole point of the abstraction being *one* virtual address space for the process.

More precise answer:

In C#, `static` on a class field maps the field to a single location in the process's loader data segment, shared by every thread that references the containing type. Local variables declared inside a method are allocated on the calling thread's stack frame; their addresses are relative to that thread's stack pointer, which is saved and restored across context switches, so another thread never sees them. This is the same distinction OSEP draws in Ch. 13: code, heap, and static data are shared across threads of one process; each thread gets its own stack.

## Three-Question Test

1. What is the OS doing?
   - It maps one virtual address space onto the process and threads all share that mapping. Static fields share one physical page through that mapping; locals live on per-thread stacks that the OS swaps via the TCB on context switches.
2. Which .NET API exposes it?
   - `static` fields for shared data, local variables for per-thread data, `Interlocked.Increment` for atomic shared-state updates.
3. Where does it break at scale?
   - Reading and writing shared fields from multiple threads without synchronization produces a race (slice 4.5). Logging from many threads can interleave at the byte level even when each `WriteLine` is one call, because `Console.WriteLine` is not atomic across threads. The lesson in this slice is *visibility*, not safety.

## Learning Note

### What changed

Added `RequestStats.TotalRequests` (a static field on a small holder class because top-level `static` is invalid in C# top-level statements) and a per-thread `localRequestId` in `HandleClient`. The log statement now shows both values next to the existing `[thread N]` tag.

The increment uses `Interlocked.Increment` deliberately — the lesson here is *what is shared vs private*, not *how shared state breaks*. Slice 4.5 will deliberately replace the atomic increment with plain `++` to expose the data race that comes from un-synchronized shared memory.

File affected:

- `src/MiniWebServer.Host/Program.cs`

### What I observed

Experiment 1 — five concurrent fast requests:

```text
[thread 4] Local request id: 1, shared total seen so far: 1
[thread 5] Local request id: 2, shared total seen so far: 2
[thread 6] Local request id: 3, shared total seen so far: 3
[thread 7] Local request id: 4, shared total seen so far: 4
[thread 8] Local request id: 5, shared total seen so far: 5
```

Each thread sees its own `localRequestId` (1..5) and the shared total reads the same value the increment produced. Because `Interlocked.Increment` returns the new value *after* the atomic update, and we read the shared total right after, there is no race window in this experiment. The two values are equal here only because the increment and the read are very close together on the same thread.

Experiment 2 — slow request first, fast request 50 ms later:

```text
[thread 3] Local request id: 1, shared total seen so far: 1
[thread 3] Sleeping 5000 ms to simulate blocking I/O...

[thread 4] Local request id: 2, shared total seen so far: 2
[thread 4] Response: 200 OK
[thread 4] Closed client socket.

[thread 3] Response: 404 Not Found
[thread 3] Closed client socket.
```

While thread 3 was blocked in `Thread.Sleep(5000)`, thread 4 incremented the shared counter from 1 to 2 and read 2 right after. The counter incremented even though thread 3 was not running. That is the shared-address-space guarantee in action: the static field is one cell, every thread sees the latest write.

### OSTEP concept

In a multi-threaded process, threads share code, static data, and heap, and each has its own stack and registers. OSEP Ch. 13 frames this for processes (each process has its own virtual address space); Ch. 26 extends it to threads (threads of one process share one virtual address space). The MMU gives hardware isolation between processes; it does not isolate threads inside the same process. Two threads running concurrently can technically read each other's stack frames if they obtain a pointer — the "isolation" between thread stacks is a convention enforced by compilers and stack-pointer management, not by hardware.

The shared `RequestStats.TotalRequests` is in the loader data segment, visible to every thread that references the type. The local `localRequestId` is on the calling thread's stack frame; its address is relative to that thread's `%rsp` (stack pointer), which the OS saves and restores during every context switch. No other thread ever sees it.

This is the same memory map OSEP draws in Figure 26.1 (right): two stacks spread across one address space, code and shared segments above and below them. The server now makes that picture observable in its own console output.

### .NET mechanism

- `static int TotalRequests` (on a holder class) maps to one field slot in the process-wide loader data. Every thread that reaches `RequestStats.TotalRequests` reads and writes the same memory.
- `int localRequestId` inside `HandleClient` is on each thread's own stack; it lives only as long as that handler is on the stack frame for `HandleClient`. After `HandleClient` returns, the slot is gone.
- `Interlocked.Increment(ref …)` performs an atomic compare-and-swap loop at the CPU level so the read-modify-write is not torn across threads. This makes the slice deterministic. Replacing it with `RequestStats.TotalRequests++` would turn the same code into a textbook race — that is exactly what slice 4.5 does.

### Next question

What happens if two threads run `RequestStats.TotalRequests++` instead of `Interlocked.Increment`?

Next slice: Slice 4.5 — Prepare Race Condition Lab.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted