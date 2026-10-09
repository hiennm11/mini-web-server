# Milestone 29: Condition Variables (Ch. 30 §30.1-§30.3)
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
Why doesn't `while (!predicate) lock.Wait();` work on a plain `lock`/`Monitor` in C#? What does a condition variable actually do, and what does it cost when used wrong? What's the difference between Mesa and Hoare semantics, and why does every system use Mesa?
## Scope

A standalone **CV primitive** in `src/MiniWebServer.Host/MiniScheduler/ConditionVariable.cs` with its **own wait queue**, separate from the lock:
- `Wait(lockObj)` — atomically releases the lock and parks the thread on this CV's queue; re-acquires the lock before returning. Throws if the caller does not hold the lock.
- `Signal()` — releases the longest-waiting thread.
- `Broadcast()` — releases every thread parked here.
- `WaitWhile(lockObj, predicate)` — the canonical `while` idiom, with `predicate` meaning "still blocked" (same shape as `SpinWait.SpinWhile` and OSEP's `while (count == 0) pthread_cond_wait(...)`).

**Why not a `Monitor` wrapper**: `Monitor` fuses mutex and condition variable into one object, so two CVs over the same lock share a single wait queue. That makes §30.2's fix (`empty` for producers, `fill` for consumers) inexpressible — a consumer's signal could still wake a consumer, which is the bug the section exists to teach. POSIX keeps `pthread_cond_t` separate from `pthread_mutex_t` for exactly this reason. See ADR 0019.

The driver route `/cv/run?scenario=lost-wakeup|single-cv|two-cv|covering-condition` runs each case beside its fixed counterpart. Broken cases hang on purpose, so every scenario runs under a bounded join and reports the timeout as data rather than wedging the server.

## Slice
- **[s1-condition-variables.md](./s1-condition-variables.md)** — CV primitive + lost-wakeup reproduction + bounded-buffer demo.

## OSTEP coverage
- **Ch. 30 §30.1** "Definition and Routines" [OS+AD14]: introduces CVs as a queue that threads put themselves on when a state is not as desired; some other thread, when it changes the state, signals one (or more) waiters. The canonical `Wait(Lock)` atomically releases the lock + parks; `Signal` wakes one.
- **Ch. 30 §30.2** (the Mesa/Hoare discussion that follows Figure 30.9) [LR80 vs H74]: Mesa (the C# `Monitor` default) signals as a *hint* — the waker keeps the lock until it leaves the critical section; the waiter doesn't immediately run. The waiter's lock-acquisition may race with another thread that runs first. Hoare semantics transfer the lock atomically. Almost every system uses Mesa; the canonical defensive idiom is `while (!predicate) Wait()` to re-check on every wakeup.
- **Ch. 30 §30.2** "The Producer/Consumer (Bounded Buffer) Problem" [D72]: the canonical worked example. Single-CV-with-`if` is broken; single-CV-with-`while` is mostly broken (wake-up-the-wrong-type bug); two CVs (one for empty, one for full) + `while` is the correct solution.
- **Ch. 30 §30.3** "Covering Conditions" [LR80]: when the waker doesn't know which waiter to wake (e.g., a memory allocator where different threads are waiting for different sizes), use `Broadcast()` to wake everyone; the defensive `while` loop on each waiter re-checks and re-sleeps the ones that aren't ready.
- **Cross-reference**: M6 `docs/learning/m6-bounded-worker-pool/overview.md` uses `Monitor.Wait` / `Monitor.Pulse` directly, which stays correct because it needs only one condition. M29 makes the multi-condition case a first-class primitive.

## Files
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariable.cs` — the primitive. `Wait` / `Signal` / `Broadcast` / `WaitWhile`, plus counters (`WaitingCount`, `WaitCount`, `SignalCount`, `BroadcastWakeCount`) the demos report.
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariableDemos.cs` — the four scenarios.
- `src/MiniWebServer.Host/Program.cs` — `/cv/run` route.
- `docs/adr/0019-m29-condition-variables.md` — the `Monitor`-wrapper rejection.

## Implementation deviations from OSEP
- **Mesa semantics** (the BCL default): `Signal` releases a waiter, but the waker keeps the lock until it leaves the critical section. §30.2 notes Hoare as an alternative. We use Mesa — "Virtually every system ever built employs Mesa semantics" — §30.2, just after Figure 30.9.
- **FIFO wake order is ours**: §30.1 promises no ordering. FIFO makes the §30.2 bug reproduce deterministically instead of rarely.
- **`WaitWhile` takes the "still blocked" predicate**, so the mandatory `while` stays visible in call sites rather than hidden inside an API.
- **`Wait` requires single (non-recursive) ownership**: the BCL exposes no recursion depth, so a re-entrant `lock` around `Wait` is documented as invalid rather than detected. One `Monitor.Exit` is issued.
- **Interruption restores both invariants**: node removal and lock reacquisition are retried to completion, then the original exception is rethrown.
- **Timed wait**: `Wait(object, TimeSpan)` returns a bool for timeout, as POSIX `pthread_cond_timedwait` does.

## What this slice does NOT do
- **Semaphores as a CV alternative** (OSEP §31.5): semaphores can solve the same problem; the slice uses CVs directly because the OSTEP coverage is on CVs.
- **CV across processes**: out of scope for a single-process lab.
- **Priority donation / wait queue reordering**: our FIFO is a simulation convenience, not a scheduler promise.
- **Hoare semantics** — see deviations.
- **Timed wait** (`pthread_cond_timedwait`) — **not deferred.** This slice ships `Wait(object, TimeSpan)`; M30 (ADR 0020) uses it for hold-and-wait avoidance rather than adding it.

## Where this leads
- M30 deadlock prevention (Ch. 32 §32.3) builds on this primitive to demonstrate hold-and-wait avoidance via a timed `Wait` + lock ordering. That slice consumes the timed-wait overload this milestone already ships; it does not add it.
