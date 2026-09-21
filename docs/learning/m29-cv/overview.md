# Milestone 29: Condition Variables (Ch. 32.2)
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
Why doesn't `while (!predicate) lock.Wait();` work on a plain `lock`/`Monitor` in C#? What does a condition variable actually do, and what does it cost when used wrong?
## Scope
A small standalone **CV primitive** in `src/MiniWebServer.Host/MiniScheduler/ConditionVariable.cs` that mirrors POSIX `pthread_cond_wait` / `pthread_cond_signal` semantics on top of `Monitor.Wait` / `Monitor.Pulse`:
- `Wait(Lock)` — atomically releases the lock + parks the thread on the CV's wait queue. On wake, re-acquires the lock before returning.
- `Signal()` — wakes one waiter (FIFO order).
- `Broadcast()` — wakes all waiters.
A driver route `/cv/run?scenario=lost-wakeup-vs-cv&producers=N&consumers=M&items=K` demonstrates the **lost-wakeup bug** the CV solves (producer thread sets `flag = true` and calls `Pulse` while consumer is between the predicate check and the wait; without CV, the consumer waits forever). Also includes a producer-consumer bounded-buffer demo using `Wait(empty)` / `Wait(full)` CVs sharing one lock.
## Slice
- **[s1-condition-variables.md](./s1-condition-variables.md)** — CV primitive + lost-wakeup reproduction + bounded-buffer demo.
## OSTEP coverage
- **Ch. 32 §32.2** "The Bounded Buffer, Condition Variables, and the Lost Wakeup" [OS+AD14]: the canonical example OSEP uses to introduce CVs. Includes the Mesa vs Hoare semantics distinction.
- **Ch. 30 §30.2** producer-consumer with CVs (cross-reference): M6 bounded queue uses raw `Monitor.Wait` / `Monitor.Pulse` without a CV primitive; this slice makes that pattern a first-class helper.
- **Cross-reference**: M6 `docs/learning/m6-bounded-worker-pool/overview.md` uses the same `Monitor.Wait` / `Monitor.Pulse` machinery directly. M29 extracts the pattern.
## Files
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariable.cs` — new file. `Wait(Lock)`, `Signal()`, `Broadcast()`. Builds on `Monitor` (BCL equivalent of a mutex + condvar fused in POSIX).
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariableDemos.cs` — driver code with the lost-wakeup bug + the CV-fixed variant.
- `src/MiniWebServer.Host/Program.cs` — `/cv/run` route.
## Implementation deviations from OSEP
- **Mesa semantics** (the BCL default): `Signal` wakes a waiter, but the waiter doesn't immediately hold the lock; the waker keeps the lock until it leaves the critical section. OSEP §32.2 mentions Hoare semantics as an alternative. We use Mesa — it's what C# gives you for free.
- **Spurious wakeups**: the `Wait(Lock)` helper loops on `while (!predicate) { Monitor.Wait(_lock); }` so spurious wakeups are tolerated (POSIX requires this; .NET's `Monitor.Wait` also returns spuriously).
- **No timeout overload**: POSIX provides `pthread_cond_timedwait`; deferred.
## What this slice does NOT do
- **Semaphores as a CV alternative** (OSEP §31.5): semaphores can solve the same problem; the slice uses CVs directly because the OSEP coverage is on CVs.
- **CV across processes**: out of scope for a single-process lab.
- **Timed wait / `WaitUntil(DateTime)`**: deferred.
- **Signal-then-wake-up ordering** beyond Mesa semantics: no priority donation, no wait queue reordering.
## Where this leads
- M30 deadlock prevention (Ch. 32.3) builds on this CV primitive to demonstrate hold-and-wait avoidance and lock-ordering protocols.
- Future: replace M6's hand-rolled `Monitor.Wait` / `Monitor.Pulse` with the new `ConditionVariable` helper (one-line refactor; no behavior change).
