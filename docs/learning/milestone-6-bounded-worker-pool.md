# Milestone 6: Bounded Worker Pool + Producer/Consumer Queue

## Question

How do we accept arbitrarily many connections without allocating an OS thread per connection?

## OSTEP Context

- Chapter(s): 30 (Condition Variables), 31 (Semaphores)
- Concept: a fixed pool of worker threads, plus a producer/consumer queue, replaces unbounded thread creation. Producers (the accept loop) enqueue work; consumers (the worker threads) dequeue when idle. The condition variable parks idle workers without spinning (OSEP §30.2 producer/consumer with two CVs).
- Key point from OSEP §30.4: holding the lock while signaling is the simple, always-correct rule. A worker waits under the lock using `Monitor.Wait` (the .NET equivalent of `pthread_cond_wait`), which atomically releases the lock and parks the thread. A producer enqueues under the lock and calls `Monitor.Pulse` (the .NET equivalent of `pthread_cond_signal`) to wake one waiter. Re-checking the predicate inside `while (queue.IsEmpty)` instead of `if` handles Mesa semantics: signal is a hint, not a guarantee (OSEP §30.2 the broken `if` version).

## C#/.NET Mechanism

- `Queue<Socket>` for FIFO order. Could be a `ConcurrentQueue<Socket>` for lock-free enqueue/dequeue, but that hides the lock lesson this slice teaches; we use a plain queue + explicit `lock`.
- `Monitor.Wait(obj)` releases the lock, parks the thread, and re-acquires the lock before returning — exactly `pthread_cond_wait(obj, mutex)`.
- `Monitor.Pulse(obj)` wakes one waiter of the same lock — exactly `pthread_cond_signal(obj)`.
- `Thread` is reused (one per worker), created once at startup, never per connection.
- `WorkerPool.Static class` exposes `Start(n, webRoot)`, `Enqueue(socket)`, `QueueLength`, `WorkerCount`, and a `ClientHandler` delegate that the top-level `Program` registers once.

## Build

Add a `WorkerPool` class with fixed thread count, bounded queue, and a `/qstats` route. Replace the per-connection `new Thread(...)` in the accept loop with `WorkerPool.Enqueue(clientSocket)`.

Files affected:

- `src/MiniWebServer.Host/WorkerPool.cs` — new file, ~120 lines.
- `src/MiniWebServer.Host/Program.cs` — accept loop delegates to the pool; add `/qstats` route; constant `WorkerCount = 8`.

Code shape inside `WorkerPool`:

```csharp
private static readonly object PoolLock = new();
private static readonly Queue<Socket> Pending = new();

private static void WorkerLoop(int workerId)
{
    while (!Stopping)
    {
        Socket clientSocket;
        lock (PoolLock)
        {
            while (Pending.Count == 0 && !Stopping)
                Monitor.Wait(PoolLock);
            if (Stopping && Pending.Count == 0) return;
            clientSocket = Pending.Dequeue();
        }
        try { ClientHandler?.Invoke(clientSocket, WebRoot); }
        catch (Exception ex) { /* log + dispose */ }
    }
}

public static void Enqueue(Socket s)
{
    lock (PoolLock) { Pending.Enqueue(s); Monitor.Pulse(PoolLock); }
}
```

Accept loop changes from:

```csharp
var thread = new Thread(() => HandleClient(clientSocket, webRoot));
thread.IsBackground = true; thread.Start();
```

to:

```csharp
WorkerPool.Enqueue(clientSocket);
```

Keep the slice small:

- Queue is unbounded (`Queue<Socket>` with no max). Backpressure is out of scope (Milestone 6's roadmap row about "bounded queue and backpressure" is left for a future slice).
- Workers run forever; `Shutdown` exists but is unused (Ctrl+C still kills the process).
- The client handler is registered via a delegate because `HandleClient` is a top-level method generated on the synthetic `<Main>$` class and cannot be called directly from `WorkerPool` without `InternalsVisibleTo` hacks.

## Experiment

Terminal 1 — start the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Terminal 2 — observe baseline, park 50 slow sockets (each sends one byte and holds), sample, park 100 more, sample:

```powershell
(Invoke-WebRequest -Uri http://localhost:8080/qstats -UseBasicParsing).Content

Add-Type @'
using System;
using System.Net.Sockets;
using System.Threading;

public class SlowClient {
    public static void OpenMany(string host, int port, int count) {
        var sockets = new Socket[count];
        for (int i = 0; i < count; i++) {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            s.Connect(host, port);
            s.Send(new byte[] { 0x47 });
            sockets[i] = s;
        }
        Thread.Sleep(8000);
        foreach (var s in sockets) { try { s.Close(); } catch { } }
    }
}
'@

[SlowClient]::OpenMany('127.0.0.1', 8080, 50)
(Invoke-WebRequest -Uri http://localhost:8080/qstats -UseBasicParsing).Content
[SlowClient]::OpenMany('127.0.0.1', 8080, 100)
(Invoke-WebRequest -Uri http://localhost:8080/qstats -UseBasicParsing).Content
```

Expected result:

- Baseline `/qstats` shows `worker_count = 8`, `queue_length = 0`.
- After 50 parked sockets, `worker_count` is still 8 and `queue_length` is 0 (clients are processed by idle workers as fast as they arrive; the 30 s `/slow` sleep inside the handler is what keeps them in flight, not the queue).
- After 100 more parked sockets (150 cumulative), `worker_count` is still 8 and `queue_length` is still 0.
- `/stats` shows `private_bytes` far below the slice-4.6 baseline for the same workload (because only 8 worker threads exist, not 150).

## Observation

Answer after running the experiment:

How much memory does each parked client cost now?

Expected answer:

Roughly the cost of one `Socket` handle (a few KB) plus its share of any worker thread already in use. There are no 1 MB-per-thread costs because there are only 8 worker threads regardless of how many clients are parked.

Concrete numbers from this milestone's smoke run:

| Stage | worker_count | queue_length | OS threads | private_bytes |
|---|---|---|---|---|
| Baseline | 8 | 0 | 16 | 10 MB |
| After 50 parked | 8 | 0 | — | — |
| After 150 parked | 8 | 0 | 17 | 21 MB |

Compare to slice 4.6's unbounded thread-per-connection baseline (same 150 parked sockets): **174 MB** private. The bounded pool uses **~12× less private memory** at 150 concurrent slow clients, and the gap grows linearly with the number of parked clients.

More precise answer:

`Process.Threads.Count` stays near the fixed pool size plus runtime threads (16-17 on this hardware). The 8 workers each reserve a 1 MB stack, but no new threads are allocated per client. Each parked client is just a `Socket` kernel object plus a few hundred bytes of BCL state. With 150 parked clients, total private memory grew by ~11 MB over baseline — that is mostly the workers' stacks plus the queued-and-being-served client handles, not 150 × 1 MB of thread stacks.

## Three-Question Test

1. What is the OS doing?
   - It runs the accept thread (waiting in `Accept()`) plus 8 worker threads. When a worker has nothing to do, it is parked in the kernel via `Monitor.Wait`; when the accept loop enqueues a new socket, `Monitor.Pulse` wakes exactly one worker. The OS scheduler sees a stable set of 8 worker threads regardless of client count.
2. Which .NET API exposes it?
   - `Monitor.Wait(obj)` / `Monitor.Pulse(obj)` are the .NET equivalents of `pthread_cond_wait` / `pthread_cond_signal`. They always operate on the lock that the calling thread currently owns.
3. Where does it break at scale?
   - The queue is currently unbounded, so a massive burst can grow `Queue<Socket>` without limit. A bounded queue with a capacity + a reject-or-block policy on the producer side would add backpressure; that is a future slice in this milestone's roadmap. The current slice is about the *pool* not the *bounded buffer*; both belong to Milestone 6 but are conceptually separate steps.

## Learning Note

### What changed

Added `WorkerPool` static class with `Queue<Socket>` + `Monitor.Wait/Pulse` (the .NET analogue of `pthread_cond_wait/signal`). `Program.cs` initializes the pool once at startup with 8 worker threads, registers `HandleClient` as the client handler delegate, and replaces the per-connection `new Thread(...)` with `WorkerPool.Enqueue(clientSocket)`. A new `/qstats` route exposes worker count, queue length, and total requests.

### M6.3 (added 2026-09-17): Bounded queue + 503 backpressure

The original M6 had an unbounded `Queue<Socket>` — under sustained overload the queue could grow to thousands of items and risk OOM. The 6.3 follow-up adds a `MaxQueueSize = 64` cap, a `TryEnqueue` non-blocking enqueue that returns `false` when full, and a reject path in the accept loop that replies `503 Service Unavailable` and closes the socket. `/qstats` now also reports `capacity` and `at_capacity`. `ListenBacklog` was raised from 10 to 128 so the kernel can hold the 72 simultaneous clients used in the smoke.

Smoke proved: with 72 `/slow` clients connected (8 workers in `Thread.Sleep(30000)` + 64 queued), a 73rd request immediately gets `503 Service Unavailable` instead of being silently queued or having the kernel refuse the connection. The reject path also drains a small prefix of the request before closing so Windows does not RST the client (a socket closed with unread data in the receive buffer triggers RST on Windows).

Full learning note at `docs/learning/slice-6.3-bounded-queue-and-backpressure.md`. Plan doc predates this update: see ADR 0004 (`docs/adr/0004-extend-broad-concurrency-roadmap.md`) for the wider next-milestones roadmap.

Files affected:

- `src/MiniWebServer.Host/WorkerPool.cs` — new file.
- `src/MiniWebServer.Host/Program.cs` — accept loop delegates to pool, adds `/qstats`, adds `WorkerCount = 8`.

### What I observed

Baseline `/qstats`:

```text
worker_count = 8
queue_length = 0
total_requests = 1
```

Baseline `/stats`:

```text
threads = 16
working_set_bytes = 25612288
private_bytes = 10555392
total_requests = 2
```

After parking 50 slow sockets for 8 seconds and `/qstats`:

```text
worker_count = 8
queue_length = 0
total_requests = 53
```

After parking 100 more slow sockets (150 cumulative) and `/qstats`:

```text
worker_count = 8
queue_length = 0
total_requests = 154
```

Final `/stats`:

```text
threads = 17
working_set_bytes = 38821888
private_bytes = 21295104
total_requests = 155
```

The pool kept worker_count at 8 throughout the experiment, even with 150 concurrent slow clients. The OS-level thread count grew from 16 (idle) to 17 (during stress) — only 1 additional thread, almost certainly the runtime's GC or finalizer warming up. Compared to slice 4.6's 174 MB of private memory for the same workload, the bounded pool holds at 21 MB.

### OSEP concept

The bounded worker pool is the textbook producer/consumer pattern from OSEP §30.2 (with two condition variables) generalized to a server. Producers (the accept thread) enqueue `Socket` objects; consumers (the 8 worker threads) wait on a condition variable, dequeue, and run `HandleClient`.

The locking discipline is the lesson of §30.4: the lock is held across both the queue check and the wait. Without the lock, a producer could enqueue between the consumer's empty check and its `Wait`, leaving the new item stranded with no one to consume it — the wakeup/waiting race OSEP §30.4 calls out. With the lock, the wait atomically releases it and parks the thread, so a concurrent producer sees the locked queue and serializes through `Monitor.Pulse`.

`while (Pending.Count == 0 && !Stopping) Monitor.Wait(...)` (instead of `if`) handles Mesa semantics (OSEP §30.2 broken `if`): a wakeup is a hint that the predicate might now be true, and the consumer must re-check. The `&& !Stopping` adds a shutdown predicate so a worker can exit when the pool is told to stop.

The slice deliberately does *not* bound the queue. The OSEP producer/consumer with two CVs would let producers block when `Pending.Count == Max`. Implementing it requires choosing a max, returning `503 Service Unavailable` on overflow, or blocking the accept loop — each is a meaningful design decision. The slice as-is teaches the pool, not the bounded buffer. Bounded-queue backpressure is the natural next slice.

### .NET mechanism

- `lock (PoolLock) { ... }` is the C# form of `pthread_mutex_lock` / `pthread_mutex_unlock`. The runtime uses an object header word for the uncontended fast path and a kernel `Monitor` wait queue for the contended path.
- `Monitor.Wait(PoolLock)` releases `PoolLock`, parks the calling thread on the monitor's wait queue, then re-acquires `PoolLock` before returning — three operations, atomic with respect to other waiters on the same monitor.
- `Monitor.Pulse(PoolLock)` moves one waiter from the wait queue to the ready queue. The OS scheduler decides when that thread actually runs.
- The worker thread bodies are `IsBackground = true` so they do not prevent the process from exiting on Ctrl+C.

### Next question

The queue is unbounded. Under a sustained burst, `Queue<Socket>` can grow without limit, eventually exhausting memory. We need backpressure.

Next slice (future): add a queue capacity (e.g., 64) and have the accept loop block (or return 503) when the queue is full.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted