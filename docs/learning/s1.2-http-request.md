# Slice 1.2: HTTP Request Understanding

## Question

How does the server turn raw TCP bytes into an HTTP request with method, path, version, and headers?

## OSTEP Context

- Chapter(s): 4 (The Abstraction: The Process), 13 (The Abstraction: Address Spaces), 36 (I/O Devices)
- Concept: the OS moves bytes into process memory, but the application gives those bytes protocol meaning.
- Key point: TCP is a byte stream. HTTP is an application-level format layered on top of that stream.

The OS does not know that `GET / HTTP/1.1` is a request line. It only provides bytes through the socket. The server process decodes those bytes and parses the HTTP structure in user code.

This slice separates two kinds of work:

- I/O work: wait for the OS to provide bytes from the socket.
- CPU work: split text into request line and headers inside the process.

## C#/.NET Mechanism

- Type or API: `Socket.Receive(...)`, `Encoding.UTF8.GetString(...)`, `HttpRequestParser.Parse(...)`.
- Supporting model: `HttpRequest` with method, path, version, and headers.
- Why this, not another abstraction: parsing stays pure and testable while the socket lifecycle remains visible in `Program.cs`.

## Build

Add a tiny HTTP parser that:

1. Takes raw request text.
2. Splits the request into lines.
3. Parses the first line into:
   - method
   - path
   - version
4. Parses headers until the blank line.
5. Returns an unknown request for malformed input.
6. Logs the parsed result next to the raw request.

Files affected:

- `src/MiniWebServer.Host/HttpRequest.cs`
- `src/MiniWebServer.Host/HttpRequestParser.cs`
- `src/MiniWebServer.Host/Program.cs`
- `tests/MiniWebServer.Host.Tests/`

Keep the slice small:

- Do not route by path yet.
- Do not serve files yet.
- Do not parse request bodies yet.
- Keep the response fixed.

## Experiment

Run parser tests:

```powershell
dotnet run --project tests/MiniWebServer.Host.Tests/MiniWebServer.Host.Tests.csproj
```

Run the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Send requests:

```powershell
curl http://localhost:8080/
curl http://localhost:8080/ostep
```

Expected console output should show both raw bytes and parsed values:

```text
Method: GET
Path: /
Version: HTTP/1.1
Headers: ...
```

Then:

```text
Method: GET
Path: /ostep
Version: HTTP/1.1
Headers: ...
```

## Observation

Why can the parser be tested without opening a socket?

Expected answer:

Once bytes are decoded into text, parsing is normal CPU work inside the process. It does not need network I/O. The socket only provides bytes; the parser interprets those bytes.

## Three-Question Test

1. What is the OS doing?
   - It copies received network bytes into a buffer owned by the server process.
2. Which .NET API exposes it?
   - `Socket.Receive(...)` fills a `byte[]`; `Encoding.UTF8.GetString(...)` decodes the bytes; `HttpRequestParser.Parse(...)` interprets the text.
3. Where does it break at scale?
   - One `Receive()` call may not contain a full HTTP request. Large or slow requests require a receive loop, which becomes slice 1.4.

## Learning Note

### What changed

Added a small request model and parser. The server now logs structured request data: method, path, version, and header count.

### What I observed

Different URLs produce different parsed paths while the raw socket behavior stays the same.

### OSTEP concept

This slice shows the boundary between OS byte movement and application protocol interpretation. The OS delivers bytes; the process gives them meaning.

### .NET mechanism

`Socket.Receive` provides bytes. `Encoding.UTF8` decodes text. `HttpRequestParser` is pure CPU logic and can be tested without sockets.

### Next question

The server understands the request path. How can it use that path to read persistent bytes from the file system?

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted
