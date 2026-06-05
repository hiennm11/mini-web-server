# ADR 0001: Build-First OSTEP Learning Design

## Status

Accepted

## Date

2026-05-19

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

Lesson slice templates and conventions live in `docs/learning/lesson-slices.md`.

The OSTEP NotebookLM notebook provides concept-to-code mapping. Before implementing any milestone 4+ slice, query that notebook for the relevant chapter context.

## Milestone Roadmap

### Milestone 1: Raw Socket Server

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

Status: implemented and documented in `docs/learning/milestone-1-raw-socket-server.md`.

### Milestone 2: HTTP Request Understanding

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

Status: implemented and documented in `docs/learning/milestone-2-http-request-understanding.md`.

### Milestone 3: Static File Server

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

Status: implemented and documented in `docs/learning/milestone-3-static-file-server.md`.

### Future Milestone 4: Thread-Per-Connection (planned — not yet sliced)

Goal: learn why a single-threaded server blocks clients and how threads change server behavior.

This milestone must be implemented through lesson slices, not as one large change. Each slice introduces one observable OS behavior before moving to the next.

Planned slices (see `docs/learning/milestone-4-thread-per-connection-plan.md`):

- 4.1 Prove single-thread blocking (process states: Running / Ready / Blocked)
- 4.2 Spawn one thread per client (thread = multiple execution points)
- 4.3 Observe thread identity and scheduling (scheduler non-determinism)
- 4.4 Shared address space appears (shared heap vs independent stacks)
- 4.5 Prepare race condition lab (shared mutable state danger)
- 4.6 Thread-per-connection limits (stack + context-switch overhead)

Learning focus per slice:

| Slice | OS concept | .NET mechanism |
|-------|-----------|---------------|
| 4.1 | Process states, blocking I/O | `Thread.Sleep`, timing logs |
| 4.2 | Threads, multiple PCs | `new Thread(() => ...).Start()` |
| 4.3 | Scheduler, context switch | `Thread.CurrentThread.ManagedThreadId` |
| 4.4 | Shared address space | `static` variables vs locals |
| 4.5 | Race condition, critical section | Unsafe `counter++` (no lock yet) |
| 4.6 | Thread stack overhead | Connection stress test |

### Future Milestone 5: Race Conditions Lab

Goal: make shared mutable state visible, then fix it.

Planned learning focus:

- OS: race conditions, critical sections, locks.
- .NET: `lock`, `Monitor`, `Interlocked`, memory visibility.
- Experiment: stress concurrent requests and compare unsafe counting with synchronized counting.

### Future Milestone 6: Thread Pool And Work Queue

Goal: replace unbounded thread creation with bounded work.

Planned learning focus:

- OS: producer/consumer, bounded buffer, backpressure, condition variables.
- .NET: `Queue<T>`, `Monitor.Wait`, `Monitor.Pulse`, `ThreadPool` comparisons.
- Experiment: cap the queue and observe what happens when clients arrive faster than workers can respond.

### Future Milestone 7: Async/Event-Based Server

Goal: explore event-based concurrency after the threaded model is understood.

Planned learning focus:

- OS: event loops, non-blocking I/O, asynchronous I/O, continuation-style control flow.
- .NET: `AcceptAsync`, `ReceiveAsync`, `SendAsync`, `Task`, async state machines, runtime thread-pool behavior.
- Experiment: compare blocked threads in the threaded version with async operations under concurrent load.

## Consequences

Good:

- The repo stays aligned with its educational purpose.
- Each server capability maps directly to visible OS and .NET concepts.
- Pure parsing and response logic can be tested without socket setup.
- Console output remains part of the learning feedback loop.

Tradeoffs:

- The server intentionally remains limited and non-production-ready.
- Some implementation choices are simpler than robust HTTP servers would require.
- Future production features require explicit ADRs because they may undermine the learning goal.

## Verification Strategy

- Early socket behavior can be verified with `dotnet build`, `dotnet run`, `curl`, and console logs.
- Pure parsing and response-formatting logic should have focused tests.
- Concurrency milestones should include repeatable stress commands or small test harnesses where practical.
