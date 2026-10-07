# ADR 0020: M30 Deadlock Prevention & Avoidance (OSEP Ch. 32 §32.3)

## Status

Accepted

## Date

2026-10-07

## Context

M20 (dining philosophers) and M22 (lock-free) already exhibit a deadlock, but nothing in the repo names the conditions that produce one or shows the standard responses. OSEP §32.3 gives the vocabulary: the four Coffman conditions, one prevention technique per condition, and deadlock *avoidance* as the alternative.

The milestone spec asked for `AcquireAll`, `AcquireWithTimeout`, `AcquireInOrder` and a `Banker` class over a `Lock[]` type.

### Two corrections to the spec

**1. There is no `Lock` type.** M5 models a lock as a plain `object` guarded by `Monitor`; that is what the whole repo uses. The slice introduces `ResourceLock`, a thin wrapper carrying a stable `Id` plus the `object` monitor. The `Id` exists for exactly one reason: §32.3 TIP "ENFORCE LOCK ORDERING BY LOCK ADDRESS" orders by the lock's address so a caller cannot express the order wrongly. The textbook gets that for free from the pointer; an `object` in C# has no usable ordering, so the id makes the total order explicit instead of accidental.

**2. The Banker's algorithm is not in OSTEP.** §32.3 names Dijkstra's algorithm [D64] as "one famous example of an approach like this" and immediately qualifies it as "only useful in very limited environments". The chapter gives avoidance only as a *scheduling* idea — a contention table and an assignment of threads to CPUs. The Max/Allocation/Need/Available tables and the safety-algorithm steps are Dijkstra 1964 as presented in the standard OS literature, not OSTEP. The code, the docs and the route output all mark this boundary rather than citing §32.3 for material it does not contain.

## Decision

Implement `DeadlockSim` with one scenario per technique, all over the same workload: four threads, two locks, half of them asking for `(L1, L2)` and half for `(L2, L1)`.

### 1. `naive` — the bug, staged

OSTEP §32.3 figure 32.6 is careful: "if this code runs, deadlock does not necessarily occur; rather, it may occur". Left alone it deadlocks rarely, and a demo that usually passes teaches nothing. Each thread takes its first lock and meets at a barrier; only then does anyone reach for a second. The barrier *is* the context switch the textbook describes.

### 2. `ordering` — prevention of circular wait

§32.3: "Probably the most practical prevention technique ... is to write your locking code such that you never induce a circular wait." `AcquireInOrder` sorts by `ResourceLock.Id` before acquiring. Callers still pass the two locks in opposite directions; the rule reconciles them.

### 3. `batch` — prevention of hold-and-wait

§32.3's `prevention` lock around the whole batch, verbatim in shape: take a global lock, acquire every lock the thread needs, release the global lock. No thread is ever part-way through a set.

### 4. `preempt` — prevention of no preemption

§32.3: take L1, try L2, on failure release L1 and retry, with "a random delay before looping back" to avoid livelock.

The collision is **staged**, and that turned out to be the whole design problem. Measured, not assumed: with a uniform acquisition order the contended branch fires on the first run (cold thread pool) and then stops firing entirely — warm threads queue on the first lock and the second is always free by the time the next arrives. A demo that reports `failed trylock attempts: 0` while explaining backoff is teaching nothing. Threads 0 and 1 therefore hold their first lock behind a `CountdownEvent` before attempting the second, which makes the collision certain. Verified: the contended branch fires on every run (1–3 retries observed).

### 5. `banker` — avoidance

The classic five-thread, three-class example. `Banker.TryRequest` grants a request only if the resulting state still admits a safe sequence; a refusal leaves the state byte-identical.

## Consequences

### Positive

- **All four Coffman conditions are visible**, each named against the technique that breaks it.
- **`naive` and `preempt` both deterministic.** The broken case always deadlocks; the fixed one always exercises the backoff path.
- **Prevention vs avoidance is contrastable in one response.** Prevention forbids a shape; avoidance admits or refuses at runtime. Same problem, two philosophies.
- **The citation boundary is explicit.** A reader who goes to §32.3 for the Banker tables finds the chapter says nothing of the kind.

### Review findings that changed the design

Recorded because each one replaced something that looked right and was not:

- **The staging barrier was itself the deadlock.** `naive` originally waited on four signals before attempting the second lock. Only two threads can hold a first lock simultaneously — T2 cannot enter L1 while T0 holds it — so the barrier could never complete and the reported deadlock was its own artefact. Measured: with a four-party barrier only 2 of 4 threads ever reached their second-lock attempt. It is now a two-party rendezvous.
- **Blocking waits cannot be unwound.** `Monitor.Enter` parks with no way out from outside, so the original `naive` stranded four threads per request (measured: 15 requests grew the host from 16 to 76 threads). The wait is now a bounded condition-variable wait, which makes the deadlock both observable (the run times out and reports DEADLOCK) and releasable (teardown broadcasts and every worker exits).
- **Two reported metrics could not fail.** The batch counter incremented *after* both resource locks were held, so it read 1 whether or not the prevention lock existed; it now increments on entry to the prevention region. The original "max locks held per batch" was worse — always 2 by construction.
- **The textbook's unsafe banker request never reached the safety check.** `[3,3,3]` exceeds `Available [3,3,2]`, so it is refused by the availability test. The demo now uses `T4 → [3,3,0]`, which fits both `Available` and the declared `Need` and is refused only because no safe sequence would remain.
- **The verdict and the completion count could disagree.** They were read at separate points, so a worker finishing in between produced `DEADLOCK` alongside `completed=4/4`. Both are now frozen from one read.

### Negative

- **`ResourceLock.Id` is a convention, not a type-system guarantee.** A caller that bypasses `AcquireInOrder` and locks `L2` first reintroduces the deadlock, exactly as §32.3 warns: "ordering is just a convention, and a sloppy programmer can easily ignore the locking protocol".
- **`RunsToCompletion` uses a 2 s deadline**, so a scenario that would take longer is reported as a deadlock. Fixed at four threads the workloads finish in milliseconds, so this is headroom rather than a real limit.
- **Two locks only.** The partial-ordering problem §32.3 discusses (ten lock groups in Linux `mm/filemap.c`) needs a realistic graph to be meaningful; two locks cannot show it.
- **No detect-and-recover.** The wait-for graph is drawn in the `naive` output but no cycle detector runs — that is the third school and is out of scope.
- **Timed wait was added to `ConditionVariable` for this slice** and is used by the tests; the deadlock scenarios themselves use `Monitor.TryEnter`, not the timed form. The overload earns its place by being the POSIX shape M29 deferred, not by being load-bearing here.

## Verification

- Build clean (`dotnet build`).
- 5 new tests: timed wait returns false on timeout and true on signal; `naive` deadlocks *and* reports `completed=0/4` while all three prevention scenarios clear the same workload with `completed=4/4`, the batch overlap counter reads 1 and the trylock contended branch fires; Banker refuses a request that reaches the safety check and rolls back through every thread's `Need`, grants the safe one, and rejects a `Need` overrun, an availability overrun and a negative request; Banker recognises an unsafe *state* independently of any request; no scenario retains threads across runs.
- Mutation check on the thread-retention test: removing the teardown broadcast makes it fail with "threads grew from 13 to 17".
- Smoke: all five scenarios run 3× with identical verdicts; 50 mixed requests leave the host's thread count flat (16 → 16); unknown scenario returns 400.

## Source Documents

- `docs/learning/m30-deadlock/overview.md` — milestone scope.
- `docs/learning/m30-deadlock/s1-prevention-avoidance.md` — slice doc + smoke evidence.
- `docs/learning/m20-dining-philosophers/overview.md` — M20's deadlock as the problem statement.
- OSTEP Ch. 32 §32.3 "Deadlock Bugs" — four Coffman conditions, prevention per condition, "Deadlock Avoidance via Scheduling", the lock-address TIP, and the verdict on Banker's algorithm. Figures 32.6 (simple deadlock) and 32.7 (dependency graph).
- Dijkstra 1964, "Een algorithme ter voorkoming van de dodelijke omarming" [D64] — the Banker tables and safety algorithm, as presented in the standard OS literature. **Not in OSTEP.**