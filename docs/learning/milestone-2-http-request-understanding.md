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

## C#/.NET Concept

`Socket.Receive(...)` fills a `byte[]` buffer. `Encoding.UTF8.GetString(...)` turns those bytes into a `string`. `HttpRequestParser.Parse(...)` turns that string into a small structured model.

The parser is pure code. It does not need a socket, port, browser, or OS network event. That is why it can be tested with direct strings.

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

## Next Question

The server now understands the path string, but it still ignores it. Milestone 3 will use the path to serve files from disk.

