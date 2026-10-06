# ADR 0019: M29 Condition Variables (OSEP Ch. 30 §30.1-§30.3)

## Status

Accepted

## Date

2026-10-06

## Context

M6 already reaches for `Monitor.Wait` / `Monitor.Pulse` in `WorkerPool`, and M20 (dining philosophers) uses semaphores. Neither makes a **condition variable** a first-class concept, so the OSEP Ch. 30 material is missing from the repo:

- **§30.1** the CV as an explicit queue, `wait()`'s atomic release-and-park, Mesa vs Hoare semantics, and the state-variable rule that makes the join example correct;
- **§30.2** the producer/consumer problem, and why *one* CV is broken even with a correct `while` loop;
- **§30.3** covering conditions, where `broadcast` is the right answer.

The milestone spec (`docs/learning/m29-cv/overview.md`) proposed building the CV "on top of `Monitor.Wait` / `Monitor.Pulse`". Taking that literally would have quietly destroyed the §30.2 lesson, which is the reason the milestone exists.

### The problem with the proposed design

`Monitor` fuses mutex and condition variable into a single object, and its wait queue is keyed by that object. Two `ConditionVariable` instances wrapping the same lock would therefore share **one** queue. Then §30.2's fix — a producer-only `empty` and a consumer-only `fill` — is inexpressible: `Pulse(fill).Pulse(empty)` are the same `Pulse(lock)`, so a consumer's signal can still wake a consumer. The demo would have "passed" by never exercising the bug it was written to teach.

POSIX draws the line explicitly for this reason: `pthread_cond_t *c` and `pthread_mutex_t *m` are separate objects, which is why `pthread_cond_wait` takes both.

## Decision

Implement a real condition variable in `src/MiniWebServer.Host/MiniScheduler/ConditionVariable.cs`.

### 1. Its own FIFO wait queue

Each `ConditionVariable` owns a private `LinkedList<Waiter>`. The associated lock is supplied per call (`Wait(lockObj)`), never captured at construction, so one CV can be paired with whichever lock the caller uses — matching `pthread_cond_wait(c, m)`.

`Signal` releases the head; `Broadcast` releases everyone. This is what makes `empty` and `fill` genuinely separable.

### 2. Reject `Wait` without the lock

`Wait` throws `SynchronizationLockException` when the caller does not hold `lockObj` (`Monitor.IsEntered`). Waiting without the lock is the one mistake that silently reintroduces the exact race a CV exists to remove, so it fails loudly rather than hanging.

### 3. `WaitWhile` takes the "still blocked" predicate

`WaitWhile(lockObj, predicate)` loops `while (predicate()) Wait(lockObj)`. The predicate is the reason to **keep waiting**, matching `SpinWait.SpinWhile` and the shape OSEP writes (`while (count == 0) pthread_cond_wait(...)`, §30.2 figure 30.10). This is deliberately *not* a `WaitUntil`-style "wait until true" API: the §30.2 TIP's point is that `while` is mandatory, and an API that hides the loop hides the lesson.

### 4. A per-waiter handoff, not a shared monitor wait

Each waiter gets its own `ManualResetEventSlim` (spin count 0 — a CV exists to stop spinning). Releasing is therefore lock-free with respect to the caller's lock, and `_queueLock` is a leaf lock never held while acquiring the caller's lock. An earlier draft held `_queueLock` across the handoff and also tried to `Monitor.TryEnter` the caller's lock for validation; both are deadlock hazards and were removed.

### 5. Staged, deterministic demos, with teardown

The broken cases run on a bounded join and report the timeout as **data**, since hanging is the point. `single-cv` stages the figure-30.11 interleaving explicitly (consumers park before the producer starts, single-slot buffer, producer holds the gate across its whole loop) instead of relying on a race to go wrong — an unstaged version usually limps along to completion and teaches nothing.

Because `/cv/run` is reachable on every request, a timed join is not enough: it ends the caller's wait, not the worker's. Every scenario therefore sets a shutdown predicate under its own gate and broadcasts once the observation is recorded, then joins every thread it started. A parked thread per request would leak a native thread and its stack for the process lifetime.

### 6. `Wait` tolerates interruption without losing invariants

A thread interrupted during `Wait` still completes node removal and lock reacquisition, then has the original exception rethrown. Returning without ownership would make the caller's own `lock` throw from its `Exit`, masking the real cause; leaving the node queued would let a later `Signal` release a thread that had already left. Waiter events are never disposed, since a signaler that dequeued the waiter may still be inside `Set()`.

## Consequences

### Positive

- **The §30.2 lesson is real.** `two-cv` provably cannot wake a producer with a consumer's signal, and a test asserts exactly that isolation.
- **The isolation test is mutation-checked.** Sharing one wait queue across instances — the behaviour `Monitor.Pulse` gives — was applied as a mutation; the test fails under it and passes on the real implementation.
- **Faithful to POSIX's separation**, so the mental model transfers to real code.
- **`/cv/run` never wedges the server and never leaks threads** — every scenario returns and releases what it started.

### Negative

- **Recursive lock ownership is documented, not detected.** `lock(gate) { lock(gate) { Wait(gate); } }` passes the ownership check, and one `Monitor.Exit` leaves the monitor owned at depth 1, so the waiter would park still holding it. The BCL exposes no recursion depth, so the single-exit form is documented as the required discipline instead.
- **`WorkerPool` still hand-rolls `Monitor.Wait`/`Monitor.Pulse`.** It does not need two CVs, so it is correct as-is; migrating it would be churn without a lesson. M6's pattern stays as the "one condition, one monitor" case.
- **FIFO ordering is our choice, not OSTEP's.** §30.1 promises nothing about wake order. FIFO makes the §30.2 bug reproducible instead of rare, which is the reason to pick it, but it is a simulation convenience.
- **No timed wait** (`pthread_cond_timedwait` analog) — deferred; M30 needs it for lock-ordering / hold-and-wait demos and will add it there.
- **Not a general-purpose primitive.** It is single-process, deliberately unfaithful in places (no priority donation, no re-check-on-wake beyond the predicate loop), and exists to teach Ch. 30.

## Verification

- Build clean (`dotnet build`).
- 7 new tests: lock-required guard; `Signal` releases exactly one and `Broadcast` releases all; separate instances are separate queues; `WaitWhile` re-checks after an unsatisfying signal; lost-wakeup hangs without a state variable and completes with one; single-CV hangs while two-CV completes with a verified-once sum; covering-condition lets the 10-byte waiter through while the 100-byte waiter stays parked.
- Mutation check on the isolation test, as described above.
- Smoke: each of the four `/cv/run` scenarios run 3× with identical results; 24 consecutive `single-cv` requests leave the host's thread count flat (17 → 16 → 14, i.e. no retention).

## Source Documents

- `docs/learning/m29-cv/overview.md` — milestone scope + OSEP citations.
- `docs/learning/m29-cv/s1-condition-variables.md` — slice doc + smoke evidence.
- `docs/learning/m6-bounded-worker-pool/s1-bounded-worker-pool.md` — predecessor pattern (one CV per monitor).
- OSTEP Ch. 30 §30.1 "Definition and Routines", "Mesa vs Hoare Semantics".
- OSTEP Ch. 30 §30.2 "The Producer/Consumer (Bounded Buffer) Problem", figure 30.11 / 30.12, "TIP: USE WHILE (NOT IF)".
- OSTEP Ch. 30 §30.3 "Covering Conditions".