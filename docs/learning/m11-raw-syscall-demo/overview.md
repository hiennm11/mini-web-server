# Milestone 11: Raw Syscall Demo

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

What does the syscall boundary look like in practice? Can we observe the kernel handling `open` / `read` / `close` directly?

## Scope

Replace the M3 `File.ReadAllBytes()` (which hides the syscall in the BCL) with explicit `FileStream` reads using `FileOptions.None` (the simplest .NET path) and observe the kernel-side cost.

## Slice

- **[s1-raw-syscall-demo.md](./s1-raw-syscall-demo.md)** — `RawFileAccess.Open(path)` returns a `FileStream`. `/fs-raw-read?path=` route opens + reads + closes the file explicitly, exposing the open/read/close lifecycle.

## OSTEP coverage

- **Ch. 36 I/O Devices** (§36.3 canonical protocol — status/command/data registers + polling, §36.4 interrupts, §36.5 DMA, §36.6 explicit I/O instructions vs memory-mapped I/O, §36.7 device drivers).
- **Ch. 39 Interlude: Files and Directories** (§39.3 `open` with `O_CREAT`/`O_WRONLY`/`O_TRUNC` flags, §39.4 `read` returns bytes-read count + `EOF`, §39.7 `fsync()` for durability).

The "open / read / close" cycle is just the OS-level mechanism under the covers of M3's `File.ReadAllBytes`. M11 makes the cycle visible.

OSEP §36.5 is the canonical justification for async I/O: with PIO, the CPU spends too long copying data; DMA offloads the copy. Our `ReceiveAsync` in M7 uses DMA under the hood.

## OSEP §-specific deviations

- OSEP §36.7 explains the device-driver abstraction. .NET's `FileStream` is the managed equivalent — it wraps the OS's open/read/close syscalls.
- OSEP §39.7 mentions `fsync()` for durability. Our M11 slice doesn't call `fsync` after writing.
- OSEP §36.4 covers interrupts + DMA. The .NET async path uses both — kernel-level async I/O uses interrupts to wake the waiting thread when the I/O completes.

## Key OSEP quotes

> "When you first open another file (as cat does above), it will almost certainly be file descriptor 3." (OSEP §39.4) — fds 0, 1, 2 are stdin/stdout/stderr.

> "Use strace (and similar tools)" (OSEP §39.5 TIP) — strace shows every syscall a program makes.

## .NET mechanism

- `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.None)`.
- `FileStream.Read(byte[] buffer, int offset, int count)` returns the number of bytes read.
- `FileStream.Close()` calls the `close` syscall.

## Files

- `src/MiniWebServer.Host/RawFileAccess.cs` — `Open(path)` + `ReadAllBytes(handle)` helper.
- `src/MiniWebServer.Host/Program.cs` — `/fs-raw-read?path=` route.

## What this slice does NOT do

- Doesn't go below .NET's `FileStream` to `pread` / `mmap` (would need P/Invoke or a third-party binding).
- Doesn't measure syscall latency (could be added with `ETW` events).
- Doesn't demonstrate `fsync()`.
