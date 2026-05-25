# Milestone 1: Raw Socket Server

## What We Built

The host process creates a TCP server socket, binds it to port `8080`, listens for pending connections, blocks in `Accept()`, receives raw request bytes, sends a manually formatted HTTP response, and closes the client socket.

## Generic OS Concept

A server is a long-running process that asks the operating system for network resources. Calls such as bind, listen, accept, receive, and send cross the boundary between application code and the OS network stack.

`Accept()` is blocking I/O. When no client is waiting, the server thread cannot keep executing user code. The OS waits for a network event and schedules other runnable work until a client connection arrives.

## OSTEP Connection

OSTEP starts with the process abstraction: a program on disk becomes a running process only after the OS loads its code and data into memory, creates its stack and heap, initializes I/O state, and starts execution. This server is that kind of process. Its socket loop is ordinary user code running inside the process address space.

The process cannot directly control the network card. It runs in user mode, where hardware limits what application code can do. Socket operations ask the OS to act on the process's behalf. Conceptually, each operation such as bind, listen, accept, receive, or send crosses into the kernel through a system call. The CPU traps into the OS, runs privileged kernel code, then returns to the server.

OSTEP's process states explain why blocking calls are useful instead of wasteful:

- **Running**: the server is executing instructions on the CPU.
- **Ready**: the server could run, but the scheduler picked another process.
- **Blocked**: the server is waiting for an event such as network input.

When `Accept()` has no client to return, the server becomes blocked. The OS does not need to spin the CPU in the server loop. It can schedule another ready process until the network event arrives. When a client connects, the OS marks the server ready again, and the scheduler eventually lets it run.

This is the first place the project makes OSTEP visible: the server is not magic and the browser is not talking to C# directly. A user process asks the kernel for controlled access to a device-backed byte stream.

## C#/.NET Concept

`System.Net.Sockets.Socket` exposes TCP socket operations directly. `Socket.Accept()` blocks the current managed thread. `Socket.Receive()` copies bytes from the socket into a `byte[]` buffer. `Encoding.UTF8.GetString(...)` turns those bytes into text for learning and logging.

HTTP is written manually as bytes:

- status line
- headers
- blank line
- body

`Content-Length` must match the byte length of the response body.

## Common Misconceptions

TCP and HTTP are different layers. TCP gives the server a connection-oriented byte stream. HTTP is the text format the browser expects inside that stream. Sending only `Hello World!` over TCP is not the same thing as sending an HTTP response.

Blocking does not mean the whole computer is frozen. It means this server thread is waiting for a kernel-managed event. The OS scheduler can still run other processes.

The listening socket and the accepted client socket are different roles. The listening socket waits for new connections. The accepted client socket represents one client conversation and is the socket the server receives from and sends to.

## Experiment

Run the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Then send a request:

```powershell
curl http://localhost:8080/
```

Before a client connects, the server waits inside `Accept()`. After the connection is accepted, the server calls `Receive()` to read raw request bytes, logs them, sends `Hello World!`, closes the client socket, and returns to `Accept()`.

While the server is waiting, notice that your terminal is quiet but your machine is still responsive. That is the OS process state model in practice: the server is blocked, not burning CPU in user code.

## Browser Requests

One browser visit can create more than one HTTP request. For example, opening `http://localhost:8080/` may produce:

```text
GET / HTTP/1.1
GET /favicon.ico HTTP/1.1
```

The server logs once per accepted client socket, not once per thing the learner thinks of as a page visit. Browser refreshes, devtools, extensions, retries, and favicon loading can all create extra connections.

Use `curl` when you want a smaller experiment:

```powershell
curl http://localhost:8080/
```

With `curl`, one command usually creates one request, so the server log is easier to reason about.

## Next Question

The server can see request text such as `GET /ostep HTTP/1.1`, but it does not understand it yet. Milestone 2 will parse the raw HTTP request into method, path, version, and headers.
