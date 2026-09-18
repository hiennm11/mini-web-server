# Milestone 7: Async / Event-Based Concurrency

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does a server handle many concurrent slow connections without one OS thread per connection?

## Scope

Add a second server mode triggered by the `--async` command-line flag. The new mode uses async I/O throughout: a single OS thread runs the accept loop via `AcceptAsync`; each connection is a `Task` driving `ReceiveAsync` / `SendAsync`. No worker pool.

## Slice

- **[s1-async-event-based.md](./s1-async-event-based.md)** — `AsyncServer` class with single-threaded accept loop + per-connection Task. `Program.cs` startup branches on `--async`.

## OSEP coverage

The whole chapter is the reference:

- **Ch. 33 Event-based Concurrency (Advanced)**
  - §33.1 The Basic Idea: An Event Loop — `while(1) { events = getEvents(); for (e in events) processEvent(e); }`. Our accept loop is a more sophisticated version of this.
  - §33.2 An Important API: select() (or poll()) — non-blocking I/O multiplexing. .NET's `Socket.AcceptAsync` / `ReceiveAsync` / `SendAsync` are the OS equivalent.
  - §33.4 Why Simpler? No Locks Needed — on a single CPU, only one event handler runs at a time. Our accept loop is single-threaded.
  - §33.5 A Problem: Blocking System Calls — "no blocking calls are allowed" or the whole loop stalls.
  - §33.6 A Solution: Asynchronous I/O — `aio_read` / `aio_error` on Mac, similar on Linux.
  - §33.7 Another Problem: State Management — manual stack management via continuations; we avoid this via C#'s async/await state machines.

## OSEP §-specific deviations

- OSEP §33.1 pseudocode is what our `AsyncServer.RunAsync` looks like in spirit (loop + dispatch + handle).
- OSEP §33.6 uses POSIX async I/O. .NET's async/await is a higher-level abstraction that the runtime translates to OS async I/O or thread-pool dispatch depending on the API.
- OSEP §33.7 manual stack management — we DON'T do this. C# `async`/`await` handles the state machine for us.
- OSEP §33.4 says no locks needed on single CPU — our accept loop is single-threaded. But each per-connection Task can run on different ThreadPool threads (multicore), so we DO need locks for any shared state.

## Key OSEP quotes

> "Don't Block In Event-Based Servers" (OSEP §33.5) — TIP

> "In a multi-threaded application, the developer has little or no control over what is scheduled at a given moment in time." (OSEP §33 intro) — event-based gives control.

## .NET mechanism

- `Socket.AcceptAsync(ct)` / `Socket.ReceiveAsync(...)` / `Socket.SendAsync(...)` return `Task`. The `await` keyword compiles the calling method into a continuation-based state machine.
- When a Task awaits I/O, the CLR returns control to the caller (or to the `Task.Run`/`ThreadPool` worker). When I/O completes, the continuation is scheduled back on the `ThreadPool`.
- The runtime uses `ThreadPool` internally; many short-running Tasks can run in parallel on multiple cores.

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
