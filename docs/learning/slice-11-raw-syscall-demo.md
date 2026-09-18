# Milestone 11: Raw `open`/`read`/`write`/`close` Syscall Demo

## Question

What does the OS actually do when a file is read in our server? How do we make the kernel's `open` / `read` / `close` syscalls observable?

## OSTEP Context

- Chapter(s): 39 (Files and Directories), specifically §39.1 (the file abstraction), §39.3 (creating, reading, writing, closing files), §39.4 (file descriptors).
- Concept: every file operation in user-space goes through the kernel via a syscall. The kernel hands back an integer *file descriptor* (fd) on `open`; subsequent `read` / `write` / `seek` take the fd; `close` releases it. The fd is the user's handle into the kernel's per-process open-file table.
- Key point from OSEP §39.4: file descriptors are integers starting at 0 (stdin), 1 (stdout), 2 (stderr). Each `open` allocates the lowest unused fd. The kernel maintains an offset, access mode, and pointer to the underlying vnode/inode for each fd.

## C#/.NET Mechanism

- `FileStream` wraps a kernel fd. Its constructor `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)` issues an `open` syscall; `.Read(buffer, offset, count)` issues a `read` syscall; `Dispose` issues a `close`.
- The default buffer size for `FileStream` is **4096 bytes** on .NET — the same as a typical disk sector size. Each `Read` call asks the kernel for up to 4096 bytes and returns the actual count.
- `.NET` runtime hides the syscall details behind the type. To observe them, we instrument each call: log when `open` happens (constructor entry), log each `Read` with bytes returned, log when `close` happens (Dispose).

## Build

Replace `File.ReadAllBytes` in `StaticFileResponder` with a raw read helper that logs each syscall. Add a dedicated `/read-syscall` route that demonstrates the same pattern.

```csharp
public static class RawFileAccess
{
    public static byte[] ReadAllBytesRaw(string path)
    {
        Console.WriteLine($"[syscall] open(path={path})");
        using var fs = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.None,  // no async; we want blocking read so each call is one syscall
        });
        var buffer = new byte[4096];
        var ms = new MemoryStream();
        int totalReads = 0;
        long totalBytes = 0;
        int n;
        while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            Console.WriteLine($"[syscall] read(fd={fs.SafeFileHandle}, requested={buffer.Length}, returned={n}, offset={fs.Position - n})");
            ms.Write(buffer, 0, n);
            totalReads++;
            totalBytes += n;
        }
        Console.WriteLine($"[syscall] close(fd={fs.SafeFileHandle}) [total_reads={totalReads}, total_bytes={totalBytes}]");
        return ms.ToArray();
    }
}
```

Replace `File.ReadAllBytes(fullPath)` in `StaticFileResponder` with `RawFileAccess.ReadAllBytesRaw(fullPath)`. The `using var fs` guarantees `close` runs.

Add `/read-syscall?path=` route in `Program.cs` that:
- Strips the query (path = `?path=` value)
- Validates the resolved path is inside `webRoot`
- Calls `RawFileAccess.ReadAllBytesRaw(...)`
- Returns text/plain with a header showing total reads, total bytes, and the body

```csharp
else if (parsedRequest.Path == "/read-syscall")
{
    // Parse ?path= query
    var qs = parsedRequest.Path.IndexOf('?') >= 0
        ? parsedRequest.Path.Substring(parsedRequest.Path.IndexOf('?') + 1)
        : "";
    var pathParam = "";
    foreach (var kv in qs.Split('&'))
    {
        var eq = kv.IndexOf('=');
        if (eq > 0 && kv.Substring(0, eq) == "path") { pathParam = Uri.UnescapeDataString(kv.Substring(eq + 1)); break; }
    }
    if (string.IsNullOrEmpty(pathParam)) pathParam = "index.html";

    string root = Path.GetFullPath(webRoot);
    string fullPath = Path.GetFullPath(Path.Combine(root, pathParam.Replace('/', Path.DirectorySeparatorChar)));
    if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
    {
        response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("Not Found\n"));
    }
    else
    {
        byte[] body = RawFileAccess.ReadAllBytesRaw(fullPath);
        string header = $"file: {pathParam}\nbytes: {body.Length}\n";
        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
        var combined = new byte[headerBytes.Length + body.Length];
        Buffer.BlockCopy(headerBytes, 0, combined, 0, headerBytes.Length);
        Buffer.BlockCopy(body, 0, combined, headerBytes.Length, body.Length);
        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", combined);
    }
}
```

Files affected:

- `src/MiniWebServer.Host/RawFileAccess.cs` (new) — the syscall-logging helper.
- `src/MiniWebServer.Host/StaticFileResponder.cs` — replace `File.ReadAllBytes` with `RawFileAccess.ReadAllBytesRaw`.
- `src/MiniWebServer.Host/Program.cs` — add `/read-syscall` route.

Keep the slice small:

- Read-only — no `write` syscall demo yet. `write` is a separate slice (file mutation requires safe path handling and is its own persistence lesson).
- No async I/O — we use blocking `Read` so each call is a clear single-syscall observation. The async version would mix kernel-completion callbacks with the syscall narrative.
- No direct `P/Invoke` to `open`/`read`/`close` libc — too OS-specific and the .NET wrapper is one syscall call away.

## Experiment

Terminal 1 — start the server (default mode is fine):

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Terminal 2 — request a file via the new route:

```powershell
curl http://127.0.0.1:8080/read-syscall?path=index.html
```

Watch the server console for the syscall log:

```
[syscall] open(path=D:\repos\mini-web-server\wwwroot\index.html)
[syscall] read(fd=..., requested=4096, returned=N, offset=...)
[syscall] read(fd=..., requested=4096, returned=...)
...
[syscall] close(fd=...)
```

For a 300-byte `index.html`, the smoke should show **1 read** (single syscall because file fits in one buffer). For a 10 KB file, **3 reads** (10 KB / 4 KB). For an empty file, **0 reads** + the close.

Then request a regular static file (`/index.html`) and confirm the syscall log fires too — that proves `StaticFileResponder` now uses the raw helper.

Then request a missing file (`/read-syscall?path=nonexistent.html`) and confirm **no syscall fires** (path validation rejects before `open`).

## Observation

Answer after running the experiment:

What does the kernel do when we read a file?

Expected answer:

The kernel allocates a file descriptor in the process's open-file table, increments the inode's reference count, sets the file offset to 0, and returns the fd to user-space. Each `read` syscall asks the kernel to copy bytes from the file (starting at the current offset) into a user-space buffer; the kernel updates the offset by the bytes returned. `read` returns 0 at EOF. `close` decrements the inode reference count, frees the fd, and flushes any buffered state.

More precise answer:

In our helper, each `fs.Read(buffer, 0, 4096)` call invokes exactly one `read` syscall on the underlying fd. The syscall returns the number of bytes actually copied (which may be less than 4096 if the file has fewer bytes left, or if the kernel short-reads due to a partial buffer). The `using var fs` ensures `close` runs deterministically when the method returns, even on exception. The console log makes each syscall observable: one `[syscall] open` per request, one `[syscall] read` per 4 KB chunk, and one `[syscall] close` per file.

## Three-Question Test

1. What is the OS doing?
   - The kernel allocates a per-process fd table entry pointing to the inode, sets the file offset to 0, and returns the fd. `read` copies bytes from the kernel's page cache (or disk, on cache miss) into the user buffer; the kernel updates the offset. `close` decrements the inode refcount and frees the fd entry.
2. Which .NET API exposes it?
   - `System.IO.FileStream` (synchronous) wraps the fd. `fs.SafeFileHandle` exposes the underlying HANDLE on Windows (or `int` fd on Unix via P/Invoke). `.Read(buffer, offset, count)` issues one `read` syscall. `Dispose` issues `close`.
3. Where does it break at scale?
   - Many small reads waste syscalls (each one crosses the user/kernel boundary). The `BufferedStream` wrapper buffers reads so the user sees one big read but the kernel sees many small ones. The default `FileStream` buffer is 4 KB; for large files, `FileOptions.SequentialScan` enables readahead.

## Learning Note

### What changed

- `src/MiniWebServer.Host/RawFileAccess.cs` (new) — `ReadAllBytesRaw(string path)` opens a `FileStream` (logs `open`), loops `fs.Read(buffer, 0, 4096)` (logs each `read` with bytes returned + offset), disposes via `using` (logs `close`). Uses blocking `FileOptions.None` so each `Read` is a single observable syscall, not an IOCP callback.
- `src/MiniWebServer.Host/StaticFileResponder.cs` — replaces `File.ReadAllBytes(fullPath)` with `RawFileAccess.ReadAllBytesRaw(fullPath)`. The static-file route now logs every syscall too.
- `src/MiniWebServer.Host/Program.cs` — new `/read-syscall?path=...` route that parses the query, validates the path stays inside `webRoot`, calls `RawFileAccess.ReadAllBytesRaw`, and returns the body prefixed with a small header (`file:`, `bytes:`, `---`).

Files affected:

- `src/MiniWebServer.Host/RawFileAccess.cs` (new)
- `src/MiniWebServer.Host/StaticFileResponder.cs` (replaced `File.ReadAllBytes` with the raw helper)
- `src/MiniWebServer.Host/Program.cs` (added `/read-syscall` route)

### What I observed

Smoke against the running server (default worker-pool mode):

**Request 1: `/read-syscall?path=index.html`** (225 bytes)

Server console:

```
[syscall] open(path=D:\repos\mini-web-server\src\MiniWebServer.Host\bin\Debug\net10.0\wwwroot\index.html)
[syscall] open returned handle=Microsoft.Win32.SafeHandles.SafeFileHandle
[syscall] read(handle=..., requested=4096, returned=225, offset=0)
[syscall] close(handle=...) [total_reads=1, total_bytes=225]
```

Body returned:

```
file: index.html
bytes: 225
---
<!doctype html>
<html lang="en">
...
</html>
```

1 `open` + 1 `read` + 1 `close`. The single read returned 225 bytes (less than the 4096 buffer); the next loop iteration hits EOF and exits.

**Request 2: `/read-syscall?path=big.txt`** (13658 bytes)

Server console:

```
[syscall] open(path=...wwwroot\big.txt)
[syscall] open returned handle=...
[syscall] read(handle=..., requested=4096, returned=4096, offset=0)
[syscall] read(handle=..., requested=4096, returned=4096, offset=4096)
[syscall] read(handle=..., requested=4096, returned=4096, offset=8192)
[syscall] read(handle=..., requested=4096, returned=1370, offset=12288)
[syscall] close(handle=...) [total_reads=4, total_bytes=13658]
```

4 reads: the first three return the full 4096-byte buffer; the fourth returns the tail (13658 − 12288 = 1370). 4096 × 3 + 1370 = 13658. The kernel's offset advances monotonically: 0 → 4096 → 8192 → 12288.

**Request 3: `/read-syscall?path=missing.html`** (404, no syscall fires)

Server console shows the request parses but no `[syscall]` line. The path-validation guard (`!File.Exists(fullPath)`) rejects before `open` runs. Confirms the no-syscall invariant: a 404 path costs zero disk I/O.

**Request 4: `/index.html`** (StaticFileResponder route)

Server console shows the same `[syscall] open + read + close` for the static-file path. Confirms the substitution took effect: `StaticFileResponder` now uses `RawFileAccess.ReadAllBytesRaw` instead of `File.ReadAllBytes`.

### OSEP concept

OSEP §39.3 describes `open`, `read`, `write`, `close`, `seek` as the file-system API surface. §39.4 introduces the file descriptor as an integer handle that user code holds and the kernel tracks. The slice demonstrates exactly this: `FileStream` wraps a kernel fd; each `Read` is one `read` syscall; `Dispose` is one `close` syscall.

A subtle point: the kernel's `read` may short-read (return fewer bytes than the user asked for). The OSEP §39.3 lesson is "always loop on read until 0 (EOF) or until you have what you want." Our helper does this: the `while ((n = fs.Read(...)) > 0)` loop handles short-reads transparently.

The 4 KB buffer matches the typical sector size on disk (the kernel's page cache page is 4 KB on Linux and 4 KB-aligned on Windows). For a single 4 KB page cache hit, one `read` syscall can return up to 4 KB; for a multi-page file, the kernel returns up to 4 KB per syscall unless the user asks for more (or uses a different mechanism like `sendfile` / `copy_file_range`).

The smoke's "short-read" of 1370 bytes on the 4th iteration is the kernel's normal behavior at EOF — it returns whatever is left, not zero-padded. The loop detects this as `n > 0` on the last call, writes the bytes, then the next call returns 0 and exits.

### .NET mechanism

- `FileStream` constructor issues `open`. The `FileStreamOptions` pattern (added in .NET 6) replaces the older long-arg constructor. The default buffer size is **4096 bytes**; the .NET runtime manages a buffered stream around the kernel fd. Setting `Options = FileOptions.None` disables async, sequential scan, and write-through, so the syscall is purely synchronous.
- `fs.Read(byte[], int, int)` issues one `read` syscall, returns bytes copied, advances `fs.Position` by `n`. On EOF returns 0. On short-read returns whatever the kernel gave.
- `fs.SafeFileHandle` is a `SafeHandle` wrapping the kernel handle (Windows `HANDLE` or Unix `int` fd). It is logged but the value is opaque — to inspect the fd integer on Unix you'd need `handle.DangerousGetHandle().ToInt32()` (unsafe).
- `using var fs` → `Dispose` → `close` syscall. `Dispose` also flushes any buffered writes (we're read-only here so no flush).

### Next question

The slice reads but does not write. A `write` syscall demo would need a safe writable location (e.g., a `tmp/` directory under `webRoot`), path validation, and an atomic-write pattern (write to a temp file + rename to the target). The OSEP Ch. 39 `write` syscall is the natural next lesson after `read`.

That's where **M12 (mini file system)** comes in: a real in-memory file system with inode, bitmap, directory entries, and journal. The M11 slice gives us the syscall vocabulary; M12 builds a tiny FS on top.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted