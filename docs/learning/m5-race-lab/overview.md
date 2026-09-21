# Milestone 5: Race Lab — Fix with `lock`

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How do you fix the data race exposed by M4.5?

## Scope

Add a `/race-safe` route that wraps the same 1M-iteration counter increment in `lock (obj) { ... }`. Run four concurrent `/race-safe` requests and verify the readings are exactly 1M apart (no lost updates).

## Slice

- **[s1-race-lab.md](./s1-race-lab.md)** — add `RequestStats.SafeCounterLock` and `/race-safe` route; observe readings 1M / 2M / 3M / 4M.

## OSTEP coverage

- **Ch. 28 Locks** — the entire chapter.
  - §28.1 Locks: The Basic Idea — lock = variable + `lock()` / `unlock()` semantics.
  - §28.2 Pthread Locks — `pthread_mutex_lock` / `pthread_mutex_unlock` (the C API our `lock` is sugar for).
  - §28.4 Evaluating Locks — correctness (mutual exclusion), fairness, performance.
  - §28.5 Controlling Interrupts — why this is the wrong approach (multiprocessors, trust issues).
  - §28.7 Building Working Spin Locks with Test-And-Set — the hardware primitive test-and-set, and the simple spin lock built on it (Figure 28.3).
  - §28.9 Compare-And-Swap — x86's compare-and-exchange; .NET's `Monitor` uses this internally.
  - §28.11 Fetch-And-Add — ticket lock built on fetch-and-add (Figure 28.7).
  - §28.16 Two-Phase Locks — Linux's hybrid spin-then-futex approach.

## OSEP §-specific deviations

- OSEP §28.1 defines a lock as a variable + `lock`/`unlock` semantics. Our `lock (obj)` in C# is the `Monitor.Enter`/`Monitor.Exit` equivalent.
- OSEP §28.7-§28.9 discuss the hardware primitives. .NET's `Monitor` uses CAS on x86 internally. We don't see this directly.
- OSEP §28.14 covers queue-based locks (Solaris park/unpark) — .NET uses similar OS-level primitives under the hood.
- OSEP §28.16 covers Linux's futex-based two-phase lock. .NET's `Monitor` is essentially this on Linux.

## Key OSEP quotes

> "A lock is just a variable, plus lock and unlock semantics." (OSEP §28.1)

> "Calling the routine lock() tries to acquire the lock; if no other thread holds the lock (i.e., it is free), the thread will acquire the lock and enter the critical section." (OSEP §28.1)

## .NET mechanism

- `lock (obj) { ... }` is syntactic sugar for `Monitor.Enter(obj)` ... `Monitor.Exit(obj)`. The JIT compiles to a thin wrapper around CAS on x86.
- On contention, the losing thread parks on the monitor's wait queue (Linux `futex` / Windows `KEYED_EVENT`).
- `RequestStats.SafeCounterLock` is a `static readonly object` so its identity is stable for the lifetime of the process.

## Alternative: `Interlocked.Increment`

OSEP mentions atomic primitives briefly (test-and-set, compare-and-swap). For a single shared integer, .NET's `Interlocked.Increment` would be cheaper than `lock` because it does a single atomic add without acquiring a lock. We chose `lock` because the test reads the counter after the loop and we want the read also to be serialized under the lock.

## Files

- `src/MiniWebServer.Host/Program.cs` — `/race-safe` route under `lock (RequestStats.SafeCounterLock)`.

## What this slice does NOT do

- Doesn't replace the lock with `Interlocked.Increment` (cheaper for single-counter increments).
- Doesn't add finer-grained locking (multiple locks for independent state).
- Doesn't introduce condition variables / semaphores (those come in M6 / M8).

The lock serializes every operation on `SafeCounter`. If two different code paths update independent state under the same lock, they wait for each other unnecessarily. Finer-grained locks help — but at the cost of more reasoning about correctness.
