# Slice 1.4: Robust Request Receive

## Question

Why is one `Socket.Receive()` call not enough to read an HTTP request reliably?

## OSTEP Context

- Chapter(s): 4.4 (Process States), 36 (I/O Devices)
- Concept: TCP exposes a byte stream. The OS wakes the server when some bytes are available, not when a complete HTTP request is available.
- Key point: blocking I/O lets the server sleep while waiting for more bytes, but the application must decide when it has received enough protocol data.

The current server reads once into a fixed buffer. That works for tiny `curl` requests, but it is not a property guaranteed by TCP or the OS.

Important distinction:

- TCP: ordered bytes.
- HTTP: application-level message format built on those bytes.
- `Receive()`: copies currently available bytes from the socket into process memory.
- Request completeness: decided by server code, usually by finding `\r\n\r\n` and checking `Content-Length`.

## C#/.NET Mechanism

- Type or API: `Socket.Receive(byte[])` in a loop.
- Supporting logic: accumulate bytes, detect header terminator, parse `Content-Length`, stop after expected body bytes are received.
- Why this, not another abstraction: this keeps the raw socket lifecycle visible and avoids hiding TCP stream behavior behind higher-level HTTP APIs.

## Build

Replace the single receive call with a small request-receiving routine.

Behavior to build:

1. Keep receiving until the HTTP header terminator `\r\n\r\n` is found.
2. Parse headers enough to find `Content-Length`.
3. If no body is expected, stop after headers are complete.
4. If a body is expected, keep receiving until the accumulated body byte count reaches `Content-Length`.
5. Put a maximum request size guard in place so a client cannot grow memory forever.
6. Return the accumulated request bytes to the existing parsing/logging flow.

Files likely affected:

- `src/MiniWebServer.Host/Program.cs`
- Optional pure helper: `src/MiniWebServer.Host/HttpRequestReceiver.cs`
- Optional tests: `tests/MiniWebServer.Host.Tests/...`

Keep the slice small:

- Do not add keep-alive.
- Do not add chunked transfer decoding yet.
- Do not add async.
- Do not refactor the full server loop.

Suggested limits:

- Header limit: 16 KB.
- Full request limit: 1 MB.
- If the client exceeds the limit, close the connection or return `400 Bad Request` in a later slice.

## Experiment

Run tests:

```powershell
dotnet run --project tests/MiniWebServer.Host.Tests/MiniWebServer.Host.Tests.csproj
```

Run the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Then send a normal request:

```powershell
curl -i http://localhost:8080/
```

Expected:

- Server still serves `/` as before.
- Existing parser logs still work.

Then send a request slowly from a raw TCP client. Example concept:

```powershell
$client = [System.Net.Sockets.TcpClient]::new('localhost', 8080)
$stream = $client.GetStream()
$part1 = [Text.Encoding]::ASCII.GetBytes("GET / HTTP/1.1`r`nHost: localhost`r`n")
$part2 = [Text.Encoding]::ASCII.GetBytes("User-Agent: slow-client`r`n`r`n")
$stream.Write($part1, 0, $part1.Length)
Start-Sleep -Seconds 2
$stream.Write($part2, 0, $part2.Length)
$buffer = New-Object byte[] 4096
$read = $stream.Read($buffer, 0, $buffer.Length)
[Text.Encoding]::UTF8.GetString($buffer, 0, $read)
$client.Close()
```

Expected:

- Before this slice, the server may parse only the first fragment and treat the request as malformed or incomplete.
- After this slice, the server waits for the second fragment, sees `\r\n\r\n`, then handles the request normally.

## Observation

Answer after running the experiment:

Why did the server need to wait for the second fragment before parsing?

Expected answer:

TCP did not preserve the application's idea of an HTTP request. The first `Receive()` returned only bytes currently available. The OS had no reason to wait for `\r\n\r\n`; that delimiter belongs to HTTP, so the server code must accumulate bytes until the delimiter appears.

## Three-Question Test

1. What is the OS doing?
   - It wakes the server when socket bytes are available and copies those bytes into process memory. It does not assemble complete HTTP messages.
2. Which .NET API exposes it?
   - `Socket.Receive(...)` returns the number of bytes copied on this call. The receive loop decides whether to call it again.
3. Where does it break at scale?
   - Without size limits, a slow or malicious client can hold the server thread and grow memory. With one thread, a slow partial request still blocks all later clients until Phase 2 adds concurrency.

## Learning Note

After observing, write a short note here:

### What changed

TBD after implementation.

### What I observed

TBD after experiment.

### OSTEP concept

TBD after experiment.

### .NET mechanism

TBD after experiment.

### Next question

If robust receive can wait for slow clients, how does the single-threaded server behave when one client sends headers very slowly?

## Status

- [x] Planned
- [ ] Built
- [ ] Experimented
- [ ] Noted
