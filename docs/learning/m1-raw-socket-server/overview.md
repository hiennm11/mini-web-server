# Milestone 1: Raw Socket Server

> **Overview** — what this milestone covers and where to start. Slices live in this folder.

## Question

What does a minimal TCP server look like in raw socket terms — `bind`, `listen`, `accept`, `receive`, `send`, `close`?

## Scope

A single-threaded server that accepts one client at a time, reads an HTTP request, writes back a hard-coded response, and closes. No parsing, no file serving, no concurrency.

## Slices (in order)

1. **[s1-raw-socket-server.md](./s1-raw-socket-server.md)** — accept one client, write a hard-coded HTTP response, close. The shortest possible "hello world" over HTTP.
2. **[s2-robust-request-receive.md](./s2-robust-request-receive.md)** — handle chunked TCP receive: the request may arrive in multiple `Receive()` calls. Implement the header-end + content-length parsing logic.

## OSTEP concept

OSEP Ch. 4 (Processes), §4.4 (Process States):

- **Running / Ready / Blocked**: a server thread blocks in `Accept()` when no client is waiting. The OS schedules other runnable processes; the server becomes ready when a TCP SYN arrives.
- A user process cannot directly drive the network card. Each socket operation crosses the kernel boundary via a syscall (trap → privileged code → return).

This milestone makes OSEP's process abstraction visible: the server is not "talking to the browser directly". It's a process asking the kernel for controlled access to a byte stream.

## .NET mechanism

- `System.Net.Sockets.Socket` exposes TCP operations directly.
- `Accept()` blocks the current managed thread.
- `Receive()` copies bytes from the socket into a `byte[]` buffer.
- HTTP is written manually as bytes (status line + headers + blank line + body).

## Files in this milestone

- `src/MiniWebServer.Host/Program.cs` — accept loop + `HandleClient` + manual HTTP response
- `src/MiniWebServer.Host/HttpResponse.cs` — `HttpResponse` record + `ToBytes()` / `WriteTo()`
- `src/MiniWebServer.Host/SocketServer.cs` (early slices) — extracted from Program.cs in later slices
