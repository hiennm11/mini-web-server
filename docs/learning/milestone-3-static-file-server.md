# Milestone 3: Static File Server

## What We Built

The server now uses the parsed request path to read files from `wwwroot`.

The source files live in `src/MiniWebServer.Host/wwwroot`. During build, .NET copies them beside the host executable. The server uses that runtime location so `dotnet run --project ...` works from any current directory.

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

## OSTEP Connection

OSTEP describes two core storage abstractions: files and directories. A file is a linear array of bytes. A directory maps human-readable names to lower-level file identities, and directories compose into a tree. When this server maps `/` to `index.html`, it is translating an HTTP name into a file-system name inside that tree.

The OS does not care whether a file contains HTML, an image, or source code. It stores and returns bytes. The web server adds the HTTP meaning by choosing a content type and wrapping the bytes in an HTTP response.

Opening and reading a file crosses into the OS just like socket I/O does. In OSTEP's Unix examples, `open()` returns a file descriptor: an opaque handle that lets the process read or write a file. In .NET, the API is different, but the idea is the same: application code asks the OS for access to a named file, and the OS tracks the opened file and the current read position.

File metadata also matters. OSTEP shows metadata through `stat()`: size, identity, permissions, timestamps, and related facts. In this project, `File.Exists(...)` and `File.ReadAllBytes(...)` hide most of that detail, but they still depend on the file-system metadata and file contents managed by the OS.

Disk and storage I/O are device operations. OSTEP's I/O chapters explain why the OS uses device drivers, interrupts, and DMA to avoid making the CPU manually wait and copy every byte. If the server blocks while reading a file, the OS can put that execution path to sleep until the I/O completes.

That blocking behavior motivates the next milestone. With one thread, a slow file read delays every other client. With multiple threads, one thread can wait for disk or network I/O while another thread continues serving a different client.

## C#/.NET Concept

`StaticFileResponder` maps an `HttpRequest` to an `HttpResponse`.

It uses:

- `Path.GetFullPath(...)` to normalize paths
- `File.Exists(...)` to check file presence
- `File.ReadAllBytes(...)` to read file bytes
- content types such as `text/html; charset=UTF-8`

`HttpResponse.ToBytes()` manually formats the HTTP status line, headers, blank line, and body bytes.

`WebRootLocator` avoids a common bug: using `"wwwroot"` relative to the terminal's current directory. Current directory can change; `AppContext.BaseDirectory` points to the running app's output folder.

## Common Misconceptions

A URL path is not automatically safe to pass to file APIs. The request path comes from the client, so it must be treated as untrusted input.

Rejecting `..` is about preserving the web root boundary. File systems intentionally support parent-directory traversal, but a static file server should expose only the chosen web root.

`File.ReadAllBytes(...)` is fine for this milestone because the files are tiny. It is not a scalable strategy for large files, because it loads the whole file into memory before sending the response.

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

To connect this to OSTEP, read these three cases as file-system name resolution:

- `/` becomes a known file name under the web root.
- `/missing.txt` names something that is not present in that directory tree.
- `/../CONTEXT.md` tries to name something outside the allowed tree and is rejected before the OS is asked to read it.

## Next Question

The server now reads files, but still handles one client at a time. Milestone 4 will introduce one thread per client connection.
