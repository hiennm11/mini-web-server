# Milestone 7: Async / Event-Based Concurrency

## Question

How does a server handle many concurrent slow connections without one OS thread per connection?

## OSTEP Context

- Chapter(s): 33 (Event-based Concurrency)
- Concept: an event loop processes I/O events as they arrive instead of dedicating one thread per connection. The same OS thread can serve many connections: when one connection is waiting on I/O, the loop moves on to the next event.
- Key point from OSEP §33.1: scheduling is explicit and under the application's control — there is no preemption of thread systems to worry about. §33.5: no blocking calls are allowed, or the whole loop stalls. §33.6: asynchronous I/O is the OS-level primitive that makes this practical.

## C#/.NET Mechanism

- `Socket.AcceptAsync(ct)` / `Socket.ReceiveAsync(ArraySegment<byte>, SocketFlags)` / `Socket.SendAsync(ArraySegment<byte>, SocketFlags)` return `Task` (or `ValueTask`). The `await` keyword compiles the calling method into a continuation-based state machine.
- When a Task awaits I/O, the CLR returns control to the caller (or to the `Task.Run`/`ThreadPool` worker that invoked it). When the I/O completes, the continuation is scheduled back on the `ThreadPool`.
- The `async` keyword therefore maps directly onto OSEP §33.4: "no locks needed" only holds when the work is single-threaded. On .NET, the runtime uses the `ThreadPool` internally to run continuations, which means many short-running Tasks can run in parallel on multiple cores. The application code itself does not need locks for the accept and per-connection paths because each Task is independent.

## Build

Add a second server mode triggered by the `--async` command-line flag. The new mode uses async I/O throughout and does not use the worker pool.

Files affected:

- `src/MiniWebServer.Host/ServerConfig.cs` — new file with `public const int MaxRequestBytes` and `RaceIterations` so both servers can share the same values.
- `src/MiniWebServer.Host/AsyncServer.cs` — new file, ~180 lines. Single-threaded accept loop with `AcceptAsync`. Per-connection `Task` driving `ReceiveAsync` / `SendAsync`.
- `src/MiniWebServer.Host/Program.cs` — startup branches on `args.Contains("--async")` to choose between worker-pool mode and async mode.

Code shape in `AsyncServer`:

```csharp
public static async Task RunAsync(int port, string webRoot, CancellationToken ct)
{
    using var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));
    serverSocket.Listen(10);

    while (!ct.IsCancellationRequested)
    {
        Socket clientSocket = await serverSocket.AcceptAsync(ct);
        _ = HandleClientAsync(clientSocket, webRoot, ct);
    }
}
```

Per-connection handler:

```csharp
byte[] buffer = new byte[ServerConfig.MaxRequestBytes];
int total = 0;
int headerEnd = -1;
int contentLength = 0;

while (total < ServerConfig.MaxRequestBytes)
{
    int readBytes = await clientSocket.ReceiveAsync(
        new ArraySegment<byte>(buffer, total, buffer.Length - total),
        SocketFlags.None);
    if (readBytes <= 0) break;
    total += readBytes;
    // … HttpRequestReceiver.FindHeaderEnd / ParseContentLength checks …
}

byte[] responseBytes = response.ToBytes();
await clientSocket.SendAsync(new ArraySegment<byte>(responseBytes), SocketFlags.None);
```

## Experiment

Terminal 1 — start the server in async mode:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj -- --async
```

Terminal 2 — same stress test as slice 4.6 and M6: park 50 then 100 slow sockets, sample `/stats`:

```powershell
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

(Invoke-WebRequest -Uri http://localhost:8080/stats -UseBasicParsing).Content
[SlowClient]::OpenMany('127.0.0.1', 8080, 50)
(Invoke-WebRequest -Uri http://localhost:8080/stats -UseBasicParsing).Content
[SlowClient]::OpenMany('127.0.0.1', 8080, 100)
(Invoke-WebRequest -Uri http://localhost:8080/stats -UseBasicParsing).Content
```

Expected:

- `/stats` reports a thread count that grows from baseline (~11) to roughly 15-25 under load — the runtime is using the `ThreadPool` to schedule async continuations, but the growth is small per connection.
- The server processes requests on the first thread (the accept loop) and resumes them on whatever `ThreadPool` worker is free; one slow connection does not block others.

## Observation

Answer after running the experiment:

How many threads does the async server use to handle 150 concurrent slow connections?

Expected answer:

Significantly fewer than 150, but more than 1. .NET's async/await compiles the method to a state machine, but when an `await` parks, the continuation is scheduled on the `ThreadPool`. The runtime will grow the pool up to a limit, then either spin up more threads or queue the continuations.

Concrete numbers from this milestone's smoke run:

| Stage | threads | private_bytes |
|---|---|---|
| Baseline | 11 | 9.8 MB |
| After 50 parked | 20 | 63 MB |
| After 150 cumulative parked | 20 | 172 MB |

The thread count grew from 11 to 20 — about 9 extra threads for 150 concurrent slow connections. Compare:

| Slice | Server model | threads at 150 parked | private_bytes |
|---|---|---|---|
| Slice 4.6 | one thread per connection | ~150+ | 174 MB |
| Milestone 6 | worker pool of 8 | 17 | 21 MB |
| Milestone 7 | async / event-based | 20 | 172 MB |

The async server uses about the same memory as slice 4.6 because every parked connection still holds a 1 MB receive buffer (allocated by the handler) and the runtime's `ThreadPool` grows worker threads on demand. The thread count, however, is 20 instead of 150 — the application code itself never allocates one thread per connection. The OSEP §33 lesson is about the *application's* concurrency model, not the runtime's total thread count.

More precise answer:

The single-threaded event loop only holds the OS thread during the active portion of request processing (parse, log, build response bytes). During the 30-second `Task.Delay` in `/slow`, the connection has no thread at all — the `Task` is parked on the `ThreadPool`'s timer queue. When the timer fires, the continuation resumes on whichever worker is free. That is why the thread count stays in the 20s even with 150 parked clients: the runtime is multiplexing 150 waiting Tasks over a small set of `ThreadPool` workers, exactly the OSEP §33 event-loop idea translated to .NET.

## Three-Question Test

1. What is the OS doing?
   - The OS thread runs the async accept loop. When a connection awaits I/O, the thread is released back to the runtime; another connection's continuation can run on it. The OS-level syscall (`AcceptAsync`, `ReceiveAsync`) parks the calling thread only if no completion is immediately ready.
2. Which .NET API exposes it?
   - `Socket.AcceptAsync(ct)`, `Socket.ReceiveAsync(ArraySegment<byte>, SocketFlags)`, `Socket.SendAsync(...)`. `await` consumes the returned `Task` and the C# compiler rewrites the enclosing method into a state machine.
3. Where does it break at scale?
   - Each parked connection still holds its 1 MB receive buffer (set by `ServerConfig.MaxRequestBytes`). On the same 150-client workload the memory cost is comparable to slice 4.6. The win is the *thread count* and the *overlap* (one slow connection does not stall the accept loop or other handlers) — not raw memory. A production async server uses a pre-allocated buffer pool (`ArrayPool<byte>`) to avoid per-connection allocations.

## Learning Note

### What changed

Added `AsyncServer` and `ServerConfig` files. `Program.cs` now branches on `args.Contains("--async")` to choose between the existing worker-pool mode and the new async mode. The async mode uses `Socket.AcceptAsync` for the listen loop, then a `Task` per accepted connection that drives `Socket.ReceiveAsync` and `Socket.SendAsync`. `HttpRequestReceiver` is reused so the receive loop logic is identical between modes.

Files affected:

- `src/MiniWebServer.Host/ServerConfig.cs` — new file.
- `src/MiniWebServer.Host/AsyncServer.cs` — new file.
- `src/MiniWebServer.Host/Program.cs` — startup branches on `--async` flag.

### What I observed

Baseline `/stats` (async mode, no clients):

```text
threads = 11
working_set_bytes = 26632192
private_bytes = 9846784
total_requests = 1
async_active_tasks = 1
```

After parking 50 slow sockets (8 s):

```text
threads = 20
working_set_bytes = 82092032
private_bytes = 63426560
total_requests = 52
async_active_tasks = 52
```

After parking 100 more slow sockets (cumulative 150):

```text
threads = 20
working_set_bytes = 189624320
private_bytes = 172240896
total_requests = 153
async_active_tasks = 153
```

### OSEP concept

The async mode is the OSEP §33 event-based server translated to .NET's `async`/`await` + `Task` model. The accept loop is single-threaded and never blocks; each accepted connection becomes a `Task` that the runtime schedules on the `ThreadPool`. The `ThreadPool` worker count is small (~9 in this smoke), so 150 waiting connections are multiplexed over those workers rather than each getting its own OS thread.

The memory cost is not lower than M6 because each parked connection still holds the full 1 MB receive buffer and the runtime still allocates state machines, but the *thread* cost is dramatically lower. In slice 4.6 each parked client had its own dedicated OS thread; in M7 the application code shares a small worker set. This matches the OSEP goal: many connections with few threads at the application layer. The win is the ratio of connections-per-thread and the overlap behavior — one slow client does not freeze the accept loop or other handlers — not the raw memory footprint.

OSEP §33.4 says "no locks needed" for a single-CPU event server. On a multi-core machine with `ThreadPool` workers, the analysis is more nuanced: multiple handlers can be running concurrently on different cores, so shared mutable state still needs synchronization. The `/race` and `/race-safe` routes in the async server exercise the same race-fix lesson as M5 — the async model alone does not make shared-state updates safe.

### .NET mechanism

- `Socket.AcceptAsync(ct)` returns a `Task<Socket>`. Internally, the CLR registers an IOCP (I/O Completion Port) callback with Windows or an epoll-based callback on Linux. The calling thread is released as soon as the registration is in place.
- When a new connection arrives, the kernel completes the I/O, the IOCP/epoll wakes, and the runtime schedules the continuation. The thread that runs the continuation is whichever `ThreadPool` worker is free; it may be the same thread that called `AcceptAsync` (no context switch needed) or a different one.
- `await` is a syntactic transformation: the C# compiler rewrites the method into a state machine that records the current position, sets up the continuation callback, and returns the `Task`. When the I/O completes, the runtime calls back into the state machine at the right position.
- The handler's `Task.Delay(30000)` schedules a timer. While waiting, the connection's Task holds no OS thread at all — exactly the "I/O overlap without thread cost" property OSEP §33 calls out.

### Next question

What if we need to bound the runtime's total work? `ThreadPool` will grow up to `ThreadPool.GetMaxThreads()` (default hundreds), then start queueing continuations. Under sustained load we still need to decide what to do: queue, shed, or reject.

Next slice (future, if you want it): add `ThreadPool.SetMinThreads` / `SetMaxThreads` to bound the pool and observe how the async server behaves when the queue fills.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted