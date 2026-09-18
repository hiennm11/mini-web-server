# Milestone 3: Static File Server

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How do you serve files from disk over HTTP, given a parsed request path?

## Scope

Map a request path like `/index.html` to a file under a configurable `wwwroot` directory. Read the file, build a 200 OK response with `Content-Type` + `Content-Length`. Handle 404 for missing files. (No directory listing, no MIME-type detection beyond the basic extension → type mapping.)

## Slice

- **[s1-static-file-server.md](./s1-static-file-server.md)** — `StaticFileResponder.CreateResponse(parsedRequest, webRoot)` returning either a `200 OK` with the file body or a `404 Not Found`.

## OSEP concept

This slice is the entry point for OSEP Part III (Persistence). The server starts treating files as **persistent** — disk contents must survive process restarts, so the file path translation must respect filesystem semantics (case-sensitive on Linux, absolute paths rejected for security).

`File.ReadAllBytes(webRoot + path)` is the simplest possible read — one syscall, into a managed buffer. No buffering, no caching, no shared mappings. The next milestone (M11) will revisit this with raw syscalls.

## .NET mechanism

- `System.IO.File.ReadAllBytes(string path)` — synchronous read.
- `System.IO.Path.Combine` + `Path.GetFullPath` for path resolution.
- `Path.DirectorySeparatorChar` for cross-platform path building.

## Files

- `src/MiniWebServer.Host/StaticFileResponder.cs` — `CreateResponse(parsedRequest, webRoot)`.
- `src/MiniWebServer.Host/Program.cs` — `WebRootLocator.GetWebRoot()` finds the `wwwroot/` directory.
- `wwwroot/` — sample files (index.html).
