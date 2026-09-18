# Milestone 5: Race Lab — Fix with `lock`

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How do you fix the data race exposed by M4.5?

## Scope

Add a `/race-safe` route that wraps the same 1M-iteration counter increment in `lock (obj) { ... }`. Run four concurrent `/race-safe` requests and verify the readings are exactly 1M apart (no lost updates).

## Slice

- **[s1-race-lab.md](./s1-race-lab.md)** — add `RequestStats.SafeCounterLock` and `/race-safe` route; observe readings 1M / 2M / 3M / 4M.

## OSEP concept

- **Ch. 28 Locks** — `lock` provides mutual exclusion. Inside the critical section, only one thread can be active, so the read-modify-write triple from §26.4 cannot interleave. The scheduler still preempts inside the critical section, but the next thread to acquire the lock waits at `Monitor.Enter` until the previous thread calls `Monitor.Exit`.
- **Ch. 28 §28.1-2** — a lock is a variable + `lock`/`unlock` semantics. `lock()` blocks the caller if another thread already holds the lock; `unlock()` releases it.
- **Ch. 28 §28.7-9** — at the hardware level, the OS uses atomic primitives (test-and-set, compare-and-swap) to build the lock.
- **Ch. 28 §28.12-14** — the kernel parks losing threads on a wait queue so they don't spin.

## .NET mechanism

- `lock (obj) { ... }` is syntactic sugar for `Monitor.Enter(obj)` ... `Monitor.Exit(obj)`. The monitor uses a hardware compare-and-swap or similar atomic primitive inside the kernel, then parks waiting threads on a kernel wait queue (Linux `futex` / Windows `KEYED_EVENT`).
- `RequestStats.SafeCounterLock` is a `static readonly object` so its identity is stable for the lifetime of the process.
- `Interlocked.Increment` would be the cheaper primitive for a single shared integer, but `lock` is needed here because the test reads the counter after the loop ends and we want the read also to be serialized.

## Files

- `src/MiniWebServer.Host/Program.cs` — `/race-safe` route under `lock (RequestStats.SafeCounterLock)`.

## What this slice does NOT do

- Doesn't replace the lock with `Interlocked.Increment` (cheaper for single-counter increments).
- Doesn't add finer-grained locking (multiple locks for independent state).
- Doesn't introduce condition variables / semaphores (those come in M8 / M9).

The lock serializes every operation on `SafeCounter`. If two different code paths update independent state under the same lock, they wait for each other unnecessarily. Finer-grained locks help — but at the cost of more reasoning about correctness.
