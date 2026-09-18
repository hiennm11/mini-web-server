# Milestone 3: Static File Server

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How do you serve files from disk over HTTP, given a parsed request path?

## Scope

Map a request path like `/index.html` to a file under a configurable `wwwroot` directory. Read the file, build a 200 OK response with `Content-Type` + `Content-Length`. Handle 404 for missing files.

## Slice

- **[s1-static-file-server.md](./s1-static-file-server.md)** — `StaticFileResponder.CreateResponse(parsedRequest, webRoot)` returning either a 200 OK with the file body or a 404 Not Found.

## OSTEP coverage

- **Ch. 39 Interlude: Files and Directories** — files = linear array of bytes (§39.1); `open` / `read` / `write` / `close` (§39.3-§39.4); current file offset + `lseek` (§39.5).
- **Ch. 40 File System Implementation** (§40.2 vsfs layout: superblock + inode bitmap + data bitmap + inode table + data region).

This milestone uses the **OS file API** — we never implement a file system, we just consume one. The OSEP chapters give us vocabulary (file descriptor, current offset, `read` returns `n` bytes) for what's happening underneath.

OSEP §40.2 is what the **underlying** file system looks like: 4 KB blocks, superblock at offset 0, two bitmaps, an inode table, then data blocks. We don't see this directly — `File.ReadAllBytes` hides it.

## OSEP §-specific deviations

- OSEP §39.1 emphasizes files are a "linear array of bytes". We expose this exactly — the HTTP body IS the bytes.
- OSEP §39.5 introduces `lseek` for non-sequential access. Our static file responder always serves the whole file (no range requests yet).
- OSEP §39.7 `fsync()` for durability. Our M3 slice doesn't fsync after writing — writes are buffered by the OS.

## .NET mechanism

- `System.IO.File.ReadAllBytes(string path)` — synchronous read into a managed buffer.
- `System.IO.Path.Combine` + `Path.GetFullPath` for path resolution.
- `Path.DirectorySeparatorChar` for cross-platform paths.

## Files

- `src/MiniWebServer.Host/StaticFileResponder.cs` — `CreateResponse(parsedRequest, webRoot)`.
- `src/MiniWebServer.Host/Program.cs` — `WebRootLocator.GetWebRoot()` finds the `wwwroot/` directory.
- `wwwroot/` — sample files (index.html).

## Where this leads

- M4: serve files concurrently across multiple clients.
- M11: replace `File.ReadAllBytes` with raw `FileStream` (syscall-level).
- M12: implement our own toy file system from scratch.
