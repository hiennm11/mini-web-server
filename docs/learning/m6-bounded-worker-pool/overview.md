# Milestone 6: Bounded Worker Pool

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

M4.6 showed that unbounded thread creation (150 OS threads for 150 parked clients) wastes memory. How do you bound the number of threads and queue the rest?

## Scope

Replace `new Thread(...)` per connection with a fixed-size pool of worker threads. The accept loop enqueues a connection; a worker dequeues and handles it. If all workers are busy, the new connection waits in the queue.

The bounded-queue backpressure (M8) is in a separate slice — this milestone ships an unbounded queue first, then M8 bounds it.

## Slice

- **[s1-bounded-worker-pool.md](./s1-bounded-worker-pool.md)** — `WorkerPool` class with a fixed thread count and an unbounded `BlockingCollection<Socket>`. `Program.cs` accept loop enqueues instead of spawning threads.

## OSTEP coverage

- **Ch. 28 Locks** — the `WorkerPool` is essentially a multi-threaded queue protected by a lock (or `BlockingCollection` internally uses a lock + condition variable).
- **Ch. 30 Condition Variables** (§30.2 the canonical "two CVs" bounded-buffer problem, with `wait`/`signal`).
- **Ch. 31 Semaphores** (§31.4 the bounded-buffer with semaphores — empty/full + mutex).

The bounded buffer problem OSEP §30 + §31.4 covers is exactly what our `WorkerPool` solves — except the "items" are socket connections instead of integers.

## OSEP §-specific deviations

- OSEP §30 covers the textbook producer/consumer with `pthread_cond_wait` + `pthread_cond_signal`. .NET's `BlockingCollection<T>.Take()` is essentially the same primitive.
- OSEP §31.4 shows the full 3-semaphore solution (mutex + empty + full). Our `BlockingCollection` wraps this internally.
- OSEP §28.1-§28.2 covers the lock primitive, which our internal queue uses.

## Key OSEP quotes

> "A bounded buffer is also used when you pipe the output of one program into another." (OSEP §30.2)

> "If we are going to add bounded buffers to a multi-threaded program, we have to somehow add synchronization to the get and put routines." (OSEP §30.2)

## .NET mechanism

- `System.Collections.Concurrent.BlockingCollection<T>` — thread-safe FIFO with `Take()` (blocks if empty) and `Add()` (blocks if bounded). Internally uses a lock + two condition variables (the OSEP §30.2 pattern).
- `Thread.Sleep(int)` inside `WorkerThreadProc()` simulates slow I/O work.
- `TaskCompletionSource<bool>` notifies the accept loop when a worker finishes.

## Files

- `src/MiniWebServer.Host/WorkerPool.cs` — `WorkerPool` static class with `Enqueue(Socket)`, `QueueLength`, `WorkerCount`, `Capacity`.
- `src/MiniWebServer.Host/Program.cs` — accept loop uses `WorkerPool.Enqueue(clientSocket)` instead of `new Thread(...)`.

## Where this leads

- M8 (s1 in `m8-bounded-queue/`) bounds the queue and adds 503 backpressure when the pool is saturated.
- M7 (`m7-async-event-based/`) takes a completely different approach: async I/O on a single OS thread.
- M10 (`m10-threadpool-cap/`) shows that .NET's built-in `ThreadPool` has similar properties.
