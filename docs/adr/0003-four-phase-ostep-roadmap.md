# ADR 0003: Four-Phase OSTEP Learning Roadmap

## Status

Accepted

## Date

2026-06-08

## Context

ADR 0001 defines the build-first OSTEP learning direction and the current milestone roadmap. ADR 0002 proves the single-thread blocking baseline before adding threads.

Milestones 1-3 are complete. Milestone 4 is in progress: slice 4.1 is complete, and the remaining thread-per-connection slices are planned.

The project now needs a higher-level learning roadmap that maps server capabilities to OSTEP's core structure. Milestones remain capability checkpoints. Phases explain the learning progression from easy to hard.

The project must keep its low-level learning shape:

- use `System.Net.Sockets.Socket` directly;
- avoid Kestrel, ASP.NET Core, `HttpListener`, and HTTP server libraries;
- make OS behavior observable through code, commands, and console output;
- decompose each phase into lesson slices before implementation.

## Decision

We will organize the Mini Web Server learning path into four OSTEP-aligned phases:

1. **The Process & The Byte Stream** — raw socket lifecycle, HTTP parsing, static file serving, robust request receiving.
2. **Threads: Multiple Points of Execution** — thread-per-connection, scheduler observation, shared address space, races, locks, thread limits.
3. **Bounded Concurrency** — fixed worker pool, producer-consumer queue, condition variables, backpressure.
4. **Event-Based Concurrency** — async sockets, event loop, non-blocking I/O, many connections with few threads.

This ADR does not replace the existing milestone documents. It groups them into a simpler learning arc and adds one missing Phase 1 slice: robust multi-call request receiving.

Each phase must be implemented through lesson slices following `docs/learning/lesson-slices.md`.

## Phase 1: The Process & The Byte Stream

Status: mostly complete. Milestones 1-3 are complete. One new slice remains.

### Features

| Slice | Server behavior to build | OSTEP mapping | C#/.NET mechanism |
|-------|--------------------------|---------------|-------------------|
| Done | Raw socket lifecycle: `Bind` -> `Listen` -> `Accept` -> `Receive` -> `Send` -> `Close` | Chapter 4: The Abstraction: The Process; Chapter 6: Limited Direct Execution | `Socket`, `Bind`, `Listen`, `Accept`, `Receive`, `Send` |
| Done | Parse HTTP request text into method, path, version, and headers | Chapter 4: Process state and address space; Chapter 36: I/O Devices | `Socket.Receive`, `byte[]`, `Encoding.UTF8`, `HttpRequestParser` |
| Done | Serve static files from `wwwroot` and reject unsafe paths | Chapter 39: Files and Directories | `Path.GetFullPath`, `File.Exists`, `File.ReadAllBytes` |
| New | Robust receive: loop `Receive()` until the full request headers and expected body are available | Chapter 4.4: Process States; Chapter 36: I/O Devices | receive loop, buffer accumulation, header delimiter detection, `Content-Length` handling |

### Concept Summary

A process is a running program with its own address space and execution state. When the only server thread blocks in `Accept()` or `Receive()`, the OS can run other work, but this server cannot accept or handle another client until that thread runs again.

TCP gives the server an ordered byte stream, not complete HTTP request objects. One `Receive()` call may return a partial request, so the application must accumulate bytes until it has enough data to parse.

## Phase 2: Threads: Multiple Points of Execution

Status: in progress. Slice 4.1 is complete. Remaining slices belong to Milestone 4 and Milestone 5.

### Features

| Slice | Server behavior to build | OSTEP mapping | C#/.NET mechanism |
|-------|--------------------------|---------------|-------------------|
| Done | Prove single-thread blocking with `/slow` | Chapter 4.4: Process States | `Thread.Sleep(5000)` |
| Planned | Spawn one thread per accepted client | Chapter 26: Concurrency: An Introduction; Chapter 27: Thread API | `new Thread(() => HandleClient(socket)).Start()` |
| Planned | Log thread IDs and observe non-deterministic ordering | Chapter 26: Concurrency; Chapter 4.4: Process States | `Thread.CurrentThread.ManagedThreadId` |
| Planned | Show shared address space with `static` data and per-thread local variables | Chapter 13: The Abstraction: Address Spaces; Chapter 26: Concurrency | `static int`, local variables, method parameters |
| Planned | Create a race condition with unsafe shared counter updates | Chapter 26: data race example; Chapter 28: Locks | `counter++` under concurrent requests |
| Planned | Stress thread-per-connection limits | Chapter 27: Thread API | many client connections, memory observation |
| Planned | Fix shared counter with mutual exclusion | Chapter 28: Locks | `lock`, `Monitor`, `Interlocked` comparison |

### Concept Summary

A thread is a separate execution point with its own program counter and stack. Threads inside the same process share heap and static data, so one blocked handler no longer freezes the whole server, but shared mutable state becomes dangerous.

Race conditions happen when multiple threads interleave reads and writes to shared data without coordination. A lock provides mutual exclusion: only one thread can enter the critical section at a time.

## Phase 3: Bounded Concurrency

Status: planned. Maps to future Milestone 6.

### Features

| Slice | Server behavior to build | OSTEP mapping | C#/.NET mechanism |
|-------|--------------------------|---------------|-------------------|
| Planned | Replace unbounded thread creation with a fixed worker pool | Chapter 30: Condition Variables | `Thread[]`, worker loop, shared queue |
| Planned | Implement producer-consumer flow: accept thread enqueues, workers dequeue | Chapter 30: Condition Variables; Chapter 31: Semaphores | `Queue<Socket>`, `Monitor.Wait`, `Monitor.Pulse` |
| Planned | Add a bounded queue and backpressure | Chapter 30: Condition Variables | queue capacity, wait/reject behavior |
| Planned | Observe starvation and queue latency under slow requests | Chapter 26: Concurrency; Chapter 30: Condition Variables | slow path stress test, timing logs |
| Planned | Compare manual pool with .NET thread pool behavior | Chapter 30: Condition Variables | `ThreadPool.QueueUserWorkItem` |

### Concept Summary

A condition variable lets a thread sleep until a predicate becomes true. In a producer-consumer queue, producers wait when the queue is full, and consumers wait when the queue is empty.

A fixed-size worker pool protects memory and CPU from unbounded thread creation. The tradeoff is queueing: when workers are busy, new requests wait or get rejected.

## Phase 4: Event-Based Concurrency

Status: planned. Maps to future Milestone 7.

### Features

| Slice | Server behavior to build | OSTEP mapping | C#/.NET mechanism |
|-------|--------------------------|---------------|-------------------|
| Planned | Replace blocking socket calls with async socket operations | Chapter 33: Event-Based Concurrency; Chapter 36: I/O Devices | `Socket.AcceptAsync`, `ReceiveAsync`, `SendAsync` |
| Planned | Build a minimal event-loop style server | Chapter 33: Event-Based Concurrency | socket readiness loop, dispatch handlers |
| Planned | Compare manual event loop with C# `async`/`await` | Chapter 33: Event-Based Concurrency | `async`, `await`, compiler state machine |
| Planned | Stress many slow connections and compare memory against thread-per-connection | Chapter 33: Event-Based Concurrency; Chapter 36: I/O Devices | connection stress script, memory observation |

### Concept Summary

Event-based concurrency avoids one thread per connection. A loop asks the OS which sockets are ready, then handles only those sockets without blocking on the others.

Async I/O moves waiting into the OS and runtime instead of parking one user thread per client. This lets the server keep many connections open with fewer threads, but it makes control flow more indirect.

## OSTEP Chapter Map

| OSTEP area | Chapter | Project phase |
|------------|---------|---------------|
| CPU virtualization | Chapter 4: The Abstraction: The Process | Phase 1, Phase 2 |
| CPU virtualization | Chapter 6: Limited Direct Execution | Phase 1 |
| Memory virtualization | Chapter 13: The Abstraction: Address Spaces | Phase 2 |
| Concurrency | Chapter 26: Concurrency: An Introduction | Phase 2, Phase 3 |
| Concurrency | Chapter 27: Thread API | Phase 2 |
| Concurrency | Chapter 28: Locks | Phase 2 |
| Concurrency | Chapter 30: Condition Variables | Phase 3 |
| Concurrency | Chapter 31: Semaphores | Phase 3 |
| Concurrency | Chapter 33: Event-Based Concurrency | Phase 4 |
| Persistence and I/O | Chapter 36: I/O Devices | Phase 1, Phase 4 |
| Persistence | Chapter 39: Files and Directories | Phase 1 |

## Consequences

Good:

- The project now has a simple learning arc from single-threaded process behavior to event-based concurrency.
- Existing milestone documents remain valid.
- Each phase maps code behavior to specific OSTEP chapters.
- The roadmap keeps implementation grounded in observable server behavior instead of abstract reading.

Tradeoffs:

- Phase 1 gains one more slice before concurrency continues: robust request receiving.
- Phase 3 could be skipped for speed, but it teaches producer-consumer queues and condition variables, which are core OS concepts.
- Phase 4 should not begin until thread-per-connection and bounded worker-pool limits are observed; otherwise async looks like magic instead of a response to a measured problem.

## Next Steps

1. Create `docs/learning/slice-1.4-robust-request-receive.md`.
2. Implement slice 1.4 before continuing Phase 2.
3. Continue Milestone 4 slices 4.2-4.6.
4. Slice Milestone 5 before implementing the lock fixes.
5. Slice Milestone 6 before implementing the worker pool.
6. Slice Milestone 7 before implementing async sockets.
