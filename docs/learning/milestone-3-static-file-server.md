# Milestone 3: Static File Server

## What We Built

The server now uses the parsed request path to read files from `wwwroot`.

Examples:

```text
GET / HTTP/1.1          -> wwwroot/index.html -> 200 OK
GET /missing.txt HTTP/1.1 -> no file          -> 404 Not Found
GET /../CONTEXT.md HTTP/1.1 -> unsafe path    -> 404 Not Found
```

## Generic OS Concept

A web path is not automatically a file path. The server must translate protocol text into a safe file-system name.

The file system gives names to persistent data. The server asks the OS to read bytes from disk, then sends those bytes back through the socket.

Path safety matters because `..` can try to escape the web root. The server resolves full paths and rejects anything outside `wwwroot`.

## C#/.NET Concept

`StaticFileResponder` maps an `HttpRequest` to an `HttpResponse`.

It uses:

- `Path.GetFullPath(...)` to normalize paths
- `File.Exists(...)` to check file presence
- `File.ReadAllBytes(...)` to read file bytes
- content types such as `text/html; charset=UTF-8`

`HttpResponse.ToBytes()` manually formats the HTTP status line, headers, blank line, and body bytes.

## Experiment

Run tests:

```powershell
dotnet run --project tests/MiniWebServer.Host.Tests/MiniWebServer.Host.Tests.csproj
```

Run server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Try:

```powershell
curl -i http://localhost:8080/
curl -i http://localhost:8080/missing.txt
curl -i --path-as-is http://localhost:8080/../CONTEXT.md
```

Expected:

- `/` returns `200 OK` and HTML from `wwwroot/index.html`
- `/missing.txt` returns `404 Not Found`
- `/../CONTEXT.md` returns `404 Not Found`

## Next Question

The server now reads files, but still handles one client at a time. Milestone 4 will introduce one thread per client connection.

