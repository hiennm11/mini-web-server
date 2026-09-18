# ADR 0001: Build-First OSTEP Learning Design

## Status

Accepted

## Date

2026-05-19

> **Roadmap closure note**: All seven milestones (M1-M7) and all twelve roadmap slices are now implemented. This ADR was originally written with M4-M7 as future work. The text below keeps the original design rationale and milestone plan but updates the status of each milestone to reflect that they have been built. See `CONTEXT.md` "OSEP Coverage" for what this repo maps to in the OSTEP textbook and what remains as future slices.

## Source Documents

- `docs/superpowers/specs/2026-05-19-build-first-ostep-learning-design.md`
- `docs/superpowers/plans/2026-05-19-raw-socket-server-milestone-1.md`
- `docs/superpowers/plans/2026-05-19-http-request-understanding-milestone-2.md`
- `docs/superpowers/plans/2026-05-19-static-file-server-milestone-3.md`

## Context

Mini Web Server is a learning lab for understanding how operating-system concepts from *Operating Systems: Three Easy Pieces* show up in C# and .NET server code.

The project should teach from the operating-system boundary upward. It should keep raw TCP sockets, manual HTTP text, explicit byte buffers, file reads, and console feedback visible before introducing higher-level abstractions.

The learning style is build-first:

1. Add one small server capability.
2. Explain the generic operating-system concept exposed by that capability.
3. Explain the matching C#/.NET mechanism.
4. Run an experiment that makes the behavior observable.
5. Capture short repo notes when the lesson matters.

This is not a chapter-by-chapter OSTEP reading plan. OSTEP concepts are introduced when code makes them concrete.

## Decision

We will preserve Mini Web Server as a low-level, build-first learning project.

Implementation should keep the server close to the operating-system boundary:

- use `System.Net.Sockets.Socket` directly;
- use explicit `Bind`, `Listen`, `Accept`, `Receive`, and `Send` behavior;
- manually format and parse HTTP text where useful for learning;
- use explicit buffers and byte/string decoding;
- keep console logs clear enough to show the runtime lifecycle;
- prefer small, focused pure types only when they make behavior testable without hiding the socket lifecycle.

We will avoid ASP.NET Core, Kestrel, `HttpListener`, TLS, middleware pipelines, dependency injection frameworks, and production-grade HTTP support unless a future ADR explicitly changes the project goal.

The server is a learning lab, not a production web server.

## Learning Unit Decision

Milestones are intentionally coarse-grained capability checkpoints. They are not the unit of daily implementation.

For execution, each milestone must be decomposed into **lesson slices**. A lesson slice is a 30–90 minute build-first exercise that exposes one observable OS behavior inside a repository file.

Each lesson slice must define:

- OSTEP concept (backed by NotebookLM query).
- C#/.NET mechanism.
- Small code behavior to build.
- Experiment command.
- Observation prompt.
- Learning note output path.

Lesson slice templates and conventions live in `docs/learning/README.md`.

The OSTEP NotebookLM notebook provides concept-to-code mapping. Before implementing any milestone 4+ slice, query that notebook for the relevant chapter context.

## Milestone Roadmap

### Milestone 1: Raw Socket Server — implemented

Goal: improve the single-threaded raw socket server so request receiving, connection handling, and learning feedback are clearer while preserving its low-level educational shape.

Decisions:

- Keep one console executable using `System.Net.Sockets.Socket` directly.
- Make constants and helper boundaries explicit: port, listen backlog, receive buffer size, response creation, send-all, and per-client handling.
- Keep helper methods small and close to `Program.cs` when they clarify the socket lifecycle.
- Add minimal per-client socket error handling so one failed client does not stop the host.
- Verify primarily through `dotnet build`, `dotnet run`, `curl`, and console logs.

Learning focus:

- OS: process lifecycle, system calls, blocking I/O, socket descriptors.
- .NET: `Socket`, `Bind`, `Listen`, `Accept`, `Receive`, `Send`, byte arrays, UTF-8 decoding.
- Experiment: show that `Accept()` blocks until a client connects and that the host handles one client at a time.

Status: implemented and documented in `docs/learning/m1-raw-socket-server/overview.md`.

### Milestone 2: HTTP Request Understanding — implemented

Goal: parse raw HTTP request text into method, path, version, and headers, then log the parsed view beside the raw bytes.

Decisions:

- Keep raw sockets in `MiniWebServer.Host`.
- Add tiny pure parsing types so parsing can be tested without opening sockets.
- Add a no-dependency console test project that references the host project and exits nonzero on failed assertions.
- Parse the request line by spaces into method, path, and version.
- Parse headers until the blank line.
- Return an unknown request for malformed input.
- Keep the host response fixed as `Hello World!` until static file serving is introduced.

Learning focus:

- OS: TCP is a byte stream, not a message protocol.
- .NET: buffer boundaries, string decoding, request parsing, defensive input handling.
- Experiment: send different `curl` requests and compare raw bytes against parsed values.

Status: implemented and documented in `docs/learning/m2-http-request/overview.md`.

### Milestone 3: Static File Server — implemented

Goal: use the parsed request path to serve files from `wwwroot`.

Decisions:

- Keep `Program.cs` as the raw socket host.
- Add pure response/file-serving types testable without sockets.
- Use `HttpResponse` to format status line, headers, and body bytes.
- Use `StaticFileResponder` to map request paths to safe file paths under `wwwroot`.
- Serve `wwwroot/index.html` for `/`.
- Return `404 Not Found` for missing files and path traversal attempts.
- Start with `File.ReadAllBytes(...)` and minimal content-type support, accepting the learning tradeoff that large files are loaded all at once.
- Update context and learning notes after implementation.

Learning focus:

- OS: file naming, open/read/close, buffering, disk latency, page cache.
- .NET: file reads, path normalization, MIME mapping, response byte formatting.
- Experiment: serve a file, request a missing file, and verify path traversal cannot escape `wwwroot`.

Status: implemented and documented in `docs/learning/m3-static-file-server/overview.md`.

### Milestone 4: Thread-Per-Connection — implemented

Goal: learn why a single-threaded server blocks clients and how threads change server behavior.

This milestone was implemented through lesson slices, not as one large change. Each slice introduced one observable OS behavior before moving to the next.

| Slices (see `docs/learning/m4-thread-per-connection/overview.md` for the full plan and `docs/learning/s4.*.md` for each slice's learning note):

- 4.1 Prove single-thread blocking (process states: Running / Ready / Blocked) — `Thread.Sleep`
- 4.2 Spawn one thread per client (thread = multiple execution points) — `new Thread(...).Start()`
- 4.3 Observe thread identity and scheduling (scheduler non-determinism) — `ManagedThreadId`
- 4.4 Shared address space appears (shared heap vs independent stacks) — `Interlocked.Increment`
- 4.5 Prepare race condition lab (shared mutable state danger) — bare `counter++`
- 4.6 Thread-per-connection limits (stack + context-switch overhead) — `Process.PrivateMemorySize64`

Learning focus per slice:

| Slice | OS concept | .NET mechanism |
|-------|-----------|---------------|
| 4.1 | Process states, blocking I/O | `Thread.Sleep`, timing logs |
| 4.2 | Threads, multiple PCs | `new Thread(() => ...).Start()` |
| 4.3 | Scheduler, context switch | `Thread.CurrentThread.ManagedThreadId` |
| 4.4 | Shared address space | `Interlocked.Increment` + per-thread local |
| 4.5 | Race condition, critical section | Bare `counter++` under concurrent requests |
| 4.6 | Thread stack overhead | `Process.Threads.Count` + `PrivateMemorySize64` under 50/150 parked slow clients |

### Milestone 5: Race Conditions Lab — implemented

Goal: make shared mutable state visible, then fix it.

Implemented as `/race-safe` route that runs the same loop as slice 4.5's `/race` but inside `lock (RequestStats.SafeCounterLock)`. Smoke confirmed 4 concurrent × 1M increments produce exactly the deterministic total, while the non-locked `/race` still loses ~20-30%.

Learning focus:

- OS: race conditions, critical sections, mutual exclusion via locks.
- .NET: `lock` (= `Monitor.Enter`/`Monitor.Exit`).
- Pairing with `Interlocked.Increment` shows the right primitive choice for each shape of critical section.

Status: implemented and documented in `docs/learning/m5-race-lab/s1-race-lab.md`.

### Milestone 6: Thread Pool And Work Queue — implemented

Goal: replace unbounded thread creation with bounded work.

Implemented as `WorkerPool` static class with `Queue<Socket>` + lock + `Monitor.Wait`/`Monitor.Pulse` (= `pthread_cond_wait`/`signal`). Accept thread does `WorkerPool.Enqueue`; 8 long-lived worker threads dequeue and run `HandleClient`. Smoke proved 150 parked slow clients now use 17 threads and ~21 MB private memory (vs ~150+ threads and ~174 MB in slice 4.6).

Learning focus:

- OS: producer/consumer queue, condition variables, bounded concurrency, Mesa semantics.
- .NET: `Monitor.Wait`/`Pulse`/Monitor lock, `Queue<T>`.
- Comparison with slice 4.6 quantifies the win.

Status: implemented and documented in `docs/learning/m6-bounded-worker-pool/s1-bounded-worker-pool.md`. Queue is still unbounded; bounded-queue backpressure is a future slice.

### Milestone 7: Async/Event-Based Server — implemented

Goal: explore event-based concurrency after the threaded model is understood.

Implemented as `AsyncServer` selected via `--async` CLI flag (default mode remains the M6 worker pool). Single-threaded `AcceptAsync` loop; each accepted connection becomes a `Task` driving `ReceiveAsync`/`SendAsync`. Smoke proved 150 parked slow clients use ~20 threads (vs ~150+ in slice 4.6), proving the OSEP Ch. 33 lesson of "many connections with few threads". Memory did not drop below M6's because per-connection 1 MB receive buffer dominates; that win is for a future slice with `ArrayPool<byte>`.

Learning focus:

- OS: event loop, async I/O, continuation-style control flow, `select`/`epoll` (via `AcceptAsync`).
- .NET: `Task`, `async`/`await` state machines, `Socket.AcceptAsync`/`ReceiveAsync`/`SendAsync`.
- Two-mode comparison: worker pool vs async under the same stress test.

Status: implemented and documented in `docs/learning/m7-async-event-based/s1-async-event-based.md`. The default mode is still the M6 worker pool.

## Consequences

Good:

- The repo stays aligned with its educational purpose.
- Each server capability maps directly to visible OS and .NET concepts.
- Pure parsing and response logic can be tested without socket setup.
- Console output remains part of the learning feedback loop.
- All seven milestones are now closed; the repo is a complete OS concepts lab for the core three pieces of OSEP (Virtualization, Concurrency, Persistence).

Tradeoffs:

- The server intentionally remains limited and non-production-ready.
- Some implementation choices are simpler than robust HTTP servers would require.
- Future production features require explicit ADRs because they may undermine the learning goal.

## Verification Strategy

- Early socket behavior can be verified with `dotnet build`, `dotnet run`, `curl`, and console logs.
- Pure parsing and response-formatting logic should have focused tests.
- Concurrency milestones should include repeatable stress commands or small test harnesses where practical.
