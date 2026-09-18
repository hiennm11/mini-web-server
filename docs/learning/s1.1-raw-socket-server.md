# Slice 1.1: Raw Socket Server

## Question

What does a minimal web server do before HTTP parsing, routing, or files exist?

## OSTEP Context

- Chapter(s): 4 (The Abstraction: The Process), 4.4 (Process States), 6 (Limited Direct Execution)
- Concept: a server is a user process that asks the OS for controlled access to network resources.
- Key point: socket operations cross the user/kernel boundary. The process cannot talk to the network device directly.

A program on disk becomes a process when the OS loads it, creates its address space, initializes runtime state, and starts executing instructions. This server is one process with one execution path.

The socket lifecycle makes the OS boundary visible:

```text
Socket() -> Bind() -> Listen() -> Accept() -> Receive() -> Send() -> Close()
```

`Accept()` and `Receive()` are blocking I/O. If no client or bytes are ready, the OS can put the server thread into a blocked state and run other work.

## C#/.NET Mechanism

- Type or API: `System.Net.Sockets.Socket`.
- Calls: `Bind`, `Listen`, `Accept`, `Receive`, `Send`, `Shutdown`, `Close`.
- Why this, not another abstraction: raw sockets expose the lifecycle directly. ASP.NET Core, Kestrel, and `HttpListener` hide the OS-facing steps this project is meant to teach.

## Build

Build a single-threaded TCP server that:

1. Creates a TCP socket.
2. Binds to `IPAddress.Any` on port `8080`.
3. Starts listening with a small backlog.
4. Blocks in `Accept()` until a client connects.
5. Receives raw request bytes into a fixed buffer.
6. Logs the decoded request text.
7. Sends a manually formatted HTTP response.
8. Closes the client socket.
9. Loops back to `Accept()`.

Files affected:

- `src/MiniWebServer.Host/Program.cs`

Keep the slice small:

- No request parser yet.
- No static files yet.
- No concurrency yet.
- No async yet.
- One client at a time.

## Experiment

Run the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Send a request:

```powershell
curl -i http://localhost:8080/
```

Expected:

- The server blocks quietly before the client connects.
- After `curl`, the server logs raw request text.
- The client receives a valid HTTP response.
- The server closes the client socket and returns to `Accept()`.

What to notice:

```text
GET / HTTP/1.1
Host: localhost:8080
User-Agent: curl/...
```

The browser or `curl` is not talking to C# directly. The client talks to the OS network stack. The server process receives bytes from the OS through the socket API.

## Observation

Why does the terminal stay quiet before a client connects?

Expected answer:

The server is blocked inside `Accept()`. The OS knows no client is ready, so the server thread does not keep burning CPU. When a client connects, the OS wakes the server so it can accept the connection.

## Three-Question Test

1. What is the OS doing?
   - It owns the TCP port, queues incoming connections, blocks the server when no client is ready, and wakes it when a connection arrives.
2. Which .NET API exposes it?
   - `Socket.Bind`, `Socket.Listen`, `Socket.Accept`, `Socket.Receive`, and `Socket.Send`.
3. Where does it break at scale?
   - One blocking server thread handles one client at a time. A slow client delays every later client.

## Learning Note

### What changed

Built the first raw socket server loop. It accepts a TCP client, reads bytes, sends a minimal HTTP response, closes the client socket, and waits for the next client.

### What I observed

The server sits quietly until a client connects. After `curl`, it logs the raw HTTP request bytes and sends a response.

### OSTEP concept

This slice shows the process abstraction and process states. The server is a user process. It enters a blocked state while waiting for network events and runs again when the OS has work for it.

### .NET mechanism

`System.Net.Sockets.Socket` exposes the TCP lifecycle directly. The socket calls are the application-level view of OS-managed networking.

### Next question

The server can see text such as `GET / HTTP/1.1`, but it does not understand it yet. How do raw bytes become a structured HTTP request?

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted
