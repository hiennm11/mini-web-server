# Milestone 1: Raw Socket Server

> **Overview** — what this milestone covers and where to start. Slices live in this folder.

## Question

What does a minimal TCP server look like in raw socket terms — `bind`, `listen`, `accept`, `receive`, `send`, `close`?

## Scope

A single-threaded server that accepts one client at a time, reads an HTTP request, writes back a hard-coded response, and closes. No parsing, no file serving, no concurrency.

## Slices (in order)

1. **[s1-raw-socket-server.md](./s1-raw-socket-server.md)** — accept one client, write a hard-coded HTTP response, close.
2. **[s2-robust-request-receive.md](./s2-robust-request-receive.md)** — handle chunked TCP receive: the request may arrive in multiple `Receive()` calls. Implement the header-end + content-length parsing logic.

## OSTEP coverage

- **Ch. 4 The Abstraction: The Process** (§4.1 process = running program, §4.4 process states Running/Ready/Blocked).
- **Ch. 6 Mechanism: Limited Direct Execution** (§6.1 direct execution, §6.2 system calls for restricted ops, §6.3 timer interrupt + context switch).

The server is the first place the project makes OSEP visible: the server is **a process** running user-mode code. It can't directly drive the network card — it asks the OS to handle network I/O via **syscalls** (`bind`, `listen`, `accept`, `receive`, `send`, `close`). Each syscall traps into kernel mode, the kernel performs the privileged operation, and returns to user mode.

OSEP §4.4 explains why blocking calls are useful instead of wasteful: `Accept()` blocks the server thread when no client is waiting. The OS marks the process `Blocked`, schedules other runnable work, and only marks the server `Ready` again when a TCP SYN arrives.

## OSEP §-specific deviations

- OSEP §4.4 introduces three process states; we don't model these explicitly — the OS does it for us. Our server is single-threaded so only one "point of execution" exists.
- OSEP §6.2 shows the syscall mechanism via trap instructions. .NET wraps this: `Socket.Accept()` looks like a normal method call but compiles to a syscall under the hood.
- OSEP §6.3 timer interrupt — we don't observe the timer directly; the OS handles context switches on our behalf when we block in `Accept()`.

## .NET mechanism

- `System.Net.Sockets.Socket` exposes TCP operations directly. `Accept()` blocks the current managed thread.
- `Receive()` copies bytes from the socket into a `byte[]` buffer.
- `Encoding.UTF8.GetString(...)` converts bytes to text for logging.

## Files in this milestone

- `src/MiniWebServer.Host/Program.cs` — accept loop + `HandleClient` + manual HTTP response.
- `src/MiniWebServer.Host/HttpResponse.cs` — `HttpResponse` record + `ToBytes()` / `WriteTo()`.

## Where this leads

- M2: parse the raw request into structured form.
- M3: serve a real file instead of a hard-coded response.
- M4: add threads so the server doesn't block on a single slow client.
