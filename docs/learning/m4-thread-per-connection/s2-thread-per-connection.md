# Slice 4.2: Spawn One Thread Per Client

## Question

If one blocked handler freezes the accept loop, can the server keep accepting clients by moving each handler to its own thread?

## OSTEP Context

- Chapter(s): 26 (Concurrency: An Introduction), 27 (Thread API)
- Concept: a thread is a separate point of execution with its own program counter, register set, and stack. Multiple threads inside one process share the same address space.
- Key point from NotebookLM query: a multi-threaded program has more than one point of execution. When one thread blocks on I/O or a wait, the OS scheduler can run another ready thread in the same process.

For this server, that means the accept loop and a slow client handler can become separate execution points. The `/slow` handler can block in `Thread.Sleep` (originally 5000 ms; later extended to 30000 ms for s4.6 stress tests) while the main server thread returns to `Accept()` and accepts another client.

Detailed thread mapping:

```text
Main thread:
  Bind -> Listen -> Accept -> spawn handler thread -> loop back to Accept

Handler thread A:
  HandleClient(/slow) -> Sleep(5000) -> Send response -> Close socket -> exit

Handler thread B:
  HandleClient(/) -> Send response -> Close socket -> exit
```

Important distinction from the NotebookLM query:

- Threads are not separate processes.
- They share the same process address space: code, heap, and static data.
- Each thread has its own stack, so handler-local variables belong to that handler thread.
- Shared data will become dangerous later. This slice does not add shared counters or locks.
- A thread-per-connection web server usually does not `join` handler threads. The main thread keeps accepting new clients while handlers finish independently.

Teaching points:

1. A thread is another point of execution inside the same process.
2. A blocked handler thread does not have to block the accept thread.
3. The OS scheduler decides which ready thread runs next.
4. Console output may begin to interleave because multiple handlers can run at the same time.
5. Unbounded thread creation works for this lesson, but it breaks at scale because every thread needs stack memory and scheduler time.

Misconception to avoid:

> Adding threads makes the socket operations non-blocking.

Wrong. `Accept()`, `Receive()`, `Send()`, and `Thread.Sleep()` are still blocking calls. The difference is that blocking now affects one thread, not the whole host.

## C#/.NET Mechanism

- Type or API: `Thread`, `Thread.Start()`, `Thread.IsBackground`.
- Why this, not another abstraction: `Thread` exposes the OS-thread idea directly. `Task.Run`, `ThreadPool`, and `async` hide too much for this lesson and belong to later phases.

The current control flow is:

```text
Accept() -> HandleClient() -> Receive() -> Parse() -> Respond() -> Close() -> loop back to Accept()
```

During `/slow`, slice 4.1 proved this becomes:

```text
Accept() -> HandleClient(/slow) -> Sleep(5s) -> Respond() -> Close() -> loop back to Accept()
```

The new control flow should be:

```text
Accept() -> start Thread(HandleClient) -> loop back to Accept()
                             |
                             v
                  Receive() -> Parse() -> Respond() -> Close() -> handler thread exits
```

The accept loop must not wait for the handler thread to finish.

## Build

Modify the accept loop so each accepted client is handled by a new background thread.

File affected:

- `src/MiniWebServer.Host/Program.cs`

Expected code shape inside the `while (true)` loop, after `Accept()` returns:

```csharp
Socket clientSocket = serverSocket.Accept();

var thread = new Thread(() => HandleClient(clientSocket, webRoot));
thread.IsBackground = true;
thread.Start();
```

Keep the existing per-client error handling inside or around the handler path. Do not add locks, counters, thread pools, async sockets, cancellation, or a routing abstraction.

No `Thread.Join()`. Joining would make the accept loop wait and would recreate the single-thread blocking behavior.

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
- Terminal 3 responds quickly if started while Terminal 2 is sleeping.
- The server console shows the `/` client is accepted before the `/slow` handler closes its socket.
- The `/` response may finish before the `/slow` response.

What to notice in the server console:

```text
Accepted client socket from ...
Path: /slow
Sleeping 5000 ms to simulate blocking I/O...

Accepted client socket from ...
Path: /
Response: 200 OK
Sent ... response bytes.
Closed client socket.

Response: 404 Not Found
Sent ... response bytes.
Closed client socket.
```

The second client no longer waits for the slow handler to return before the application accepts it.

## Observation

Answer after running the experiment:

Why can the server accept and respond to `/` while `/slow` is still sleeping?

Expected answer:

The server now has more than one point of execution. The main accept thread creates a handler thread for `/slow`, then immediately loops back to `Accept()`. When `/slow` blocks in `Thread.Sleep`, only that handler thread is blocked. The accept thread can still accept `/`, create another handler thread, and let that request complete.

More precise answer:

The OS scheduler treats the main thread, the `/slow` handler thread, and the `/` handler thread as separate schedulable entities. The `/slow` thread is blocked on a timer. The accept thread and `/` handler thread can still be ready or running. This is how threads overlap slow waits with other useful server work.

## Three-Question Test

1. What is the OS doing?
   - It schedules multiple threads in one process. When one handler thread blocks, other ready threads from the same process can still run.
2. Which .NET API exposes it?
   - `new Thread(() => HandleClient(...)).Start()` creates a new managed thread backed by an OS thread. `IsBackground = true` prevents handler threads from keeping the process alive after the foreground thread exits.
3. Where does it break at scale?
   - One thread per connection is unbounded. Many connections mean many stacks, many scheduler decisions, and heavy context switching. This motivates the later worker-pool and bounded-queue slices.

## Learning Note

### What changed

Modified the accept loop in `src/MiniWebServer.Host/Program.cs` to spawn a new background `Thread` per accepted client. The accept thread no longer calls `HandleClient` directly; it starts a thread and immediately returns to `Accept()`. Per-client error handling (`SocketException` and general `Exception` catches) moved into the new thread body so a failed client does not stop the host, and the client socket is disposed in each catch path. The handler method signature and `using (clientSocket)` block are unchanged. No locks, no counters, no thread pool, no `Task.Run`, no `async`, no `Thread.Join`. `IsBackground = true` so handler threads do not keep the process alive.

### What I observed

Verified with a live experiment using `Invoke-WebRequest` on Windows PowerShell:

**Before the change (single-thread baseline)**:
- `/slow` elapsed: ~8665 ms
- `/` elapsed: ~6859 ms
- Server log showed `/` was only accepted **after** `/slow` closed its socket

**After the change (thread-per-client)**:
- `/slow` elapsed: ~7890 ms (handler still sleeps 5s)
- `/` elapsed: ~2144 ms (client-side PowerShell overhead; server-side response is immediate)
- Server log shows the critical ordering proof:
  ```
  Sleeping 5000 ms to simulate blocking I/O...   <- /slow still sleeping
  Accepted client socket from 127.0.0.1:54448   <- / accepted WHILE /slow sleeps
  Response: 200 OK
  Sent 324 response bytes.
  Closed client socket.   <- / finishes first
  Response: 404 Not Found   <- /slow responds after sleep
  ```
- `/` is accepted and responds before `/slow` finishes. The accept loop is no longer blocked by the slow handler.

### OSTEP concept

A thread is a separate point of execution with its own program counter, register set, and stack. Multiple threads in one process share the same address space. When the `/slow` handler thread blocks in `Thread.Sleep` (5000 ms when this slice ran; later extended to 30000 ms), the OS scheduler can still run the accept thread and other handler threads from the same process. The single-thread blocking problem (slice 4.1) is solved by giving each accepted client its own execution point.

### .NET mechanism

`new Thread(() => HandleClient(clientSocket, webRoot))` creates a managed thread backed by an OS thread. `thread.IsBackground = true` marks it as a background thread so it does not prevent process exit. `thread.Start()` begins execution. The lambda captures the per-iteration `clientSocket` local — each thread sees its own client socket. No `Thread.Join()` is used; the accept thread must return to `Accept()` immediately to accept new clients.

### Next question

If multiple handlers can run at the same time, can requests finish in an order different from the order they arrived?

Next slice: Slice 4.3 — Observe Scheduling Non-Determinism.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted
