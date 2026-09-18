# Milestone 11: Raw Syscall Demo

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

What does the syscall boundary look like in practice? Can we observe the kernel handling `open` / `read` / `close` directly?

## Scope

Replace the M3 `File.ReadAllBytes()` (which hides the syscall in the BCL) with explicit `FileStream` reads using `FileOptions.None` (the simplest .NET path) and observe the kernel-side cost.

## Slice

- **[s1-raw-syscall-demo.md](./s1-raw-syscall-demo.md)** — `RawFileAccess.Open(path)` returns a `FileStream`. `/fs-raw-read?path=` route opens + reads + closes the file explicitly, exposing the open/read/close lifecycle.

## OSEP concept

- **Ch. 39 Files and Directories** — the `open` / `read` / `close` / `write` system calls.
- **Ch. 36 I/O Devices** — the kernel mediates all disk access; user-mode `open` is a syscall that returns a file descriptor (or handle).

The lesson is that even "raw" `FileStream` is still wrapped in the BCL; the actual syscall goes through the .NET runtime's IO manager. To go truly raw you'd use `pread` / `mmap` via P/Invoke or a C library — out of scope here.

## .NET mechanism

- `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.None)`.
- `FileStream.Read(byte[] buffer, int offset, int count)` returns the number of bytes read.
- `FileStream.Close()` calls the `close` syscall.

## Files

- `src/MiniWebServer.Host/RawFileAccess.cs` — `Open(path)` + `ReadAllBytes(handle)` helper.
- `src/MiniWebServer.Host/Program.cs` — `/fs-raw-read?path=` route.

## What this slice does NOT do

- Doesn't go below .NET's FileStream to `pread`/`mmap` (would need P/Invoke or a third-party binding).
- Doesn't measure syscall latency (could be added with `ETW` events).
