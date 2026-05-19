# Build-First OSTEP Learning Design

## Purpose

Use Mini Web Server as a hands-on lab for learning operating-system concepts from *Operating Systems: Three Easy Pieces* while also learning how those concepts appear in C# and .NET.

The project should stay close to the operating-system boundary. It should use raw TCP sockets, manual HTTP text, explicit buffers, and visible console feedback before introducing higher-level abstractions.

## Learning Style

The preferred style is build-first learning:

1. Add one small server capability.
2. Explain the generic operating-system concept exposed by that capability.
3. Explain the matching C#/.NET concept.
4. Run an experiment that makes the behavior observable.
5. Capture short notes in the repo when the lesson is important.

This is not a chapter-by-chapter reading plan. OSTEP chapters are used when they become relevant to code.

## Milestones

### 1. Raw Socket Server

Improve the current single-threaded server without changing its educational shape.

- Build: better request receive buffer, clearer logging, simple error handling, and a clean connection lifecycle.
- OS concept: process lifecycle, system calls, blocking I/O, socket descriptors.
- .NET concept: `Socket`, `Bind`, `Listen`, `Accept`, `Receive`, `Send`, byte arrays, UTF-8 decoding.
- Experiment: show that `Accept` blocks until a client connects and that the host handles one client at a time.

### 2. HTTP Request Understanding

Parse the first request line and selected headers by hand.

- Build: parse method, path, version, and headers from the raw request.
- OS concept: TCP is a byte stream, not a message protocol.
- .NET concept: buffer boundaries, string decoding, request parsing, defensive input handling.
- Experiment: send different requests with `curl` and observe the raw bytes versus parsed values.

### 3. Static File Server

Serve files from a local `wwwroot` directory.

- Build: map request paths to files, return `404 Not Found`, and send basic content types.
- OS concept: file naming, open/read/close, buffering, disk latency, page cache.
- .NET concept: `FileStream`, chunked reads, path normalization, MIME mapping.
- Experiment: serve a text file and a larger file, then compare first and repeated reads.

### 4. Thread-Per-Connection

Allow multiple clients to be handled concurrently.

- Build: create a worker thread for each accepted client socket.
- OS concept: threads, shared address space, blocking I/O overlap, scheduling, context switching.
- .NET concept: `Thread`, thread stacks, shared static state, exception boundaries.
- Experiment: hold one request open and show that another client can still receive a response.

### 5. Race Conditions Lab

Make shared mutable state visible and then fix it.

- Build: add a total request counter and intentionally update it from multiple threads.
- OS concept: race conditions, critical sections, locks.
- .NET concept: `lock`, `Monitor`, `Interlocked`, memory visibility.
- Experiment: stress the server with concurrent requests and compare unsafe counting with synchronized counting.

### 6. Thread Pool And Work Queue

Replace unbounded thread creation with bounded work.

- Build: accept client sockets into a bounded queue and process them with a fixed worker pool.
- OS concept: producer/consumer, bounded buffer, backpressure, condition variables.
- .NET concept: `Queue<T>`, `Monitor.Wait`, `Monitor.Pulse`, `ThreadPool` comparisons.
- Experiment: cap the queue and observe what happens when clients arrive faster than workers can respond.

### 7. Async/Event-Based Server

Explore event-based concurrency after the threaded model is understood.

- Build: use async socket operations for accept, receive, and send.
- OS concept: event loops, non-blocking I/O, asynchronous I/O, continuation-style control flow.
- .NET concept: `AcceptAsync`, `ReceiveAsync`, `SendAsync`, `Task`, async state machines, runtime thread-pool behavior.
- Experiment: compare blocked threads in the threaded version with async operations under concurrent load.

## Working Agreement

Each milestone should start with a short explanation of the next behavior to build. Implementation should stay small enough to understand in one sitting.

For each milestone, Codex should provide:

- The code change.
- The OSTEP concept behind the change.
- The C#/.NET mechanism used by the code.
- One or more commands to run.
- A short observation prompt so the learner predicts or interprets the result.

## Testing And Verification

Verification should grow with the project:

- Early milestones can be checked with `dotnet build`, `dotnet run`, and `curl`.
- Pure parsing and response-formatting logic should get focused unit tests when it is extracted.
- Concurrency milestones should include repeatable stress commands or small test harnesses where practical.

## Out Of Scope For Now

The project should not use ASP.NET Core, Kestrel, `HttpListener`, TLS, middleware pipelines, dependency injection frameworks, or production-grade HTTP support unless a future milestone explicitly changes the educational goal.

The server is a learning lab, not a production web server.

