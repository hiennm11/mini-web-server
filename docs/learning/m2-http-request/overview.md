# Milestone 2: HTTP Request Understanding

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How do you parse a raw HTTP request (status line + headers + body) from a TCP byte stream?

## Scope

Take the M1 server and parse the request into structured form: method, path, version, headers dictionary, body. Validate the framing (header terminator + Content-Length).

## Slice

- **[s1-http-request.md](./s1-http-request.md)** — `HttpRequest` record + `HttpRequestParser.Parse(string)`.

## OSTEP coverage

- **Ch. 39 Interlude: Files and Directories** (§39.3 creating files via `open()`, §39.4 reading and writing — our `POST` body semantics follow this pattern).
- **Ch. 4 The Abstraction: The Process** (§4.1 — the server is a process; §4.2 process API for create/destroy/wait/status, indirectly the API we're providing over HTTP).

OSEP §39.4 documents `read()` / `write()` system calls and the file-descriptor model. We don't expose raw fds over HTTP (we expose parsed HTTP requests), but the same byte-stream semantics apply: the body length is given by `Content-Length`, just as a file's length is given by its inode's `st_size`.

The "process API" surface — what the OS provides for creating/destroying/waiting for processes — is roughly what we're building at the HTTP layer (create file = POST /fs/create; wait for completion = synchronous response).

## OSEP §-specific deviations

- OSEP §39 covers POSIX file descriptors. Our HTTP API is a higher-level abstraction; the OS underneath still uses fds.
- The "process creation" analogy is loose — OSEP `fork()` / `exec()` is about OS processes, not HTTP requests.

## .NET mechanism

- `string.Split('\n')` to extract lines.
- `string.IndexOf("\r\n\r\n")` to find header terminator.
- `string.Substring` for slices.
- `int.TryParse` for `Content-Length`.

No regex, no async — pure string manipulation.

## Files

- `src/MiniWebServer.Host/HttpRequest.cs` — `HttpRequest` record (Method, Path, Version, Headers dict, Body).
- `src/MiniWebServer.Host/HttpRequestParser.cs` — `Parse(string rawRequest)` static method.
- `src/MiniWebServer.Host/Program.cs` — uses `HttpRequestParser.Parse(request)` in the accept loop.

## Where this leads

- M3: use the parsed path to serve a real file from `wwwroot/`.
- M4: handle multiple clients concurrently (now that each request is parsed, we can route it independently).
