# Milestone 29: Condition Variables (Ch. 30 §30.1-§30.3)
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
Why doesn't `while (!predicate) lock.Wait();` work on a plain `lock`/`Monitor` in C#? What does a condition variable actually do, and what does it cost when used wrong? What's the difference between Mesa and Hoare semantics, and why does every system use Mesa?
## Scope
A standalone **CV primitive** in `src/MiniWebServer.Host/MiniScheduler/ConditionVariable.cs` that mirrors POSIX `pthread_cond_wait` / `pthread_cond_signal` semantics on top of `Monitor.Wait` / `Monitor.Pulse`:
- `Wait(Lock)` — atomically releases the lock + parks the thread on the CV's wait queue. On wake, re-acquires the lock before returning.
- `Signal()` — wakes one waiter (FIFO order).
- `Broadcast()` — wakes all waiters.

A driver route `/cv/run?scenario=lost-wakeup-vs-cv&producers=N&consumers=M&items=K` demonstrates the **lost-wakeup bug** OSEP §30.1 introduces: producer sets `done = 1` and calls `signal`; consumer was past the check but not yet on the wait queue when the signal fired → consumer waits forever. The fix is the canonical `while (!predicate) { Wait(); }` idiom that uses the CV primitive. Also includes a producer/consumer bounded-buffer demo using `Wait(empty)` / `Wait(full)` CVs sharing one lock — the canonical Ch. 30 §30.2 example.

## Slice
- **[s1-condition-variables.md](./s1-condition-variables.md)** — CV primitive + lost-wakeup reproduction + bounded-buffer demo.

## OSTEP coverage
- **Ch. 30 §30.1** "Definition and Routines" [OS+AD14]: introduces CVs as a queue that threads put themselves on when a state is not as desired; some other thread, when it changes the state, signals one (or more) waiters. The canonical `Wait(Lock)` atomically releases the lock + parks; `Signal` wakes one.
- **Ch. 30 §30.1** "Mesa vs Hoare semantics" [LR80 vs H74]: Mesa (the C# `Monitor` default) signals as a *hint* — the waker keeps the lock until it leaves the critical section; the waiter doesn't immediately run. The waiter's lock-acquisition may race with another thread that runs first. Hoare semantics transfer the lock atomically. Almost every system uses Mesa; the canonical defensive idiom is `while (!predicate) Wait()` to re-check on every wakeup.
- **Ch. 30 §30.2** "The Producer/Consumer (Bounded Buffer) Problem" [D72]: the canonical worked example. Single-CV-with-`if` is broken; single-CV-with-`while` is mostly broken (wake-up-the-wrong-type bug); two CVs (one for empty, one for full) + `while` is the correct solution.
- **Ch. 30 §30.3** "Covering Conditions" [LR80]: when the waker doesn't know which waiter to wake (e.g., a memory allocator where different threads are waiting for different sizes), use `Broadcast()` to wake everyone; the defensive `while` loop on each waiter re-checks and re-sleeps the ones that aren't ready.
- **Cross-reference**: M6 `docs/learning/m6-bounded-worker-pool/overview.md` uses the same `Monitor.Wait` / `Monitor.Pulse` machinery directly. M29 extracts the pattern into a first-class primitive.

## Files
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariable.cs` — new file. `Wait(object lockObj)`, `Signal()`, `Broadcast()`. Builds on `Monitor` (BCL equivalent of a mutex + condvar fused in POSIX).
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariableDemos.cs` — driver code with the lost-wakeup bug + the CV-fixed variant + the §30.2 two-CV bounded buffer.
- `src/MiniWebServer.Host/Program.cs` — `/cv/run` route.

## Implementation deviations from OSEP
- **Mesa semantics** (the BCL default): `Signal` wakes a waiter, but the waker keeps the lock until it leaves the critical section. OSEP §30.1 mentions Hoare as an alternative. We use Mesa — it's what C# gives you for free.
- **Spurious wakeups**: the `Wait(Lock)` helper loops on `while (!predicate) { Monitor.Wait(_lock); }` so spurious wakeups are tolerated (POSIX requires this; C# `Monitor.Wait` can return spuriously).
- **No `WaitUntil(DateTime)` timeout overload**: POSIX provides `pthread_cond_timedwait`; deferred.

## What this slice does NOT do
- **Semaphores as a CV alternative** (OSEP §31.5): semaphores can solve the same problem; the slice uses CVs directly because the OSEP coverage is on CVs.
- **CV across processes**: out of scope for a single-process lab.
- **Priority donation / wait queue reordering** — Mesa gives us a FIFO wait queue by default; deferred.
- **Hoare semantics** — see deviations.

## Where this leads
- M30 deadlock prevention (Ch. 32 §32.3) builds on this CV primitive to demonstrate hold-and-wait avoidance via timed `Wait` + lock ordering.
- Future: replace M6's hand-rolled `Monitor.Wait` / `Monitor.Pulse` with the new `ConditionVariable` helper (one-line refactor; no behavior change).
