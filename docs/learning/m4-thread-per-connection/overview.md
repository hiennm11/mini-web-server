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

1. **[s1-single-thread-blocking.md](./s1-single-thread-blocking.md)** — add a `/slow` route that `Thread.Sleep`s; observe the server can't accept new connections during the sleep.
2. **[s2-thread-per-connection.md](./s2-thread-per-connection.md)** — `new Thread(...).Start()` per client; observe that the accept loop returns to `Accept()` immediately.
3. **[s3-scheduling-non-determinism.md](./s3-scheduling-non-determinism.md)** — record completion order across multiple clients; observe the OS scheduler picks a non-deterministic order.
4. **[s4-shared-address-space.md](./s4-shared-address-space.md)** — add a shared `RequestStats.TotalRequests` counter; observe all threads see and update the same memory.
5. **[s5-race-condition-prep.md](./s5-race-condition-prep.md)** — add a `/race` route that increments non-atomically; observe the lost-update bug.
6. **[s6-thread-per-connection-limits.md](./s6-thread-per-connection-limits.md)** — spawn 150 parked clients; observe the memory blow-up from 150 OS thread stacks.

## OSTEP coverage

This is the canonical concurrency chapter sweep — almost all of Part II.

- **Ch. 4** — Process states (§4.4): Running / Ready / Blocked. A server thread blocked in `Accept()` is in Blocked state; the scheduler runs other Ready work until the SYN arrives.
- **Ch. 26 Concurrency: An Introduction** (§26.2 thread creation, §26.3 shared data, §26.4 the race / lost-update problem — Figure 26.7 is the canonical trace, §26.5 wish for atomicity, §26.7 why in OS class).
- **Ch. 27 Thread API** (§27 covers `pthread_create`, `pthread_join`, thread-local storage, mutex locks). Our use of `new Thread(...)` is the .NET equivalent.
- **Ch. 4.5 Process Creation** — Process creation loads code + static data into memory; the runtime allocates stack + heap. We observe the stack cost directly in slice 6.

## OSEP §-specific deviations

- OSEP §26.2 uses `pthread_create` + `pthread_join`. We use `new Thread(...).Start()` + `Thread.Join`. The .NET runtime wraps `pthread_create` under the hood.
- OSEP §26.4 figure 26.7 shows a 3-instruction race (`mov / add / mov`) with a context switch between thread 1's `add` and `mov`. Our `RequestStats.UnsafeCounter++` is the same race at the C# language level.
- OSEP §26.5 wish for atomicity — we observe the bug in slice 5 and fix it in M5.

## Key OSEP quotes

> "A thread is very much like a separate process, except for one difference: they share the same address space and thus can access the same data." (OSEP §26.1)

> "When in doubt, think maliciously!" — OSEP's advice for analyzing concurrent programs (§26.4). Imagine a scheduler that interrupts at the worst possible moment.

## .NET mechanism

- `new Thread(StartPoint)` — creates a managed thread that maps to an OS thread (default 1 MB stack).
- `Thread.Sleep(int)` — blocks the current managed thread; OS marks it Blocked.
- `RequestStats` — static class with static fields shared across all threads in the process.

## Files

- `src/MiniWebServer.Host/Program.cs` — accept loop branches on `--async` flag; `HandleClient` contains the per-connection logic.
- `src/MiniWebServer.Host/RequestStats.cs` — `TotalRequests`, `UnsafeCounter`, `SafeCounter` shared state.

## Where this leads

- M5 fixes the race with `lock` (slice 5's negative control motivates the fix).
- M6 replaces unbounded thread creation with a bounded worker pool.
- M7 moves from thread-per-connection to async/event-based (one OS thread, many Tasks).
