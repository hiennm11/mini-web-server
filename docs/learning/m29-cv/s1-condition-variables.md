# Slice 29.1: Condition Variables

> **What it does** — adds a first-class condition variable (POSIX `pthread_cond_*` semantics, its own wait queue separate from the lock) plus a `/cv/run` route that reproduces the OSEP §30.1 lost-wakeup bug, the §30.2 one-CV deadlock, and the §30.3 covering-condition case — each beside its fixed counterpart.

## Question

Why doesn't `while (!ready) lock.Wait();` work on a bare `Monitor`? What does a condition variable actually buy you, what does it cost when used wrong, and why do two of them fix a problem that `while` cannot?

## OSTEP coverage

- **§30.1 Definition and Routines.** The CV is "an explicit queue that threads can put themselves on when some state of execution (i.e., some condition) is not as desired". `wait()` "release[s] the lock and put[s] the calling thread to sleep (atomically); when the thread wakes up ... it must re-acquire the lock before returning".
- **§30.1 Mesa vs Hoare.** "Signaling a thread only wakes them up; it is thus a hint that the state of the world has changed ... but there is no guarantee that when the woken thread runs, the state will still be as desired ... Virtually every system ever built employs Mesa semantics." Hence: "always use while loops."
- **§30.1 lost wakeup (figure 30.4).** Without the state variable, "the child will signal, but there is no thread asleep on the condition. When the parent runs, it will simply call wait and be stuck."
- **§30.2 Producer/Consumer.** One CV + `while` is "Better, But Still Broken": a consumer's signal wakes "whichever thread is at the head" — often another consumer. "A consumer should not wake other consumers, only producers, and vice-versa." Two CVs fix it "by design".
- **§30.3 Covering Conditions.** When the signaler does not know who can proceed, broadcast covers every case "conservatively" at the cost of waking threads that immediately re-sleep.

## Surface

- `ConditionVariable` (new, `MiniScheduler/ConditionVariable.cs`): `Wait(lockObj)`, `Signal()`, `Broadcast()`, `WaitWhile(lockObj, predicate)`. Owns a **private FIFO wait queue**, so `empty` and `fill` are separate queues — `Monitor.Pulse` cannot do this, since two CVs over one monitor are one queue (ADR 0019). `Wait` throws `SynchronizationLockException` if the caller does not hold the lock.
- `ConditionVariableDemos` (new): four scenarios, each run under a bounded join so a hang is reported as data rather than wedging the server. Every scenario releases its parked threads before returning — a timed join ends the *caller's* wait, not the worker's, and the route is reachable per request.
- `/cv/run?scenario=lost-wakeup|single-cv|two-cv|covering-condition` (new route).

## Smoke evidence

Each scenario was run 3× and returned identical results every time.

### `?scenario=lost-wakeup` (§30.1 figure 30.4)

```
broken (no state variable)          : HUNG - lost the signal, parent waits forever
fixed  (predicate + while loop)     : completed
```

The child signals before anyone parks. Without `done`, the signal is dropped and the parent sleeps forever. With the predicate, the parent observes `done == 1` and never parks at all — which is why the state variable, not the CV, is what carries the fact.

### `?scenario=single-cv` (§30.2 figures 30.10-30.11, broken)

```
producers=1 consumers=2 buffer capacity=1 items=4
condition variables in use: 1 (shared by both roles)
consumers parked before the producer ran: 2
produced=1 consumed=1 still_pending=0
threads parked on the single CV: 2
result: HUNG - 2 thread(s) still parked with 1/4 items moved
```

The trace is staged: both consumers park before the producer starts, and the producer holds the gate for its whole loop, so it is queued as a waiter before either consumer can act. `c1` then consumes, leaves, and signals — which reaches `c2`, not the producer. Two threads are parked with 1 of 4 items moved: `c2`, which found the buffer empty and re-slept, and `p1`, which is waiting for a wake that will never come. The loop is already `while`, not `if`; that fix does not help here.

### `?scenario=two-cv` (§30.2 figure 30.12, correct)

```
producers=2 consumers=3 items=1000 capacity=8
condition variables: 2 (empty = producers wait, fill = consumers wait)
produced=1000 consumed=1000
every value consumed exactly once: True (sum=499500, expected=499500)
max observed depth=8 (capacity=8)
signals: empty=122 fill=123
result: all threads joined
```

The sum matching 0..999 proves no item was lost or duplicated; depth never exceeds capacity.

### `?scenario=covering-condition` (§30.3 figure 30.15)

```
waiters parked before the free: 2 (Ta needs 100, Tb needs 10)
  Tc freed 50 bytes -> 50 available, waiters: Ta(100) unsatisfied, Tb(10) satisfied
  Tb (needs 10) woke and allocated
Tb (10-byte request, satisfied by the 50 free)   : allocated
Ta (100-byte request, still unsatisfied)            : correctly still parked after re-check
broadcast released: 2 waiter(s)
```

`signal()` here is a coin flip: half the time it wakes `Ta`, which fails its re-check, and `Tb` sleeps despite being satisfiable. Broadcast wakes both; `Tb` proceeds, `Ta` re-parks.

## Tests

`tests/MiniWebServer.Host.Tests/Program.cs` adds 8 tests — 7 carry the `slice 29.1` marker, and the timed-wait test is marked `slice 29.1 follow-up` because M30 is its real consumer:

- `cv wait without the lock is rejected` — `Wait` without the lock throws instead of hanging.
- `cv signal releases exactly one waiter and broadcast releases all` — queue count falls 3 → 2 → 0; one signal, two broadcast releases.
- `cv separate instances are separate queues` — signalling `empty` leaves a `fill` waiter parked. This is the §30.2 fix's load-bearing property.
- `cv waitwhile re-checks the predicate after every wakeup` — a signal into a state the predicate still rejects does not let the waiter proceed (Mesa).
- `cv lost-wakeup reproduces without a state variable and is fixed with one` — the demo must show HUNG *and* completed, else it proves nothing.
- `cv single shared condition variable hangs while two conditions complete` — plus the verified-once sum.
- `cv broadcast wakes the waiter's predicate that a signal could have missed` — §30.3.

### The isolation test is mutation-checked

Sharing one wait queue across all `ConditionVariable` instances — the exact behaviour `Monitor.Pulse` gives you — was applied as a mutation to confirm the test detects it:

| | consumer released by a signal to the wrong CV? | test result |
|---|---|---|
| shared queue (mutated) | yes | **fails** — catches it |
| per-instance queue (real) | no | passes |

The earlier version of this test used `WaitWhile` with a predicate, and that version **passed under the mutation**: the wrongly-woken consumer simply re-parked on its unmet predicate, leaving `WaitingCount` at 1. The test now uses a bare `Wait` and asserts the waiter did not return, which the predicate was masking.

## What this slice does NOT do

- **Timed wait** (`pthread_cond_timedwait` analog) — deferred to M30, which needs it for preemption-based deadlock prevention.
- **Hoare semantics** — we use Mesa, which is what the BCL and every real system uses (§30.1).
- **Migrating `WorkerPool`** — M6 needs only one condition, so its `Monitor.Wait`/`Pulse` is already correct; changing it would be churn.
- **CV across processes**, priority donation, or wait-queue reordering.

## Known limits

- **`Wait` requires single (non-recursive) ownership.** A re-entrant `lock(gate) { lock(gate) { Wait(gate); } }` would pass the `IsEntered` check, and the single `Monitor.Exit` would leave the monitor owned at depth 1 — the waiter would park still holding it and a discipline-following signaler could never acquire it. The BCL does not expose recursion depth, so this is documented as invalid rather than detected.
- **Interrupted waits retry to restore invariants.** If a thread is interrupted during `Wait`, node removal and lock reacquisition are retried until they complete, then the original exception is rethrown. Returning without ownership would make the caller's own `lock` throw from its `Exit` and mask the real cause.

## Source documents

- `docs/learning/m29-cv/overview.md` — milestone scope.
- `docs/learning/m6-bounded-worker-pool/s1-bounded-worker-pool.md` — predecessor pattern (one condition per monitor).
- `docs/adr/0019-m29-condition-variables.md` — why a real CV rather than a `Monitor` wrapper.
- OSTEP Ch. 30 §30.1-§30.3.