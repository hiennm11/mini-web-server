# Slice 4.1: Prove Single-Thread Blocking

## Question

Why does a slow request block all other clients in the current single-threaded server?

## OSTEP Context

- Chapter(s): 4 (The Abstraction: The Process), 4.4 (Process States)
- Concept: a process in the **Blocked** state cannot execute instructions. The OS moves it from Running to Blocked when it waits for a slow event, then moves it back to Ready when that event completes.
- Key point from NotebookLM query: when a process blocks on slow I/O or a wait, the OS scheduler stops running it and schedules other ready work. The blocked process cannot continue until the event completes.

For this server, that means one blocked handler freezes the only accept loop. The kernel may queue new TCP connections in the listen backlog, but the server thread cannot call `Accept()` again until `HandleClient()` returns.

Detailed state mapping:

```text
Running: the server thread is executing C# instructions.
Ready: the server thread can run, but the OS scheduler is running something else.
Blocked: the server thread cannot run until a waited-for event completes.
```

In this slice, the visible transitions are:

```text
Server starts and reaches Accept()
  -> Blocked if no client is ready

Client connects
  -> Ready, then Running when the scheduler runs the server

Server enters HandleClient(/slow)
  -> Running while receiving, parsing, and logging

Server calls Thread.Sleep(5000)
  -> Blocked for about 5 seconds

Sleep timer completes
  -> Ready, then Running

Server sends response and loops back to Accept()
  -> Now it can accept the next queued client
```

Important distinction from the NotebookLM query:

- The application thread is blocked in user-mode server code.
- The OS kernel is still alive and can manage the TCP port.
- A second client can complete the TCP connection at the kernel level and wait in the listen backlog.
- The application does not call `Accept()` on that queued connection until the single server thread wakes up and returns to the top of the loop.

This is why the second `curl` appears to hang instead of immediately failing.

Teaching points:

1. Blocking is useful for CPU efficiency: the sleeping server thread does not waste CPU cycles.
2. Blocking is harmful for responsiveness in a single-threaded server: one blocked handler stalls every later client.
3. The kernel can queue connections independently from the application accepting them.
4. Concurrency becomes necessary when the server must keep accepting clients while one client is blocked.

Misconception to avoid:

> If the server app is blocked, new clients should get `Connection refused`.

Wrong. The socket is still listening in the kernel. New clients can connect and wait in the backlog. The problem is not that the port disappeared. The problem is that the user-mode server thread is not calling `Accept()` yet.

## C#/.NET Mechanism

- Type or API: `Thread.Sleep(int milliseconds)`.
- Why this, not another abstraction: `Thread.Sleep` is a tiny, visible way to force the current managed thread into a blocked wait. It proves the behavior without introducing threads, async, timers, or higher-level server abstractions.

The current control flow is:

```text
Accept() -> HandleClient() -> Receive() -> Parse() -> Respond() -> Close() -> loop back to Accept()
```

During `/slow`, the flow becomes:

```text
Accept() -> HandleClient() -> Parse(/slow) -> Sleep(5s) -> Respond() -> Close() -> loop back to Accept()
```

While sleeping, the loop has not returned to `Accept()`.

## Build

Add one deliberate slow path.

When the parsed request path is `/slow`, sleep for 5 seconds before creating and sending the response. All other paths keep the current behavior.

File affected:

- `src/MiniWebServer.Host/Program.cs`

Expected code shape inside `HandleClient`, after parsed request logging and before response creation:

```csharp
if (parsedRequest.Path == "/slow")
{
    Console.WriteLine("Sleeping 5000 ms to simulate blocking I/O...");
    Thread.Sleep(5000);
}
```

No threading. No refactoring. No new routing abstraction.

## Experiment

Use three terminals.

Terminal 1 — start the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Terminal 2 — start a slow request:

```powershell
curl http://localhost:8080/slow
```

Terminal 3 — while Terminal 2 is still waiting, request the normal index:

```powershell
curl http://localhost:8080/
```

Expected result:

- Terminal 2 waits for about 5 seconds.
- Terminal 3 also waits if started during those 5 seconds.
- Terminal 3 responds only after Terminal 2 finishes.
- The server console shows the second client is accepted after the `/slow` handler completes.

What to notice in the server console:

```text
Accepted client socket from ...
Path: /slow
Sleeping 5000 ms to simulate blocking I/O...
... five second gap ...
Sent ... response bytes.
Closed client socket.

Accepted client socket from ...
Path: /
```

The second client connected during the gap, but the application did not accept it during the gap.

## Observation

Answer after running the experiment:

Why did the second client wait even though it requested a different path?

Expected answer:

The server has one thread and one execution point. It was blocked inside `Thread.Sleep` in `HandleClient(/slow)`. The accept loop had not returned to `Accept()`, so the second client could not be accepted by the application yet. The OS could queue the connection, but the application thread could not process it.

More precise answer:

The second TCP client may connect at the kernel level while `/slow` is sleeping. That does not mean the application has accepted it. `Accept()` is the handoff point from the kernel's completed-connection queue into the server process. Because the only server thread is blocked in `Thread.Sleep`, it cannot execute that handoff until the sleep completes.

## Three-Question Test

1. What is the OS doing?
   - It moves the server thread into a blocked wait during `Thread.Sleep`. The thread cannot execute server code until the wait completes.
2. Which .NET API exposes it?
   - `Thread.Sleep(int)` blocks the current managed thread. The synchronous `Socket.Accept()` plus `HandleClient()` loop serializes client handling onto that one thread.
3. Where does it break at scale?
   - One slow client stalls all other clients. A slow disk read, slow network operation, or slow computation would freeze the whole server until it finishes.

## Learning Note

### What changed

Added a `/slow` path that sleeps for 5 seconds before responding.

### What I observed

The experiment started `/slow`, waited until the server logged `Sleeping 5000 ms to simulate blocking I/O...`, then requested `/` while the slow handler was sleeping.

Observed result:

```text
fast_seconds_while_slow_sleeping=4.97
```

The `/` request waited almost the full 5 seconds. The server console accepted the `/` client only after the `/slow` handler finished:

```text
Path: /slow
Sleeping 5000 ms to simulate blocking I/O...
Response: 404 Not Found
Sent 114 response bytes.
Closed client socket.

Accepted client socket from 127.0.0.1:50886
Path: /
Response: 200 OK
```

`/slow` returns `404 Not Found` after sleeping because no `wwwroot/slow` file exists. The status code is not the point of this slice. The blocking delay is the point.

### OSTEP concept

Process states explain the behavior. Running code can block on an event. While blocked, it cannot also accept new work. With one thread, the whole server is blocked.

The important OSTEP transition is:

```text
Running -> Blocked -> Ready -> Running
```

`Thread.Sleep(5000)` caused the server thread to leave Running and enter Blocked. The OS could run other processes during that time, but this server had no second thread to continue accepting clients. When the sleep timer completed, the server became Ready, then Running again, sent the `/slow` response, closed that client socket, and only then accepted the queued `/` client.

The second client did not prove the kernel was blocked. It proved the user-mode application thread was blocked. The kernel could still hold the connection in the listen backlog.

### .NET mechanism

`Thread.Sleep(5000)` blocks the current managed thread. The current server uses synchronous socket APIs and a single accept loop, so no other server work can continue.

### Next question

If a blocked handler freezes the accept loop, can the server keep accepting clients by moving each client handler onto its own thread?

Next slice: Slice 4.2 — Spawn One Thread Per Client.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted
