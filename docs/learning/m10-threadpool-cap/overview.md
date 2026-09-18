# Milestone 10: ThreadPool Cap

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does .NET's built-in `ThreadPool` behave under heavy async I/O load, and what happens if we cap it?

## Scope

Observe the `ThreadPool` thread count grow as connections pile up under the async server. Then cap it via `ThreadPool.SetMaxThreads(int, int)` and observe that new Tasks queue instead of running.

## Slice

- **[s1-threadpool-cap.md](./s1-threadpool-cap.md)** — observe `ThreadPool.ThreadCount` under async load (M7's 150 parked /slow clients); set a low cap; observe that Tasks start queuing.

## OSEP coverage

- **Ch. 27 Thread API** — `ThreadPool` (the OS-level thread pool). On Linux this is `clone` with `CLONE_THREAD` flags. On Windows it's the ThreadPool worker threads.
- **Ch. 30 Condition Variables** — `ThreadPool` internally uses CVs for thread parking.
- **Ch. 31 Semaphores** — `ThreadPool` often uses semaphores for task counting.

OSEP §27 covers `pthread_create` directly. The `.NET ThreadPool` is a managed wrapper around the OS thread pool — same concept, different API.

## OSEP §-specific deviations

- OSEP §27 focuses on per-thread APIs (`pthread_create`, `pthread_join`). The `ThreadPool` is a higher-level abstraction that queues work items.
- We don't measure scheduler internals (fairness, priority inversion). The slice focuses on the observable effect: cap = queue.

## .NET mechanism

- `ThreadPool.GetMinThreads(out workerMin, out completionMin)` / `GetMaxThreads(out workerMax, out completionMax)`.
- `ThreadPool.SetMaxThreads(workerMax, completionMax)`.
- `ThreadPool.GetAvailableThreads(out workerAvail, out completionAvail)` → active count = `workerMax - workerAvail`.

## Files

- `src/MiniWebServer.Host/Program.cs` — `/stats` route reports `threadpool_min`, `threadpool_max`, `threadpool_active`.
- `src/MiniWebServer.Host/AsyncServer.cs` — accepts `SetMaxThreads` args from startup.

## What this slice does NOT do

- Doesn't replace the async Task model with something else.
- Doesn't expose the cap as a runtime config (set once at startup).
