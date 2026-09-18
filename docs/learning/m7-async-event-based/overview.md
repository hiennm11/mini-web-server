# Milestone 7: Async / Event-Based Concurrency

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does a server handle many concurrent slow connections without one OS thread per connection?

## Scope

Add a second server mode triggered by the `--async` command-line flag. The new mode uses async I/O throughout: a single OS thread runs the accept loop via `AcceptAsync`; each connection is a `Task` driving `ReceiveAsync` / `SendAsync`. No worker pool.

## Slice

- **[s1-async-event-based.md](./s1-async-event-based.md)** — `AsyncServer` class with single-threaded accept loop + per-connection Task. `Program.cs` startup branches on `--async`.

## OSEP concept

- **Ch. 33 Event-based Concurrency**
  - §33.1: scheduling is explicit and under the application's control — no preemption of thread systems to worry about.
  - §33.4: "no locks needed" only holds when the work is single-threaded.
  - §33.5: no blocking calls allowed, or the whole loop stalls.
  - §33.6: asynchronous I/O is the OS-level primitive that makes this practical.

## .NET mechanism

- `Socket.AcceptAsync(ct)` / `Socket.ReceiveAsync(ArraySegment<byte>, SocketFlags)` / `Socket.SendAsync(ArraySegment<byte>, SocketFlags)` return `Task`. The `await` keyword compiles the calling method into a continuation-based state machine.
- When a Task awaits I/O, the CLR returns control to the caller (or the `ThreadPool` worker that invoked it). When the I/O completes, the continuation is scheduled back on the `ThreadPool`.
- The runtime uses the `ThreadPool` internally to run continuations. Many short-running Tasks can run in parallel on multiple cores; the accept loop itself runs single-threaded.

## Comparison to M6

| Mode | Threads per connection | Idle behavior |
|---|---|---|
| M6 worker pool | 1 worker thread per busy connection (capped at pool size) | Thread blocks on `Receive()` |
| M7 async mode | 0 OS threads per connection (continuation parked) | Continuation resumed on `ThreadPool` when I/O completes |

Both modes handle 150 parked clients; async mode uses dramatically less memory (M15 measures the actual delta).

## Files

- `src/MiniWebServer.Host/AsyncServer.cs` — accept loop + `HandleClientAsync`.
- `src/MiniWebServer.Host/ServerConfig.cs` — shared constants (`MaxRequestBytes`, `RaceIterations`).
- `src/MiniWebServer.Host/Program.cs` — startup branches on `args.Contains("--async")`.

## Where this leads

- M15 (`m15-arraypool/`) — `ArrayPool<byte>` to reduce per-connection memory in async mode.
- M10 (`m10-threadpool-cap/`) — show that the built-in `ThreadPool` has similar properties.
