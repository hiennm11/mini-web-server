# Milestone 1: Raw Socket Server

## What We Built

The host process creates a TCP server socket, binds it to port `8080`, listens for pending connections, blocks in `Accept()`, receives raw request bytes, sends a manually formatted HTTP response, and closes the client socket.

## Generic OS Concept

A server is a long-running process that asks the operating system for network resources. Calls such as bind, listen, accept, receive, and send cross the boundary between application code and the OS network stack.

`Accept()` is blocking I/O. When no client is waiting, the server thread cannot keep executing user code. The OS waits for a network event and schedules other runnable work until a client connection arrives.

## C#/.NET Concept

`System.Net.Sockets.Socket` exposes TCP socket operations directly. `Socket.Accept()` blocks the current managed thread. `Socket.Receive()` copies bytes from the socket into a `byte[]` buffer. `Encoding.UTF8.GetString(...)` turns those bytes into text for learning and logging.

HTTP is written manually as bytes:

- status line
- headers
- blank line
- body

`Content-Length` must match the byte length of the response body.

## Experiment

Run the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Then send a request:

```powershell
curl http://localhost:8080/
```

Before the request arrives, the server waits inside `Accept()`. After the request arrives, the server receives raw bytes, logs them, sends `Hello World!`, closes the client socket, and returns to `Accept()`.

## Next Question

The server can see request text such as `GET /ostep HTTP/1.1`, but it does not understand it yet. Milestone 2 will parse the raw HTTP request into method, path, version, and headers.
