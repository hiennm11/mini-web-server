# Slice 1.4: Robust Request Receive

## Question

Why is one `Socket.Receive()` call not enough to read an HTTP request reliably?

## OSTEP Context

- Chapter(s): 4.4 (Process States), 36 (I/O Devices)
- Concept: TCP exposes a byte stream. The OS wakes the server when some bytes are available, not when a complete HTTP request is available.
- Key point: blocking I/O lets the server sleep while waiting for more bytes, but the application must decide when it has received enough protocol data.

TCP: ordered bytes. HTTP: application-level message format built on those bytes. `Receive()` returns whatever bytes the OS has ready; "request completeness" is a decision the server code makes by looking for `\r\n\r\n` and respecting `Content-Length`.

## C#/.NET Mechanism

- `Socket.Receive(byte[], int offset, int count, SocketFlags)` writes currently-available bytes into the buffer at the given offset.
- `ReadOnlySpan<byte>` and `Span<byte>.IndexOf(ReadOnlySpan<byte>)` are used to search for the header terminator without copying the bytes into a string first.
- Pure helpers live in `HttpRequestReceiver`; the imperative loop lives in `Program.ReceiveRequest`. The split keeps the decision logic testable without sockets.

## Build

Add a `ReceiveRequest` loop that calls `Socket.Receive` repeatedly until the buffer contains either:
- the header terminator `\r\n\r\n` and (if present) `Content-Length` body bytes, or
- more than `MaxRequestBytes` (1 MiB).

Files affected:

- `src/MiniWebServer.Host/Program.cs` — replace one-shot receive with the loop and decode the bytes once they are complete.
- `src/MiniWebServer.Host/HttpRequestReceiver.cs` — new pure helper with `FindHeaderEnd` and `ParseContentLength`.
- `tests/MiniWebServer.Host.Tests/Program.cs` — add 7 unit tests for the helper.

Code shape inside `ReceiveRequest`:

```csharp
byte[] buffer = new byte[MaxRequestBytes];
int total = 0;
int headerEnd = -1;
int contentLength = 0;

while (total < MaxRequestBytes)
{
    int n = clientSocket.Receive(buffer, total, buffer.Length - total, SocketFlags.None);
    if (n <= 0) break;
    total += n;

    if (headerEnd < 0)
        headerEnd = HttpRequestReceiver.FindHeaderEnd(buffer.AsSpan(0, total));
    if (headerEnd >= 0 && contentLength == 0)
        contentLength = HttpRequestReceiver.ParseContentLength(buffer.AsSpan(0, headerEnd));

    if (headerEnd >= 0)
    {
        int needed = headerEnd + HttpRequestReceiver.HeaderDelimiter.Length + contentLength;
        if (total >= needed)
            return buffer.AsSpan(0, needed).ToArray();
    }
}
throw new InvalidOperationException(...);
```

Keep the slice small:

- No keep-alive (single request per socket, close after response).
- No chunked transfer encoding.
- No async / `ReceiveAsync`.
- Hard cap of 1 MiB; larger requests throw.

## Experiment

Run tests:

```powershell
dotnet run --project tests/MiniWebServer.Host.Tests/MiniWebServer.Host.Tests.csproj
```

Run server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Three smoke runs:

```powershell
# Test 1: ordinary curl still works.
curl -i http://localhost:8080/
# Expected: 200 OK with HTML body.

# Test 2: GET with headers split across two TCP writes.
$client = [System.Net.Sockets.TcpClient]::new('127.0.0.1', 8080)
$stream = $client.GetStream()
$part1 = [System.Text.Encoding]::ASCII.GetBytes("GET / HTTP/1.1`r`nHost: localhost`r`n")
$stream.Write($part1, 0, $part1.Length); $stream.Flush()
Start-Sleep -Seconds 1
$part2 = [System.Text.Encoding]::ASCII.GetBytes("`r`n")
$stream.Write($part2, 0, $part2.Length); $stream.Flush()
$buf = New-Object byte[] 4096
$read = $stream.Read($buf, 0, $buf.Length)
[System.Text.Encoding]::UTF8.GetString($buf, 0, $read)
# Expected: HTTP/1.1 200 OK

# Test 3: POST with body bytes split across two writes.
$client = [System.Net.Sockets.TcpClient]::new('127.0.0.1', 8080)
$stream = $client.GetStream()
$body = "hello world from fragmented POST"
$headers = "POST /submit HTTP/1.1`r`nHost: localhost`r`nContent-Length: $($body.Length)`r`n`r`n"
$headerBytes = [System.Text.Encoding]::ASCII.GetBytes($headers)
$stream.Write($headerBytes, 0, $headerBytes.Length); $stream.Flush()
Start-Sleep -Seconds 1
$bodyBytes = [System.Text.Encoding]::ASCII.GetBytes($body)
$stream.Write($bodyBytes, 0, $bodyBytes.Length); $stream.Flush()
$buf = New-Object byte[] 4096
$read = $stream.Read($buf, 0, $buf.Length)
[System.Text.Encoding]::UTF8.GetString($buf, 0, $read)
# Expected: HTTP/1.1 404 Not Found (no wwwroot/submit file)
```

## Observation

What does the server console show for each fragmented request?

Expected:

Test 2 — fragmented GET:
```text
[thread 4] Accepted client socket from 127.0.0.1:64796
Receive() returned 2 byte(s) on call #2; total 35 byte(s); request complete.
[thread 4] Method: GET
[thread 4] Path: /
```

Test 3 — fragmented POST:
```text
[thread 5] Accepted client socket from 127.0.0.1:64797
Receive() returned 32 byte(s) on call #2; total 94 byte(s); request complete.
[thread 5] Method: POST
[thread 5] Path: /submit
[thread 5] Headers: 2
```

`Receive() returned N byte(s) on call #2` proves the loop ran twice per request: the first `Receive()` got only the prefix the client had already sent, the second `Receive()` got the suffix that arrived after the 1-second pause. The total byte count equals the full request size. The parser then sees the complete request and produces the correct method, path, and header count.

If the loop had stopped after one `Receive()`, test 2 would have parsed `"GET / HTTP/1.1\r\nHost: localhost\r\n"` as the request line and returned 404 (parser rejects because `requestLineParts.Length != 3`). Test 3 would have parsed only the headers and dropped the body on the floor.

## Three-Question Test

1. What is the OS doing?
   - It wakes the server each time the socket has at least one byte ready. It does not assemble complete HTTP messages — that is the application's job.
2. Which .NET API exposes it?
   - `Socket.Receive(byte[], int, int, SocketFlags)` returns the number of bytes copied on this call. The receive loop in `Program.ReceiveRequest` decides whether to call it again. `HttpRequestReceiver.FindHeaderEnd` / `ParseContentLength` are pure byte-level checks that decide when to stop.
3. Where does it break at scale?
   - A slow client that opens the connection and sends one byte every minute will keep the handler thread blocked forever. The slice adds a hard 1 MiB cap and a multi-receive loop, but no idle timeout — that belongs to a later slice. Without a timeout, a malicious client can occupy a handler thread indefinitely. The deeper fix is bounded concurrency (Milestone 6).

## Learning Note

### What changed

Replaced the one-shot `ReceiveRequest` in `Program.cs` with a loop that keeps calling `Receive()` until the accumulated buffer contains either the full header terminator plus body (if `Content-Length` is present) or until the 1 MiB cap is exceeded. Added `HttpRequestReceiver` as a small pure helper exposing `FindHeaderEnd` and `ParseContentLength`, both tested without sockets. `HandleClient` now decodes the assembled bytes once after the loop returns. The old one-shot receive is gone; the new loop is the only path.

Files affected:

- `src/MiniWebServer.Host/Program.cs` — `ReceiveRequest` rewritten; `ReceiveBufferSize` removed; `MaxRequestBytes` added; `HandleClient` decodes bytes after the loop.
- `src/MiniWebServer.Host/HttpRequestReceiver.cs` — new file.
- `tests/MiniWebServer.Host.Tests/Program.cs` — 7 new tests.

### What I observed

Unit tests (`dotnet run --project tests/MiniWebServer.Host.Tests/MiniWebServer.Host.Tests.csproj`):

```text
PASS parses request line
PASS parses headers
PASS invalid request becomes unknown request
PASS serves index html for root path
PASS missing file returns 404
PASS path traversal returns 404
PASS web root resolves from app base directory
PASS parses /slow path
PASS receiver finds header terminator at expected offset
PASS receiver returns -1 when header terminator is missing
PASS receiver parses integer Content-Length value
PASS receiver parses Content-Length with leading whitespace
PASS receiver returns 0 when Content-Length is absent
PASS receiver returns 0 when Content-Length is malformed
All tests passed.
```

Smoke test against the running server:

Test 2 — fragmented GET:
```text
Response status line: HTTP/1.1 200 OK
```
Server log:
```text
[thread 4] Accepted client socket from 127.0.0.1:64796
Receive() returned 2 byte(s) on call #2; total 35 byte(s); request complete.
[thread 4] Method: GET
[thread 4] Path: /
```

Test 3 — fragmented POST with body:
```text
Response status line: HTTP/1.1 404 Not Found
```
Server log:
```text
[thread 5] Accepted client socket from 127.0.0.1:64797
Receive() returned 32 byte(s) on call #2; total 94 byte(s); request complete.
[thread 5] Method: POST
[thread 5] Path: /submit
[thread 5] Headers: 2
```

In both cases `Receive()` ran twice — once for the first TCP write (headers), once for the second TCP write (after the 1-second pause). The loop did not exit early after the first call. The `total` byte count equals the full request size (35 = 16-byte header + 15-byte header + 4-byte terminator; 94 = 62-byte headers + 32-byte body). The parser then sees the complete request and produces the correct method, path, and header count.

### OSTEP concept

The server sits on a TCP byte stream. The OS wakes it when at least one byte has arrived; it does not promise a complete HTTP request. OSEP Ch. 36 §36.4 explains why: the OS interrupts on hardware-level "data available" events, not on application-level "message complete" events. Without a receive loop, the application is at the mercy of the OS's idea of "ready," which is one or more bytes, not one logical request.

The two-fragment GET experiment is the textbook demonstration. With one `Receive()` call, the server sees only the first 33 bytes (`"GET / HTTP/1.1\r\nHost: localhost\r\n"`) — no terminator, no complete request line. The old `HttpRequestParser` would return `HttpRequest.Unknown` (because `requestLineParts.Length == 1` after splitting "GET" only), and the server would respond with `200 OK` and an empty body (the `else` branch returning `StaticFileResponder.CreateResponse` on an Unknown request path of `/`). That is the exact failure mode OSEP describes: the OS gave us bytes; we assumed they were a complete request.

With the loop, the second `Receive()` call gets the remaining 2 bytes (`"\r\n"`), the helper finds the terminator, and the parser receives the full request. The cost is one extra blocking call per slow request — the handler thread sits in `Receive()` until the client sends more bytes, then returns. That is the same blocking-I/O pattern slice 4.1 used to demonstrate single-thread blocking; it works here because we are in phase 2 with one thread per client.

### .NET mechanism

- `Socket.Receive(byte[], int offset, int count, SocketFlags)` writes up to `count` bytes starting at `offset`. We allocate the full `MaxRequestBytes` (1 MiB) once and write into it cumulatively, so the receive loop never copies data between buffers.
- `ReadOnlySpan<byte>.IndexOf(ReadOnlySpan<byte>)` (since .NET Core 2.1) finds the 4-byte `\r\n\r\n` delimiter in O(N) without allocating a string or copying bytes.
- `Encoding.UTF8.GetString(byte[])` decodes the assembled request exactly once, after the loop has confirmed the buffer is complete. Before the slice, the same call happened inside `ReceiveRequest` after one `Receive()`, so a partial request would have been decoded as garbage.
- The pure helper / imperative-loop split mirrors what `HttpRequestParser` / `Program` already did for HTTP parsing. The pattern is now consistent across the host.

### Next question

What about a slow client that holds the socket open and sends one byte per minute? The receive loop will block forever on the next `Receive()` call, occupying one handler thread indefinitely. The slice adds a size cap but not a time cap.

Next slice (future): add a receive deadline / socket read timeout so the server can recover. Or move to Phase 3 / Milestone 6 to bound the number of concurrent handlers.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted