# Milestone 2: HTTP Request Understanding

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How do you parse a raw HTTP request (status line + headers + body) from a TCP byte stream?

## Scope

Take the M1 server and parse the request into structured form: method, path, version, headers dictionary, body. Validate the framing (header terminator + Content-Length).

## Slice

- **[s1-http-request.md](./s1-http-request.md)** — define `HttpRequest` record + `HttpRequestParser.Parse(string)`. Parse method/path/version from request line. Parse headers into a dictionary. Handle malformed inputs (missing terminator, bad Content-Length).

## OSTEP concept

HTTP is text — a simple line-based protocol. This slice doesn't introduce new OS concepts, but it's the foundation for understanding **how user-space code interprets the bytes the kernel delivered**.

The chunked-receive problem (request might arrive in multiple `Receive()` calls) was solved in M1.2; this slice is about **interpreting** those bytes once they're reassembled.

## .NET mechanism

- `string.Split('\n')` to extract lines.
- `string.IndexOf("\r\n\r\n")` to find header terminator.
- `string.Substring` for slices.
- `int.TryParse` for Content-Length.

No regex; no async — pure string manipulation on the assembled request.

## Files

- `src/MiniWebServer.Host/HttpRequest.cs` — `HttpRequest` record (Method, Path, Version, Headers dict, Body).
- `src/MiniWebServer.Host/HttpRequestParser.cs` — `Parse(string rawRequest)` static method.
- `src/MiniWebServer.Host/Program.cs` — uses `HttpRequestParser.Parse(request)` in the accept loop.
