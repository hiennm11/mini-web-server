# Milestone 6: Bounded Worker Pool

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

M4.6 showed that unbounded thread creation (150 OS threads for 150 parked clients) wastes memory. How do you bound the number of threads and queue the rest?

## Scope

Replace `new Thread(...)` per connection with a fixed-size pool of worker threads. The accept loop enqueues a connection; a worker dequeues and handles it. If all workers are busy, the new connection waits in the queue.

The bounded-queue backpressure (M8) is in a separate slice — this milestone ships an unbounded queue first, then M8 bounds it.

## Slice

- **[s1-bounded-worker-pool.md](./s1-bounded-worker-pool.md)** — `WorkerPool` class with a fixed thread count and an unbounded `BlockingCollection<Socket>`. `Program.cs` accept loop enqueues instead of spawning threads.

## OSEP concept

- **Ch. 30 / §30** — bounded buffers and producer/consumer. BlockingCollection is .NET's take on a thread-safe bounded queue.
- **Ch. 27** — thread API; `ThreadPool.QueueUserWorkItem` is the OS-level equivalent (and what `WorkerPool` wraps in .NET).
- **Ch. 31** — semaphores / condition variables (used internally by BlockingCollection).

The core lesson: **thread creation is expensive** (1 MB stack each, kernel bookkeeping). A bounded pool amortizes that cost and caps the resource ceiling.

## .NET mechanism

- `System.Collections.Concurrent.BlockingCollection<T>` — thread-safe FIFO with `Take()` (blocks if empty) and `Add()` (blocks if bounded).
- `Thread.Sleep(int)` inside `WorkerThreadProc()` simulates slow I/O work.
- Manual `Task` continuation: a `TaskCompletionSource<bool>` is set when the worker finishes processing, so the accept loop can be notified.

## Files

- `src/MiniWebServer.Host/WorkerPool.cs` — `WorkerPool` static class with `Enqueue(Socket)`, `QueueLength`, `WorkerCount`, `Capacity`.
- `src/MiniWebServer.Host/Program.cs` — accept loop uses `WorkerPool.Enqueue(clientSocket)` instead of `new Thread(...)`.

## Where this leads

- M8 (s1 in `m8-bounded-queue/`) bounds the queue and adds 503 backpressure when the pool is saturated.
- M7 (`m7-async-event-based/`) takes a completely different approach: async I/O on a single OS thread.
- M10 (`m10-threadpool-cap/`) shows that .NET's built-in `ThreadPool` has similar properties.
