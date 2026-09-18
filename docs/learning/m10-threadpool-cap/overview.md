# Milestone 10: ThreadPool Cap

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does .NET's built-in `ThreadPool` behave under heavy async I/O load, and what happens if we cap it?

## Scope

Observe the `ThreadPool` thread count grow as connections pile up under the async server. Then cap it via `ThreadPool.SetMaxThreads(int, int)` and observe that new Tasks queue instead of running.

## Slice

- **[s1-threadpool-cap.md](./s1-threadpool-cap.md)** — observe `ThreadPool.ThreadCount` under async load (M7's 150 parked /slow clients); set a low cap; observe that Tasks start queuing.

## OSEP concept

- **Ch. 33** (Event-based Concurrency) — explicit control over concurrency. .NET's `ThreadPool` is the runtime's mechanism for scheduling Tasks onto OS threads; capping it is the equivalent of capping the worker pool size in M6.
- **Ch. 27** (Thread API) — `ThreadPool.QueueUserWorkItem` is the kernel's thread pool; on Linux that's the `clone` syscall with `CLONE_THREAD` flags.

The lesson is that the .NET `ThreadPool` self-tunes: it starts with the minimum thread count and grows up to the maximum as needed. Setting the maximum below the natural demand causes Tasks to queue. This is *good* for resource control but *bad* for tail latency.

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
