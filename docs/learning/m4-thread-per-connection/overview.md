# Milestone 4: Thread-Per-Connection

> **Overview** — what this milestone covers and where to start. Slices live in this folder.

## Question

What happens when a server thread blocks? How do threads change that, and what new problems appear?

## Scope

Add concurrency to the server by spawning one OS thread per client. Observe:
- how threads let multiple clients be in flight at once
- how the scheduler picks between ready threads
- how threads share the heap but have private stacks
- how non-atomic increments on a shared counter corrupt the count
- how thread stacks consume memory under load

The race fix is deferred to M5 so the problem is observable before the solution.

## Slices (in order)

1. **[s1-single-thread-blocking.md](./s1-single-thread-blocking.md)** — add a `/slow` route that `Thread.Sleep`s for several seconds; observe that the server can't accept new connections during the sleep.
2. **[s2-thread-per-connection.md](./s2-thread-per-connection.md)** — `new Thread(...).Start()` per client; observe that the accept loop returns to `Accept()` immediately while the handler sleeps.
3. **[s3-scheduling-non-determinism.md](./s3-scheduling-non-determinism.md)** — record the order of completion across multiple clients; observe that the OS scheduler picks an order that is not deterministic from the application.
4. **[s4-shared-address-space.md](./s4-shared-address-space.md)** — add a `RequestStats.TotalRequests` counter; observe that all threads see and update the same memory.
5. **[s5-race-condition-prep.md](./s5-race-condition-prep.md)** — add a `/race` route that increments a shared counter non-atomically; observe the lost-update bug.
6. **[s6-thread-per-connection-limits.md](./s6-thread-per-connection-limits.md)** — spawn 150 parked clients; observe the memory blow-up from 150 OS thread stacks.

## OSEP concept

- **Ch. 4 / §4.4** Process States — Running / Ready / Blocked (slice 1).
- **Ch. 26** Concurrency and Threads — multiple points of execution (slice 2).
- **Ch. 7 / Ch. 8 (informally)** — non-deterministic scheduling (slice 3).
- **Ch. 26 §26.2** — shared address space; heap shared, stacks private (slice 4).
- **Ch. 26 §26.4 figure 26.7** — the canonical race example (slice 5).
- **Ch. 26 / §27** — thread stack overhead, OS thread limits (slice 6).

## Files

- `src/MiniWebServer.Host/Program.cs` — accept loop branches based on `--async` flag; `HandleClient` contains all the per-connection logic.
- `src/MiniWebServer.Host/RequestStats.cs` — `TotalRequests`, `UnsafeCounter`, `SafeCounter` shared state.

## Where this leads

- M5 fixes the race with `lock` (slice 5's negative control motivates the fix).
- M6 replaces unbounded thread creation with a bounded worker pool.
- M7 moves from thread-per-connection to async/event-based (one OS thread, many Tasks).
