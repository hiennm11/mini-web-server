# Milestone 2: HTTP Request Understanding

## What We Built

The host still receives raw bytes from a TCP client socket, decodes them as UTF-8 text, and logs the raw request. It now also parses that text into:

- method
- path
- version
- headers

For example:

```text
GET /ostep HTTP/1.1
Host: localhost:8080
User-Agent: curl/8.13.0
Accept: */*
```

becomes:

```text
Method: GET
Path: /ostep
Version: HTTP/1.1
Headers: 3
```

## Generic OS Concept

TCP gives the program a byte stream. It does not give the server a ready-made HTTP request object.

The server chooses how many bytes to read into memory, then application code interprets those bytes according to the HTTP protocol. This is the boundary between transport and protocol:

- TCP: ordered bytes between sockets
- HTTP: meaning layered on top of those bytes

## OSTEP Connection

OSTEP describes a process by its machine state, especially the address space it can read and write. The receive buffer is part of that story. The OS copies incoming network data into memory owned by the server process, and only then can the C# code inspect it.

The user/kernel boundary still matters here. The network device and OS network stack deal with packets, interrupts, buffers, and byte movement. The application receives bytes in a `byte[]`. The OS does not turn those bytes into `Method`, `Path`, `Version`, or `Headers`; those are application-level meanings created by the parser.

This is why `Receive()` and `HttpRequestParser.Parse(...)` are different responsibilities:

- `Receive()` asks the OS for bytes from the client socket.
- UTF-8 decoding turns those bytes into text for this learning server.
- The parser interprets the text according to HTTP rules.

OSTEP's thread model also gives a design warning for later milestones. Threads in one process share the same address space, but each thread has its own stack. Per-request parsing state should stay in local variables or request objects, not in global mutable parser fields. That makes the parser easier to test now and safer when multiple client handlers exist later.

## C#/.NET Concept

`Socket.Receive(...)` fills a `byte[]` buffer. `Encoding.UTF8.GetString(...)` turns those bytes into a `string`. `HttpRequestParser.Parse(...)` turns that string into a small structured model.

The parser is pure code. It does not need a socket, port, browser, or OS network event. That is why it can be tested with direct strings.

## Common Misconceptions

A single `Receive()` call is not a promise to return a whole HTTP request. TCP is a stream, not a message format. This milestone accepts that simplification for learning, but the limitation is important.

The request line is not special to the OS. `GET /ostep HTTP/1.1` is just bytes until application code decides to split lines and fields.

Parsing should not depend on the current socket. Once bytes have become text, parsing can be pure code. That separation is what makes the parser testable.

## Experiment

Run parser tests:

```powershell
dotnet run --project tests/MiniWebServer.Host.Tests/MiniWebServer.Host.Tests.csproj
```

Run the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Then send:

```powershell
curl http://localhost:8080/
curl http://localhost:8080/ostep
```

The raw request still appears in the log, but the server now also shows the parsed method, path, version, and header count.

To connect this to OSTEP, watch the boundary between two kinds of work:

- I/O work: wait for the OS to provide bytes from the socket.
- CPU work: split text into request parts inside the process.

Later concurrency milestones will treat these differently. A thread may block on I/O, but parsing is ordinary CPU work once the data is in memory.

## Next Question

The server now understands the path string, but it still ignores it. Milestone 3 will use the path to serve files from disk.
