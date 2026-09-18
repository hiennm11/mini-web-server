# Slice 1.3: Static File Server

## Question

How does an HTTP path become a safe file-system read?

## OSTEP Context

- Chapter(s): 36 (I/O Devices), 39 (Files and Directories)
- Concept: files are persistent byte arrays, and directories map names to files.
- Key point: a URL path is not automatically a safe file path. The server must translate and constrain it.

OSTEP presents files and directories as OS abstractions for persistent storage. A file is a named sequence of bytes. A directory maps human-readable names to lower-level file identities and composes into a tree.

In this server, `/` maps to `wwwroot/index.html`. `/missing.txt` maps to a name that does not exist. `/../CONTEXT.md` tries to escape the web root and must be rejected.

## C#/.NET Mechanism

- Type or API: `Path.GetFullPath(...)`, `File.Exists(...)`, `File.ReadAllBytes(...)`.
- Supporting types: `StaticFileResponder`, `HttpResponse`, `WebRootLocator`.
- Why this, not another abstraction: direct file APIs keep the file-system boundary visible. No static-file middleware hides path mapping or file reads.

## Build

Add static file serving:

1. Create a `wwwroot` directory.
2. Add `index.html`.
3. Map `/` to `index.html`.
4. Normalize requested paths with `Path.GetFullPath(...)`.
5. Reject paths outside the web root.
6. Return `200 OK` with file bytes for existing safe files.
7. Return `404 Not Found` for missing or unsafe files.
8. Add minimal content type support.
9. Format the HTTP response with status line, headers, blank line, and body bytes.

Files affected:

- `src/MiniWebServer.Host/StaticFileResponder.cs`
- `src/MiniWebServer.Host/HttpResponse.cs`
- `src/MiniWebServer.Host/WebRootLocator.cs`
- `src/MiniWebServer.Host/wwwroot/index.html`
- `src/MiniWebServer.Host/Program.cs`
- `tests/MiniWebServer.Host.Tests/`

Keep the slice small:

- Use `File.ReadAllBytes(...)` for now.
- Do not stream large files yet.
- Do not add directory listings.
- Do not add caching.
- Do not add MIME completeness.

## Experiment

Run tests:

```powershell
dotnet run --project tests/MiniWebServer.Host.Tests/MiniWebServer.Host.Tests.csproj
```

Run the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Try a valid file:

```powershell
curl -i http://localhost:8080/
```

Expected:

```text
HTTP/1.1 200 OK
Content-Type: text/html; charset=UTF-8
```

Try a missing file:

```powershell
curl -i http://localhost:8080/missing.txt
```

Expected:

```text
HTTP/1.1 404 Not Found
```

Try path traversal:

```powershell
curl -i --path-as-is http://localhost:8080/../CONTEXT.md
```

Expected:

```text
HTTP/1.1 404 Not Found
```

## Observation

Why does `/../CONTEXT.md` return `404 Not Found` even though `CONTEXT.md` exists in the repository?

Expected answer:

The server exposes only the chosen web root. `..` tries to move outside that tree. The server normalizes the full path and rejects it before reading the file.

## Three-Question Test

1. What is the OS doing?
   - It resolves file names, checks metadata, and returns file bytes when the process asks to read a file.
2. Which .NET API exposes it?
   - `Path.GetFullPath(...)` normalizes names; `File.Exists(...)` checks presence; `File.ReadAllBytes(...)` reads bytes.
3. Where does it break at scale?
   - `File.ReadAllBytes(...)` loads the whole file into memory. Large files can waste memory and block the single server thread.

## Learning Note

### What changed

The server now uses the parsed request path to serve files from `wwwroot` and return `404 Not Found` for missing or unsafe paths.

### What I observed

`/` returns `index.html`. Missing files return `404`. Path traversal attempts cannot escape the web root.

### OSTEP concept

This slice shows files and directories as OS abstractions. The server maps protocol names to file-system names, then asks the OS to read persistent bytes.

### .NET mechanism

`StaticFileResponder` translates paths, `WebRootLocator` finds the runtime web root, `File.ReadAllBytes` reads file content, and `HttpResponse` wraps bytes in HTTP format.

### Next question

The server can read files, but it still assumes one `Receive()` call gives a complete request. How do we handle partial or slow HTTP requests?

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted
