# Slice 4.3: Observe Scheduling Non-Determinism

## Question

When the server handles several clients on separate threads, do requests start and finish in the order the clients arrived?

## OSTEP Context

- Chapter(s): 26 (Concurrency: An Introduction), 4.4 (Process States), 6 (Limited Direct Execution)
- Concept: the OS scheduler chooses which ready thread runs next. A thread created first does not necessarily run first or finish first.
- Key point from NotebookLM query: each thread has its own program counter, register state, and stack. During a context switch, the OS saves one thread's execution state and restores another. Timer interrupts and blocking I/O let the scheduler move execution between threads at moments the programmer does not control.

For this server, that means multiple handler threads can interleave in the console. The accept thread may create handler A before handler B, but handler B can still log, respond, or close first.

Detailed scheduler mapping:

```text
Handler thread A:
  Ready -> Running -> Blocked/Ready -> Running -> Done

Handler thread B:
  Ready -> Running -> Done

OS scheduler:
  chooses which Ready thread runs next
```

Important distinction from the NotebookLM query:

- Thread creation order is not execution order.
- Execution order is not completion order.
- A timer interrupt can stop a running thread and let the OS run another thread.
- A thread blocked on I/O, sleep, or another wait cannot run until that event completes.
- The programmer should not assume log lines from concurrent handlers appear in request-arrival order.

Teaching points:

1. Thread identity makes concurrent execution visible.
2. The OS scheduler, not the application, decides which ready thread runs.
3. Console output from concurrent handlers can interleave.
4. A later request can finish before an earlier request.
5. This non-determinism is harmless for local variables, but dangerous once threads share mutable data.

Misconception to avoid:

> Threads run in the same order they were started.

Wrong. A thread that is created first may stay Ready while another thread runs. A thread that starts first may block, letting later threads finish first.

## C#/.NET Mechanism

- Type or API: `Thread.CurrentThread.ManagedThreadId`.
- Why this, not another abstraction: `ManagedThreadId` gives each managed thread a visible identifier in logs without adding shared state, locks, counters, thread pools, or async behavior.

Current thread-per-connection flow from slice 4.2:

```text
Accept() -> start Thread(HandleClient) -> loop back to Accept()
```

Slice 4.3 should make each handler's thread visible:

```text
[thread 7] START /slow
[thread 8] START /
[thread 8] END /
[thread 7] END /slow
```

The exact IDs and ordering may change across runs.

## Build

Add thread-aware console logs around each client handler.

File affected:

- `src/MiniWebServer.Host/Program.cs`

Expected code behavior:

1. Capture `Thread.CurrentThread.ManagedThreadId` inside `HandleClient`.
2. Log the handler thread ID near the start of `HandleClient`.
3. Log the handler thread ID after the request is parsed, including the request path.
4. Log the handler thread ID before the handler exits.
5. Add a tiny variable-duration delay only if needed to make interleaving easy to observe.

Example shape:

```csharp
int threadId = Thread.CurrentThread.ManagedThreadId;
Console.WriteLine($"[thread {threadId}] Handler started for {clientSocket.RemoteEndPoint}");

// after parsing:
Console.WriteLine($"[thread {threadId}] Path: {parsedRequest.Path}");

// before close/end:
Console.WriteLine($"[thread {threadId}] Handler finished for {parsedRequest.Path}");
```

Keep the slice small. Do not add locks, counters, shared state, request IDs, a logging abstraction, thread pools, async sockets, or cancellation. Slice 4.3 is about observing scheduler behavior, not fixing or coordinating it.

## Experiment

Use two terminals.

Terminal 1 — start the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Terminal 2 — send several requests quickly:

```powershell
1..5 | ForEach-Object { Start-Job { curl http://localhost:8080/ } } | Wait-Job | Receive-Job
```

Then mix slow and fast requests:

```powershell
$slow = Start-Job { curl http://localhost:8080/slow }
Start-Sleep -Milliseconds 100
$fast = 1..3 | ForEach-Object { Start-Job { curl http://localhost:8080/ } }
@($slow) + $fast | Wait-Job | Receive-Job
```

Expected result:

- The console shows several different thread IDs.
- Log lines from different thread IDs can interleave.
- Fast `/` requests can finish while `/slow` is still sleeping.
- Repeating the experiment can produce a different ordering.

What to notice in the server console:

```text
[thread 9] Handler started ...
[thread 10] Handler started ...
[thread 9] Path: /slow
[thread 9] Sleeping 5000 ms to simulate blocking I/O...
[thread 10] Path: /
[thread 10] Response: 200 OK
[thread 10] Handler finished for /
[thread 9] Response: 404 Not Found
[thread 9] Handler finished for /slow
```

The important observation is not the exact numbers. The important observation is that the scheduler can run thread 10 while thread 9 is blocked.

## Observation

Answer after running the experiment:

Do requests finish in the order they arrived?

Expected answer:

No. The server creates one handler thread per client, but the OS scheduler decides when each thread runs. If an earlier thread blocks on `/slow`, later ready threads can run and finish first. Even without `/slow`, rapid requests may produce different log interleavings across runs.

More precise answer:

Each handler thread has its own program counter and register state. When the scheduler context-switches, it saves one thread's state and restores another. The server process keeps one shared address space, but execution moves between multiple independent thread states. Because timer interrupts, I/O waits, and scheduler policy are outside the program's direct control, ordering is non-deterministic.

## Three-Question Test

1. What is the OS doing?
   - It schedules ready threads, deschedules running threads, and resumes blocked threads when their events complete. Timer interrupts and I/O waits make the interleaving visible.
2. Which .NET API exposes it?
   - `Thread.CurrentThread.ManagedThreadId` exposes the current managed thread identity so the server can tag log lines with the thread handling each client.
3. Where does it break at scale?
   - Non-deterministic interleavings become dangerous when multiple threads access shared mutable state. Logs may be merely messy, but shared counters or caches can become incorrect without synchronization. This motivates slices 4.4 and 4.5.

## Learning Note

### What changed

Annotated every `Console.WriteLine` inside `HandleClient` and its outer per-client `try/catch` block with the handler thread's managed thread ID (`Thread.CurrentThread.ManagedThreadId`). One change per log line, no new state, no new abstractions. The accept loop, error handling, and the `/slow` path are otherwise unchanged.

Files affected:

- `src/MiniWebServer.Host/Program.cs`

### What I observed

Experiment 1 — concurrent fast requests:

```powershell
1..3 | ForEach-Object { Start-Job { curl http://localhost:8080/ } } | Wait-Job | Receive-Job
```

Server console (abridged):

```text
[thread 4] Accepted client socket from 127.0.0.1:64338
[thread 4] Response: 200 OK
[thread 4] Sent 324 response bytes.
[thread 4] Closed client socket.

[thread 5] Accepted client socket from 127.0.0.1:64339
[thread 5] Response: 200 OK
[thread 5] Closed client socket.

[thread 6] Accepted client socket from 127.0.0.1:64340
[thread 6] Response: 200 OK
[thread 6] Closed client socket.

[thread 7] Accepted client socket from 127.0.0.1:64341
[thread 7] Path: /slow
[thread 7] Sleeping 5000 ms to simulate blocking I/O...
```

The fast three ran sequentially before `/slow` even started. Three distinct thread IDs (4, 5, 6) prove each request got its own thread — slice 4.2's behavior is real.

Experiment 2 — slow first, fast requests fired during the 5-second sleep:

```powershell
$slow = Start-Job { curl http://localhost:8080/slow }
Start-Sleep -Milliseconds 50
$fast = 1..3 | ForEach-Object { Start-Job { curl http://localhost:8080/ } }
@($slow) + $fast | Wait-Job | Receive-Job
```

Server console (key excerpt):

```text
[thread 3] Path: /slow
[thread 3] Sleeping 5000 ms to simulate blocking I/O...

[thread 9]  Accepted client socket from 127.0.0.1:64362
[thread 9]  Response: 200 OK
[thread 9]  Closed client socket.

[thread 10] Accepted client socket from 127.0.0.1:64368
[thread 10] Response: 200 OK
[thread 10] Closed client socket.

[thread 11] Accepted client socket from 127.0.0.1:64369
[thread 11] Response: 200 OK
[thread 11] Closed client socket.

[thread 3]  Response: 404 Not Found      <-- /slow finishes last
[thread 3]  Sent 114 response bytes.
[thread 3]  Closed client socket.
```

This is the textbook OSEP §26.2 trace (figures 26.3, 26.4, 26.5 show three possible orderings of `main + Thread 1 + Thread 2` in `t0.c`):

- `/slow` started on thread 3.
- Threads 9, 10, 11 each accepted, parsed, responded, and closed `/` requests while thread 3 was blocked in `Thread.Sleep` (5000 ms when this slice ran; later extended to 30000 ms).
- Thread 3 only logged `Response: 404 / Sent / Closed` after all three fast requests had already finished.
- The managed thread ID `3` reappeared even though `/` threads used 9, 10, 11 — .NET recycles thread IDs after a thread exits. The IDs themselves are not unique per request; the IDs in flight are.

### OSTEP concept

A thread is a separate point of execution with its own PC, registers, and stack. Inside one process, threads share code, static data, and the heap, but the scheduler chooses which thread runs at any moment. Timer interrupts force context switches at points the programmer does not control, so even with two threads created in a known order, completion order is not deterministic. OSEP §26.2 demonstrates this with the `t0.c` example (figures 26.3, 26.4, 26.5 — three different valid interleavings of `main` + `Thread 1` + `Thread 2`).

The visible proof here is not the thread IDs themselves — it is the *gap between thread 3 starting `/slow` and thread 3 finishing it*. During that gap, three other threads were scheduled, ran to completion, and exited. The scheduler interleaved them around the blocked thread without any cooperation from the application.

Note: OSEP §26.4 (Uncontrolled Scheduling) covers a *different* demonstration — the 3-instruction `mov`/`add`/`mov` race (figure 26.7) where two threads racing on `counter++` lose updates because the read-modify-write is not atomic. That lesson motivates slice 4.5, not this one. The lesson here (§26.2) is the simpler point: even when threads do *not* race on shared state, the order in which they execute is not under the application's control.

This is also a quiet reminder of why slice 4.5 will matter: every console write here goes through the OS's stdout buffer. Two threads writing at almost the same moment could in principle interleave their text — `Console.WriteLine` is *not* atomic across threads. We have not seen it yet because each `WriteLine` call completes quickly relative to the 5-second sleep, but the danger is real. Slice 4.4 (shared address space) and slice 4.5 (the `counter++` race) will make that danger concrete.

### .NET mechanism

`Thread.CurrentThread.ManagedThreadId` returns an `int` that uniquely identifies the current managed thread while it is alive. The CLR may recycle the ID after the underlying OS thread exits, so the ID is only stable for the lifetime of the thread. That is why the same `3` showed up for the second experiment's `/slow` even though earlier experiments had used IDs 4–7.

The accept-loop lambda (`new Thread(() => HandleClient(...)).Start()`) wraps `HandleClient` in `try/catch`, so the same thread ID is also visible in the error-path logs after this slice's edit. No locks, no shared counters, no `Interlocked`, no async/await were needed to observe the behavior — the threads themselves were already created in slice 4.2; this slice only makes them *visible*.

### Next question

If handler threads share one process address space, which data is private to each thread and which data is shared by all threads?

Next slice: Slice 4.4 — Shared Address Space Appears.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted
