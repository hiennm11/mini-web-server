# ADR 0003: Four-Phase OSTEP Learning Roadmap

## Status

Accepted (roadmap closure note appended 2026-09-17)

## Date

2026-06-08

## Context

ADR 0001 defines the build-first OSTEP learning direction and the current milestone roadmap. ADR 0002 proves the single-thread blocking baseline before adding threads.

This ADR originally planned the roadmap with milestones 1-3 complete and 4-7 planned. As of 2026-09-17, all four phases are complete: milestones M1-M7 are built and documented, and the repo is a complete OS concepts lab for the core three pieces of OSTEP. The Status column below reflects current reality (every row says Done).

The project keeps its low-level learning shape:

- use `System.Net.Sockets.Socket` directly;
- avoid Kestrel, ASP.NET Core, `HttpListener`, and HTTP server libraries;
- make OS behavior observable through code, commands, and console output;
- decompose each phase into lesson slices before implementation.

## Decision

We organize the Mini Web Server learning path into four OSTEP-aligned phases:

1. **The Process & The Byte Stream** — raw socket lifecycle, HTTP parsing, static file serving, robust request receiving.
2. **Threads: Multiple Points of Execution** — thread-per-connection, scheduler observation, shared address space, races, locks, thread limits.
3. **Bounded Concurrency** — fixed worker pool, producer-consumer queue, condition variables, backpressure (queue bounded; backpressure itself still future).
4. **Event-Based Concurrency** — async sockets, event loop, non-blocking I/O, many connections with few threads.

This ADR does not replace the existing milestone documents. It groups them into a simpler learning arc and adds one missing Phase 1 slice: robust multi-call request receiving.

Each phase is implemented through lesson slices following `docs/learning/README.md`.

## Phase 1: The Process & The Byte Stream

Status: complete (4/4 features done).

### Features

| Slice | Server behavior to build | OSEP mapping | C#/.NET mechanism | Status |
|-------|--------------------------|---------------|-------------------|--------|
| 1.1 | Raw socket lifecycle: `Bind` -> `Listen` -> `Accept` -> `Receive` -> `Send` -> `Close` | Chapter 4: The Abstraction: The Process; Chapter 6: Limited Direct Execution | `Socket`, `Bind`, `Listen`, `Accept`, `Receive`, `Send` | Done |
| 1.2 | Parse HTTP request text into method, path, version, and headers | Chapter 4: Process state and address space; Chapter 36: I/O Devices | `Socket.Receive`, `byte[]`, `Encoding.UTF8`, `HttpRequestParser` | Done |
| 1.3 | Serve static files from `wwwroot` and reject unsafe paths | Chapter 39: Files and Directories | `Path.GetFullPath`, `File.Exists`, `File.ReadAllBytes` | Done |
| 1.4 | Robust receive: loop `Receive()` until the full request headers and expected body are available | Chapter 4.4: Process States; Chapter 36: I/O Devices | receive loop, buffer accumulation, header delimiter detection, `Content-Length` handling | Done |

### Concept Summary

A process is a running program with its own address space and execution state. When the only server thread blocks in `Accept()` or `Receive()`, the OS can run other work, but this server cannot accept or handle another client until that thread runs again.

TCP gives the server an ordered byte stream, not complete HTTP request objects. One `Receive()` call may return a partial request, so the application must accumulate bytes until it has enough data to parse.

## Phase 2: Threads: Multiple Points of Execution

| Status: complete (7/7 features done; slices 4.1-4.6 + m5 race lab).

### Features

| Slice | Server behavior to build | OSTEP mapping | C#/.NET mechanism | Status |
|-------|--------------------------|---------------|-------------------|--------|
| 4.1 | Prove single-thread blocking with `/slow` (now `Thread.Sleep(30000)`) | Chapter 4.4: Process States | `Thread.Sleep` | Done |
| 4.2 | Spawn one thread per accepted client | Chapter 26: Concurrency: An Introduction; Chapter 27: Thread API | `new Thread(() => HandleClient(socket)).Start()` | Done |
| 4.3 | Log thread IDs and observe non-deterministic ordering | Chapter 26: Concurrency; Chapter 4.4: Process States | `Thread.CurrentThread.ManagedThreadId` | Done |
| 4.4 | Show shared address space with `static` data and per-thread local variables | Chapter 13: The Abstraction: Address Spaces; Chapter 26: Concurrency | `Interlocked.Increment` on `static int` + per-thread local | Done |
| 4.5 | Create a race condition with unsafe shared counter updates | Chapter 26: data race example; Chapter 28: Locks | bare `counter++` under concurrent requests; smoke observed ~20% lost increments | Done |
| 4.6 | Stress thread-per-connection limits | Chapter 27: Thread API | many client connections + `Process.Threads.Count` + `PrivateMemorySize64` | Done |
| M5 | Fix shared counter with mutual exclusion | Chapter 28: Locks | `lock` (= `Monitor.Enter`/`Exit`); `/race-safe` produces deterministic totals | Done |

### Concept Summary

A thread is a separate execution point with its own program counter and stack. Threads inside the same process share heap and static data, so one blocked handler no longer freezes the whole server, but shared mutable state becomes dangerous.

Race conditions happen when multiple threads interleave reads and writes to shared data without coordination. A lock provides mutual exclusion: only one thread can enter the critical section at a time.

## Phase 3: Bounded Concurrency

Status: complete for the worker-pool step; bounded-queue + backpressure is a future slice.

### Features

| Slice | Server behavior to build | OSTEP mapping | C#/.NET mechanism | Status |
|-------|--------------------------|---------------|-------------------|--------|
| M6.1 | Replace unbounded thread creation with a fixed worker pool (8 threads) | Chapter 30: Condition Variables | `Thread[]`, worker loop | Done |
| M6.2 | Implement producer-consumer flow: accept thread enqueues, workers dequeue | Chapter 30: Condition Variables; Chapter 31: Semaphores | `Queue<Socket>`, `Monitor.Wait`, `Monitor.Pulse` | Done |
| M6.3 | Add a bounded queue and backpressure | Chapter 30: Condition Variables | queue capacity, wait/reject behavior | Future |
| M6.4 | Observe starvation and queue latency under slow requests | Chapter 26: Concurrency; Chapter 30: Condition Variables | slow path stress test, timing logs | Done (M6 smoke covered this) |
| M6.5 | Compare manual pool with .NET thread pool behavior | Chapter 30: Condition Variables | `ThreadPool` is used implicitly by async path | Future |

### Concept Summary

A condition variable lets a thread sleep until a predicate becomes true. In a producer-consumer queue, producers wait when the queue is full, and consumers wait when the queue is empty.

A fixed-size worker pool protects memory and CPU from unbounded thread creation. The tradeoff is queueing: when workers are busy, new requests wait or get rejected.

## Phase 4: Event-Based Concurrency

Status: complete for the event-loop step; remaining refinements (cancellation, ThreadPool cap, buffer pooling) are future slices.

### Features

| Slice | Server behavior to build | OSTEP mapping | C#/.NET mechanism | Status |
|-------|--------------------------|---------------|-------------------|--------|
| M7.1 | Replace blocking socket calls with async socket operations | Chapter 33: Event-Based Concurrency; Chapter 36: I/O Devices | `Socket.AcceptAsync`, `ReceiveAsync`, `SendAsync` | Done |
| M7.2 | Build a minimal event-loop style server | Chapter 33: Event-Based Concurrency | single-threaded `AcceptAsync` loop | Done |
| M7.3 | Compare manual event loop with C# `async`/`await` | Chapter 33: Event-Based Concurrency | `async`, `await`, compiler state machine | Done |
| M7.4 | Stress many slow connections and compare memory against thread-per-connection | Chapter 33: Event-Based Concurrency; Chapter 36: I/O Devices | 150 parked slow clients, ~20 threads vs 150+ in 4.6 | Done |

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

This chapter map covers the threads the rest of the roadmap in OSEP has been built around. Chapters not yet implemented (e.g., scheduling in Ch. 7-10, paging in Ch. 14-23, full FS in Ch. 36-45, security in Ch. 53-57) are documented in `CONTEXT.md` "OSEP Coverage" and remain as future slices.

## Consequences

Good:

- The project has a complete learning arc from single-threaded process behavior to event-based concurrency.
- All milestone documents and slice notes are valid.
- Each phase maps code behavior to specific OSTEP chapters.
- The roadmap keeps implementation grounded in observable server behavior instead of abstract reading.
- The repo now serves as an OS concepts lab for the core three pieces of OSTEP (Virtualization + Concurrency + Persistence).

Tradeoffs:

- Phase 1's robust receive slice was added mid-roadmap; the change is captured in the slice file itself.
- Phase 3's bounded queue + backpressure was deferred (future slice); the current worker pool accepts bursts without limit on the queue side.
- Phase 4's continuation-style control flow makes single-step debugging harder; the lesson is the trade-off, not a regression.

## Next Steps

The roadmap is complete. Future slices (none of which are required to follow the roadmap) include:

- Bounded queue + backpressure in `WorkerPool` (return `503 Service Unavailable` on overflow, or block accept).
- Reader-writer lock (`Ch. 30`) demo on a shared cache.
- MLFQ / lottery scheduler in user space (`Ch. 8-10`).
- Paging + multi-level page table → VM (`Ch. 14-23`).
- Real `open`/`read`/`write`/`close` syscall demo on top of `File.ReadAllBytes` (`Ch. 39`).
- Inode + bitmap + journaling mini file system (`Ch. 40-45`).
- Array-pooled receive buffers to lower memory in both modes.

Each future slice should follow `docs/learning/README.md`: small build-first exercise, observable experiment, learning note.
