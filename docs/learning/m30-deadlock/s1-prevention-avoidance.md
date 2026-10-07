# Slice 30.1: Deadlock Prevention & Avoidance

> **What it does** — makes the four Coffman conditions runnable: one scenario reproducing the deadlock OSTEP §32.3 figure 32.6 describes, one per prevention technique (lock ordering, atomic batch acquire, trylock + backoff), and Banker's algorithm for *avoidance* — all over the same workload so the difference is visible in one response.

## Question

What are the four Coffman conditions for deadlock, and how does each prevention technique dismantle one of them? Why does prevention ship and avoidance not?

## OSTEP coverage

- **§32.3 four conditions.** "Mutual exclusion ... Hold-and-wait ... No preemption ... Circular wait" [C+71]. "If any of these four conditions are not met, deadlock cannot occur. Thus, we first explore techniques to prevent deadlock; each of these strategies seeks to prevent one of the above conditions from arising."
- **§32.3 prevention → circular wait.** "Probably the most practical prevention technique (and certainly one that is frequently employed) is to write your locking code such that you never induce a circular wait." Partial ordering exists in the wild: Linux `mm/filemap.c` [T+94] lists ten lock-acquisition groups. The chapter's TIP orders by lock *address*.
- **§32.3 prevention → hold-and-wait.** "The hold-and-wait requirement for deadlock can be avoided by acquiring all locks at once, atomically", via a global `prevention` lock. Cost, in the chapter's own words: "likely to decrease concurrency as all locks must be acquired early on (at once) instead of when they are truly needed."
- **§32.3 prevention → no preemption.** `pthread_mutex_trylock` + back off + retry. The chapter is explicit that this "doesn't really add preemption ... but rather uses the trylock approach to allow a developer to back out of lock ownership (i.e., preempt their own ownership)". New hazard: **livelock** — threads "repeatedly attempting this sequence and repeatedly failing to acquire both locks ... progress is not being made" — cured by "a random delay before looping back".
- **§32.3 avoidance via scheduling**, naming Dijkstra's Banker's Algorithm [D64] and concluding it is "only useful in very limited environments".

### Citation boundary

**The Banker tables are not in OSTEP.** §32.3 presents avoidance as a scheduling problem — a contention table plus an assignment of threads to CPUs — and only *names* Banker's algorithm without restating it. The `Allocation`/`Max`/`Need`/`Available` tables and the safety-algorithm steps below are Dijkstra 1964 [D64] as given in the standard OS literature. The code, this doc and the route output all mark the difference (ADR 0020).

## Surface

- `ResourceLock` (new): `Id` (the total-order key) + `Monitor` (the `object` the repo's locks actually are, per M5).
- `DeadlockSim` (new): the five scenarios.
- `Banker` (new): `Available`, `NeedOf`, `TryRequest`, `FindSafeSequence`.
- `/deadlock/run?scenario=naive|ordering|batch|preempt|banker` (new route).
- `ConditionVariable.Wait(lockObj, timeout)` — the timed form M29 deferred, POSIX `pthread_cond_timedwait`.

## Smoke evidence

Each scenario was run 3× with identical verdicts; unknown scenario returns 400.

### `?scenario=naive` — figure 32.6, the bug

```
threads=4 locks=2 (L1, L2)
acquisition order: threads 0,2 take L1->L2; threads 1,3 take L2->L1
completed=0/4
result: DEADLOCK

dependency graph (figure 32.7):
  T0 holds L1, wants L2   L2 held by T1
  T1 holds L2, wants L1   L1 held by T0
  -> a cycle in the wait-for graph; nobody can proceed
```

The chapter notes that "if this code runs, deadlock does not necessarily occur; rather, it may occur". Unstaged it deadlocks rarely. The staging is a **two-party** rendezvous — only the two threads that can hold a first lock simultaneously can form the cycle. A four-party barrier looks equivalent and is not: T2 cannot enter L1 while T0 holds it, so T2 can never signal, and the run deadlocks on the barrier instead of on the locks. Measured with a four-party barrier, only 2 of 4 threads ever reached their second-lock attempt.

### `?scenario=ordering` — prevention of circular wait

```
threads=4 locks=2 (L1 id=1, L2 id=2)
callers still pass (L1,L2) or (L2,L1) in opposite directions
completed=4/4
result: all threads completed - no circular wait

every acquisition ran L1 before L2, because AcquireInOrder sorts by id:
  T0 (L1,L2) -> L1,L2        T1 (L2,L1) -> L1,L2
  T2 (L1,L2) -> L1,L2        T3 (L2,L1) -> L1,L2
```

Same callers, same lock pair, same disagreement about order — reconciled by the rule rather than by the callers.

### `?scenario=batch` — prevention of hold-and-wait

```
threads=4 locks=2 (L1, L2)
completed=4/4
result: all threads completed - no hold-and-wait

atomic batches completed: 4
max threads inside the prevention lock at once: 1 (of 4)
```

The overlap counter is incremented *on entry* to the prevention region, before any resource lock is taken. Measured after acquisition it is 1 either way — the resource locks serialise the batch regardless — so it could not distinguish a working prevention lock from a deleted one. The value is also §32.3's concurrency cost made visible: every batch ran strictly one after another.

### `?scenario=preempt` — prevention of no preemption

```
threads=4 locks=2 (L1, L2); threads 0,2 take L1->L2, threads 1,3 take L2->L1
completed=4/4
result: all threads completed - no deadlock

successful acquisitions: 4
failed trylock attempts (backed out, retried): 2
```

The retry count is the interesting number: it is non-zero on every run, because threads 0 and 1 hold their first lock behind a barrier before attempting the second. Left to the scheduler, the contended branch fires once on a cold thread pool and then stops firing — warm threads queue on the first lock and the second is always free, so the demo would report `failed trylock attempts: 0` while explaining backoff.

### `?scenario=banker` — avoidance

```
threads=5, resource classes=3, initial Available = [3,3,2]

Need = Max - Allocation:
  T0 [7,4,3]   T1 [1,2,2]   T2 [6,0,0]   T3 [0,1,1]   T4 [4,3,1]

  T4 requests [3,3,0] -> DENIED (no safe sequence after the grant (would deadlock))
    (fits Available [3,3,2] and T4's Need [4,3,1], but leaves no safe sequence)
  T1 requests [1,2,2] -> GRANTED (state stays safe)

safe sequence after the granted request: [1,3,0,2,4]
```

The refused request matters. A textbook example that asks for `[3,3,3]` is refused by the *availability* check — more units than are free — so it never reaches the safety algorithm at all. T4's `[3,3,0]` passes availability and fits its declared `Need`, and is refused only because granting it would leave `Available [0,0,2]` with T0 needing `[7,4,3]` and T2 needing `[6,0,0]`: nothing can finish. That is the case avoidance exists to catch, and a request that merely exceeded `Available` would not exercise it.

T1's grant is safe for the opposite reason: T1's allocation then equals its `Max`, so T1 finishes first and returns everything, making the other four reachable.

## Tests

`tests/MiniWebServer.Host.Tests/Program.cs` adds 5 tests:

- `cv timed wait returns false on timeout and true when signaled` — the timed form hands the lock back on expiry, reports a signal when one arrives, and leaves nothing queued.
- `deadlock naive scenario deadlocks and prevention scenarios do not` — `naive` deadlocks *and* reports `completed=0/4` (a DEADLOCK verdict must never be paired with everyone finishing); all three prevention scenarios clear the same workload with `completed=4/4`; the batch scenario's overlap counter equals 1 (it would exceed 1 if the prevention lock were removed); and the trylock scenario's contended branch actually fires, so it cannot silently degrade into a demo of nothing.
- `banker refuses the unsafe request and grants the safe one` — the refusal is one that reaches the *safety* check rather than the availability check, and the rollback is verified through every thread's `Need` (which reflects `Allocation`), not just `Available`. Also covers the `Need` overrun, the availability overrun, and a negative request.
- `banker detects an unsafe state rather than only refusing requests` — `Available [1,1,1]` has no safe sequence even though no request was made.
- `deadlock scenarios do not retain threads across runs` — the `naive` case parks four threads on purpose, so this guards the exact failure the first draft had.

## Bugs found while building this

All five were silent-wrong-answer bugs, caught by asserting the outcome rather than the shape:

1. **The safety algorithm released the wrong amount.** When a thread can finish, it frees its *entire allocation* (by then equal to its `Max`), not its remaining `Need`. Adding `Need` alone left every thread short, so the algorithm found no safe sequence and Banker refused every request — including safe ones.
2. **Refusal did not roll back.** `TryRequest` saved `_available` by reference, then mutated that same array, so the undo restored the already-modified state. A refused request silently corrupted the resource table.
3. **The `naive` scenario leaked four threads per request.** Blocking in `Monitor.Enter` cannot be unwound from outside; the wait now uses a bounded condition-variable wait so the deadlock is *observable* (the run times out and reports DEADLOCK) and *releasable* (teardown broadcasts and every worker exits). Measured: 15 requests grew the host from 16 to 76 threads before the fix; 50 requests leave it at 16 after.
4. **The staging barrier was a deadlock rather than a demonstration.** It waited on four signals, but T2 cannot enter L1 while T0 holds it and T3 cannot enter L2 while T1 holds it — so the barrier could never complete and the reported deadlock was its own artefact. It is now a two-party rendezvous between exactly the threads that can hold a first lock at the same time.
5. **Two reported metrics could not fail.** The batch counter incremented after both resource locks were held, so it read 1 whether or not the prevention lock existed. The original `max locks held per batch` was worse — always 2 by construction. And the textbook's "unsafe" banker request was refused by the availability check, never reaching the safety algorithm.

## What this slice does NOT do

- **Detect-and-recover** — the wait-for graph appears in the `naive` output but no cycle detector runs. That is the third school.
- **Partial lock ordering** — two locks cannot show what Linux `mm/filemap.c`'s ten groups demonstrate.
- **Lock-free alternatives** — M22 covers removing mutual exclusion entirely.
- **Distributed deadlock.**
- **A `try-acquire` wrapper type** — the preempt scenario uses `Monitor.TryEnter` directly, which is the same primitive the chapter's `pthread_mutex_trylock` names.

## Source documents

- `docs/learning/m30-deadlock/overview.md` — milestone scope.
- `docs/learning/m20-dining-philosophers/overview.md` — M20's deadlock as the problem statement.
- `docs/adr/0020-m30-deadlock-prevention-avoidance.md` — the two spec corrections and the staging decisions.
- OSTEP Ch. 32 §32.3; Dijkstra 1964 [D64] for the Banker tables.