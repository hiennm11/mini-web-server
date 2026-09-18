# Mini Web Server Context

## Status Snapshot

Updated 2026-09-17. Legend: ✅ built + experimented + noted · 🟡 planned · ⬜ future.

| Slice | Capability | OSTEP chapter | Status |
|-------|-----------|---------------|--------|
| 1.1 | Raw socket lifecycle (`Socket.Bind`/`Listen`/`Accept`/`Receive`/`Send`) | Ch. 4, 6 | ✅ |
| 1.2 | Parse HTTP request → method, path, version, headers | Ch. 4, 13, 36 | ✅ |
| 1.3 | Serve static files from `wwwroot`, reject path traversal | Ch. 39 | ✅ |
| 1.4 | Robust receive loop until `\r\n\r\n` + `Content-Length` bytes | Ch. 4.4, 36 | ✅ |
| 4.1 | Prove single-thread blocking via `/slow` (`Thread.Sleep`) | Ch. 4, 4.4 | ✅ |
| 4.2 | Spawn one background thread per accepted client | Ch. 26, 27 | ✅ |
| 4.3 | Log `ManagedThreadId`, observe scheduler non-determinism | Ch. 26, 4.4 | ✅ |
| 4.4 | Show shared address space (`static` vs locals) | Ch. 13, 26 | ✅ |
| 4.5 | Reproduce OSTEP `threads.c` race (`counter++`) | Ch. 26, 28 | ✅ |
| 4.6 | Stress thread-per-connection stack limit | Ch. 26, 27 | ✅ |
| M5 | Race lab: fix with `lock` / `Interlocked` | Ch. 28 | ✅ |
| M6 | Bounded worker pool + producer-consumer queue | Ch. 30, 31 | ✅ |
| M7 | Async / event-based server | Ch. 33, 36 | ✅ |
| M8 (6.3) | Bounded queue + 503 backpressure in `WorkerPool` | Ch. 30, 31 | ✅ |
| M9 | Reader-writer lock + process-stats cache | Ch. 31.5 | ✅ |
| M10 | Async mode `--max-threads` / `--min-threads` ThreadPool cap | Ch. 33 | ✅ |
| M11 | Raw `open`/`read`/`close` syscall demo (FileStream + `/read-syscall`) | Ch. 39 | ✅ |
| M12 (12.1–12.4) | Mini FS: superblock, bitmaps, inode table, dir ops, HTTP routes | Ch. 40 | ✅ (slice 12.5 journal deferred) |
| M12 (12.5) | Mini FS journal: TxB/TxE write-ahead log + backing file | Ch. 42 | ✅ |

**Milestones**: M1 ✅–M12 (12.1–12.5) ✅. M8–M12 are the post-roadmap extensions per `docs/adr/0004-extend-broad-concurrency-roadmap.md`; the original 12-slice roadmap is closed. M12 slice 12.5 (write-ahead log for crash consistency, OSEP §42.3) is now done.
**Tests**: 15 passing (8 prior + 7 new for `HttpRequestReceiver`).
**Code/runtime**: `net10.0`. Two run modes selectable via `--async` flag: default = bounded worker pool (8 threads) + producer/consumer queue, async = `AcceptAsync` + `Task` per connection with `ReceiveAsync` / `SendAsync`. Port 8080, static files under `wwwroot`.
**Latest commit**: Milestone 12 (slice 12.5) — `Journal` class with OSEP §42.3 write-ahead log (TxB + DATA + TxE per transaction). Every `WriteBlock` now goes through the journal; the journal region (disk blocks 7-14) is reserved and `Balloc` skips it. New `MountFromFile(path?)` + `SaveToFile(path)` + `MINIFS_IMAGE` env var let the FS persist across process restarts; `/fs-save` HTTP route exposes the save. Smoke 1: create file + write 17B + save + kill + restart + read returns the same 17B. Smoke 2: inject orphan TxB + create+write+save + restart + the orphan is discarded (no matching TxE) and the committed file survives. Build clean, 15/15 tests pass.

## OSTEP Coverage

The book has ~50 chapters. Repo maps **the core three pieces** (Virtualization + Concurrency + Persistence) end-to-end:

| Piece | Coverage | Roadmap slices |
|---|---|---|
| **Virtualization** | Ch. 4 process, 6 LDE, 13 address space, 26-27 thread = point of execution, 33 event-based | 1.1, 1.4, 4.4, M7 |
| **Concurrency** | Ch. 26 race, 27 thread API, 28 locks, 30 condition variables, 31 semaphores, 31.5 reader-writer, 33 event-based | 4.1–4.6, M5, M6, M7, M8, M9, M10 |
| **Persistence** | Ch. 36 I/O devices (TCP receive loop), 39 file API (basic static files; M11 covers open/read/close syscalls; no write/seek/unlink demo), 40 vsfs layout / inode / bitmap / directory, 42 crash consistency / journaling / write-ahead log | 1.3, 1.4, M11, M12 |

**Roughly 30% of OSTEP chapters have working code in this repo.**

Chapters **not yet implemented** (natural next slices):

- **Part I Virtualization**: Ch. 7 process API, Ch. 8-10 scheduling (MLFQ, lottery), Ch. 14-23 paging & advanced VM (TLB, multi-level page tables, swapping, complete VM systems)
- **Part II Concurrency**: Ch. 29 lock-free data structures, Ch. 31.6 dining philosophers (M9 covered Ch. 31.5 reader-writer)
- **Part III Persistence** (largest gap): Ch. 36-38 device drivers & RAID, Ch. 39 file API is partially covered by M11 (open/read/close only — no write/seek/unlink demo), Ch. 41-45 file system implementation (FFS, journaling, LFS, flash) — M12 covers Ch. 40 vsfs and would extend into Ch. 42 via slice 12.5
- **Part IV Security** (entirely untouched): Ch. 53-57

The repo is best understood as an **OS concepts lab for the core three pieces**, not a full reproduction of the textbook. The concurrency chapter sweep (race observable → race fixed → pool → async) is the most complete coverage; persistence is reduced to "serve files from a directory"; virtualization covers thread/process abstraction but not CPU scheduling or paging.

## Purpose

Mini Web Server is a learning-oriented .NET console application that demonstrates how a minimal HTTP server works directly on top of TCP sockets. It does not use ASP.NET Core or `HttpListener`; the point is to expose the operating-system-level network steps: create a socket, bind it to a port, listen, accept a client connection, read raw bytes, write a raw HTTP response, and close the connection.

## Current Shape

The repository is a single-context .NET solution.

- `MiniWebServer.sln` contains one host project.
- `src/MiniWebServer.Host/MiniWebServer.Host.csproj` builds a `net10.0` executable.
- `src/MiniWebServer.Host/Program.cs` is the startup. Branches on `--async` CLI flag; default starts the bounded worker-pool server; `--async` starts the event-based server. Accepts and parses `--async`.
- `src/MiniWebServer.Host/WorkerPool.cs` is the bounded pool (Phase 3, default mode). Queue + lock + `Monitor.Wait`/`Monitor.Pulse` + 8 worker threads. Accept loop just `Enqueue`s.
- `src/MiniWebServer.Host/AsyncServer.cs` is the event-based server (Phase 4, `--async` mode). Single-threaded `AcceptAsync` loop + `Task` per connection with `ReceiveAsync` / `SendAsync`.
- `src/MiniWebServer.Host/HttpRequest.cs` models the parsed request line and headers.
- `src/MiniWebServer.Host/HttpRequestParser.cs` parses raw HTTP request text.
- `src/MiniWebServer.Host/HttpRequestReceiver.cs` is the pure helper for header-terminator + Content-Length detection used by both modes.
- `src/MiniWebServer.Host/HttpResponse.cs` formats raw HTTP response bytes.
- `src/MiniWebServer.Host/StaticFileResponder.cs` maps parsed request paths to files under `wwwroot`.
- `src/MiniWebServer.Host/WebRootLocator.cs` locates the runtime `wwwroot` directory.
- `src/MiniWebServer.Host/ServerConfig.cs` holds the `MaxRequestBytes` and `RaceIterations` constants shared by both server modes.
- `src/MiniWebServer.Host/RequestStats` (nested inside Program.cs) holds `TotalRequests` (atomic), `UnsafeCounter` (non-atomic race demo), `SafeCounter` + `SafeCounterLock` (lock-fixed demo).
- `src/MiniWebServer.Host/wwwroot/index.html` is the default static page for `/` and is copied to the build output.
- `tests/MiniWebServer.Host.Tests/` contains console-based parser tests.

`docs/adr/0001-build-first-ostep-learning-design.md` records the accepted build-first OSTEP learning direction and milestone roadmap.

`docs/adr/0003-four-phase-ostep-roadmap.md` records the accepted four-phase OSEP learning roadmap:

1. Phase 1: The Process & The Byte Stream.
2. Phase 2: Threads: Multiple Points of Execution.
3. Phase 3: Bounded Concurrency.
4. Phase 4: Event-Based Concurrency.

All four phases are complete; see the **OSEP Coverage** section above for what this maps to in the textbook.

## Glossary

- **Host**: The executable process that owns the socket server and runs continuously until stopped.
- **Server socket**: The listening TCP socket created by the host with `AddressFamily.InterNetwork`, `SocketType.Stream`, and `ProtocolType.Tcp`.
- **Endpoint**: The address and port the server binds to. The current endpoint is `IPAddress.Any` on port `8080`.
- **Listen backlog**: The maximum queue length for pending connections. The current backlog is `10`.
- **Client socket**: The accepted per-connection socket returned by `serverSocket.Accept()`.
- **Raw HTTP request**: Bytes received from a client socket and decoded as UTF-8 for console logging.
- **Parsed HTTP request**: A structured view of the raw request text containing method, path, version, and headers.
- **Request line**: The first line of an HTTP request, such as `GET /ostep HTTP/1.1`.
- **Header**: A `Name: Value` line after the request line, parsed into the request header dictionary.
- **Raw HTTP response**: A manually formatted HTTP/1.1 response string sent over the client socket.
- **Static file response**: A response generated by reading bytes from a file under `wwwroot`.
- **Web root**: The directory files can be served from. The source web root is `src/MiniWebServer.Host/wwwroot`; at runtime it is copied beside the host executable.
- **Path traversal**: A request path such as `/../CONTEXT.md` that tries to escape the web root. These requests return `404 Not Found`.
- **Connection close**: The server closes the client socket after sending the response.

## Runtime Behavior

The host binds to TCP port `8080` on all IPv4 interfaces and begins listening.

### Default mode (worker pool)

The accept loop calls `WorkerPool.Enqueue(clientSocket)`. The pool:

1. Owns 8 long-lived worker threads (created once at startup).
2. Workers wait under a lock on `Monitor.Wait(PoolLock)` while the queue is empty.
3. `Enqueue` adds the socket under the lock and calls `Monitor.Pulse` to wake one worker.
4. The worker dequeues, releases the lock, and runs `HandleClient` for that connection.

Each `HandleClient` invocation:

1. Allocates a 1 MB receive buffer.
2. Loops `Socket.Receive` until the buffer contains `\r\n\r\n` + (optional) `Content-Length` bytes, capped at 1 MB.
3. Decodes the request as UTF-8 and logs the raw bytes.
4. Parses method, path, version, headers.
5. If path is `/slow`, sleeps 30 seconds to simulate blocking I/O.
6. Builds a response: static file from `wwwroot` (default), or one of the demo routes below.
7. Sends the response, logs size, closes the socket.
8. Per-client `SocketException` and generic `Exception` are logged; one bad client does not stop the host.

Routes:

- `/` → `wwwroot/index.html` (200) or 404
- `/slow` → 404 after 30 s sleep
- `/race` → runs 1_000_000 non-atomic increments on `RequestStats.UnsafeCounter` (race demo); returns cumulative value
- `/race-safe` → same loop under `lock (RequestStats.SafeCounterLock)`; deterministic
- `/stats` → process threads + working set + private bytes + total requests
- `/qstats` → worker count + queue length + total requests

### `--async` mode (event-based)

The accept loop calls `serverSocket.AcceptAsync(ct)`. Each accepted connection becomes a `Task` driven by `ReceiveAsync` / `SendAsync`. The same routes and helpers apply. No worker pool, no per-connection OS thread — the runtime `ThreadPool` multiplexes all waiting `Task`s over a small set of workers. Stop with Ctrl+C.

## Learning Workflow

This repo uses two learning levels:

- **Milestone**: a larger server capability that changes what the server can do. Milestones are roadmap units.
- **Lesson slice**: a small build-first exercise inside a milestone, sized for one focused learning session. Lesson slices are execution units.

The roadmap is also grouped into four OSTEP-aligned phases in `docs/adr/0003-four-phase-ostep-roadmap.md`. All four phases are complete (M1–M7). Phases explain the learning progression; milestones and lesson slices remain the execution structure.

Each lesson slice should include:

1. OSTEP concept (from NotebookLM or book).
2. Small server behavior to build.
3. C#/.NET mechanism.
4. Observable experiment.
5. Short learning note.

A lesson slice is 30–90 minutes of focused work: read a concept, build a tiny behavior, run an experiment, and capture what was observed. Milestones exist so the server's evolution direction stays visible; lesson slices exist so daily work is concrete and completable.

Every lesson slice must follow `docs/learning/lesson-slices.md`: start with a concrete question, query OSTEP NotebookLM, design the experiment before code, build one observable behavior, run the experiment, write the note, and pass the 10-rule checklist. The three-question test for every slice is: what is the OS doing, which .NET API exposes it, and where does it break at scale?

The current OSTEP-connected notebook is in NotebookLM: `Operating Systems: Three Easy Pieces` (id `74bcbca0-6161-48cd-92bb-9dd39032794e`, 69 sources).

Phase 1 learning docs (The Process & The Byte Stream):

- `docs/learning/slice-1.1-raw-socket-server.md`
- `docs/learning/slice-1.2-http-request-understanding.md`
- `docs/learning/slice-1.3-static-file-server.md`
- `docs/learning/slice-1.4-robust-request-receive.md`

Phase 2 learning docs (Threads: Multiple Points of Execution):

- `docs/learning/slice-4.1-single-thread-blocking.md`
- `docs/learning/slice-4.2-spawn-thread-per-client.md`
- `docs/learning/slice-4.3-scheduling-non-determinism.md`
- `docs/learning/slice-4.4-shared-address-space.md`
- `docs/learning/slice-4.5-race-condition-prep.md`
- `docs/learning/slice-4.6-thread-per-connection-limits.md`

Phase 3 + 4 learning docs:

- `docs/learning/milestone-5-race-lab.md` (race fixed with `lock`)
- `docs/learning/milestone-6-bounded-worker-pool.md` (bounded pool + producer/consumer; M8 bounded queue appended as section 6.3)
- `docs/learning/slice-6.3-bounded-queue-and-backpressure.md` (M8 full learning note)
- `docs/learning/slice-9-reader-writer-lock.md` (M9 full learning note)
- `docs/learning/slice-10-threadpool-cap.md` (M10 full learning note)
- `docs/learning/slice-11-raw-syscall-demo.md` (M11 full learning note)
- `docs/learning/slice-12-mini-file-system.md` (M12 mini-FS plan + learning note for slices 12.1–12.4)
- `docs/learning/milestone-7-async-event-based.md` (event-based server)

All twelve roadmap slices + milestones have learning notes with smoke-test output captured inline. M8 (the first post-roadmap extension) has its own learning note and a 6.3 section appended to the M6 note.

## Design Intent

Prefer preserving the educational, low-level socket-server character of the project unless a task explicitly asks for a higher-level framework. When adding behavior, make the network lifecycle easy to see and reason about.

The repo follows **build-first learning** (per `docs/learning/lesson-slices.md`): each new behavior is a 30-90 min slice with one observable OS phenomenon, a smoke test, and a learning note. Chapters of OSEP are read on demand when a slice needs them, not end-to-end.

**Source provenance**: slice docs cite specific OSEP sections. A two-pass doc sweep was done in 2026-09 against the source PDFs (`pages.cs.wisc.edu/~remzi/OSTEP/*.pdf`):

1. **Pass 1 (M9–M12)**: verified Ch. 30 (condition variables), 31 (semaphores + 31.5 reader-writer), 33 (event-based), 39 (files & directories), 40 (vsfs). Several errors found and corrected: §30.4 misattribution for the "while not if" / "two CVs" / "hold lock while signaling" lessons (those are §30.1 / §30.2); the "vsfs = xv6" mistake in M12 (vsfs is OSEP's own design, xv6 is a separate OS); the bogus "log" claim in M12 (vsfs has no log; that's Ch. 42).

2. **Pass 2 (M1–M5)**: verified Ch. 4 (process + 4.4 states), 6 (LDE), 13 (address space), 26 (concurrency), 27 (thread API), 28 (locks), 36 (I/O). One error found: slice-4.3 cited §26.4 figure 26.7 (the race example) for scheduler non-determinism, but the right citation is §26.2 t0.c + figures 26.3/26.4/26.5 (the non-deterministic ordering example). The M5 race-lab citations for §28.1-2 / §28.7-9 / §28.12-14 are accurate to the chapter's actual section breakdown.

After both passes, every "Key point from OSEP §X.Y" should match the cited chapter's actual table of contents. Future readers should still treat the citations as a pointer to where the lesson lives, not as an authoritative cross-reference, but the section numbers should now be correct.

Useful directions that fit the project:

- Extend concurrency via the next-milestones roadmap in `docs/adr/0004-extend-broad-concurrency-roadmap.md`. M8 (bounded queue + 503), M9 (reader-writer lock + cache), M10 (ThreadPool cap), M11 (raw open/read/close syscall demo), and M12 (mini file system slices 12.1–12.5) are done.
- Add `ArrayPool<byte>` to lower per-connection memory in async mode (would change the M7 numbers from 172 MB toward M6's 21 MB).
- Add a `Retry-After` header to the M8 503 response so clients can back off intelligently.
- Extend the **OSEP coverage gaps** in the OSEP Coverage section: scheduling (Ch. 7-10), paging (Ch. 14-23), full FS (Ch. 36-45), security (Ch. 53-57). Each new chapter group should get its own ADR before any slices start.
- Extract small concepts such as request receiving, response formatting, and connection handling.
- Add focused tests around pure logic if response formatting or request parsing is introduced.
- Keep console output clear because it is part of the learning feedback loop.

Directions that change the project meaning and should be explicit decisions:

- Replacing sockets with ASP.NET Core, Kestrel, or `HttpListener`.
- Adding production features such as TLS, keep-alive, middleware, dependency injection, or background worker infrastructure.
- Removing the `--async` flag and committing to one concurrency model.

## Commands

Build the solution:

```powershell
dotnet build MiniWebServer.sln
```

Run the host (default = bounded worker-pool mode):

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Run the host in event-based mode (single-threaded accept loop, `Task` per connection):

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj -- --async
```

The host uses the `wwwroot` directory copied beside the executable, so it works even when `dotnet run --project` is started from a different current directory.

Try it from another terminal while the host is running:

```powershell
curl http://localhost:8080/
curl -i http://localhost:8080/missing.txt
curl http://localhost:8080/slow
curl http://localhost:8080/stats
curl http://localhost:8080/qstats
```

Run parser tests:

```powershell
dotnet run --project tests/MiniWebServer.Host.Tests/MiniWebServer.Host.Tests.csproj
```

## Known Limitations

- Port `8080` is hard-coded.
- The async-mode accept loop has no `CancellationToken` for graceful shutdown — Ctrl+C still terminates the process; no in-flight requests are drained.
- The worker pool queue (`Queue<Socket>`) is unbounded. A sustained burst can grow memory without limit.
- `ThreadPool` size in async mode is unbounded by default. Cap it via `ThreadPool.SetMaxThreads` if you want a measured-backpressure story.
- Static file reads use `File.ReadAllBytes(...)`, so large files are loaded into memory all at once. Adding `ArrayPool<byte>` + streaming would lower memory in both modes.
- Content type support is minimal (HTML, CSS, JS, plain text).
- No TLS, no keep-alive, no chunked transfer encoding, no HTTP/2.
- Each connection allocates its own 1 MB receive buffer (`ServerConfig.MaxRequestBytes`).
- The language-service file `src/MiniWebServer.Host/MiniWebServer.Host.csproj.lscache` is generated by C# Dev Kit and is not source logic.
