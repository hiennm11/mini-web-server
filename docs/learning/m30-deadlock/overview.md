# Milestone 30: Deadlock Prevention & Avoidance (Ch. 32 §32.3)
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
What are the four Coffman conditions for deadlock, and how do the three prevention strategies (cycle prevention, hold-and-wait prevention, preemption) plus Banker's avoidance algorithm dismantle them? Why is prevention rarely used in practice but is the textbook foundation for understanding avoidance?
## Scope

A deadlock lab in `src/MiniWebServer.Host/MiniScheduler/DeadlockSim.cs` that runs the four Coffman conditions from OSEP §32.3 and the responses to them, over one shared workload (four threads, two locks, half asking for `(L1, L2)` and half for `(L2, L1)`):

1. **Mutual exclusion** — inherent to locks; cannot be removed without lock-free redesign (M22).
2. **Hold-and-wait** — broken by `AcquireAll`: the chapter's global `prevention` lock around the whole batch.
3. **No preemption** — broken by trylock + back off + retry. The chapter's own caveat: this "doesn't really add preemption" but lets a thread preempt its own ownership. Residual hazard is livelock.
4. **Circular wait** — broken by `AcquireInOrder`, sorting by `ResourceLock.Id`. This is §32.3 TIP "ENFORCE LOCK ORDERING BY LOCK ADDRESS" with an explicit id instead of a pointer.

Plus a **Banker's algorithm** demo: a request is granted only if the resulting state still admits a safe sequence; a refusal leaves the state untouched.

The driver route `/deadlock/run?scenario=naive|ordering|batch|preempt|banker` shows the broken case beside each technique's fix.

### Two corrections to the original spec

- **There is no `Lock` type** in this repo — M5 models a lock as a plain `object` guarded by `Monitor`. The slice adds `ResourceLock` (an `Id` plus that monitor) because lock *ordering* needs something orderable, and a C# `object` has no usable address ordering.
- **The Banker's tables are not in OSTEP.** §32.3 names Dijkstra's algorithm [D64] and calls it "only useful in very limited environments"; the Max/Allocation/Need/Available tables and the safety-algorithm steps are [D64] via the standard OS literature. The code and docs mark the boundary. See ADR 0020.

## Slice
- **[s1-prevention-avoidance.md](./s1-prevention-avoidance.md)** — the five scenarios + the Banker + the staging decisions.

## OSTEP coverage
- **Ch. 32 §32.3** "Deadlock Bugs" [C+71]: introduces the four Coffman conditions and the prevention strategies.
  - §32.3 "Prevention → Circular Wait" [T+94]: lock ordering as the most practical prevention (Linux mm/filemap.c has 10 partial orderings).
  - §32.3 "Prevention → Hold-and-wait": atomic batch acquire with a meta-lock.
  - §32.3 "Prevention → No Preemption": `pthread_mutex_trylock` + back off + retry (note OSEP explicitly says "doesn't really add preemption" but rather uses trylock to allow self-preemption).
  - §32.3 "Prevention → Mutual Exclusion": lock-free data structures (cross-reference to Ch. 29, deferred).
  - §32.3 "Deadlock Avoidance via Scheduling" + Dijkstra's Banker's Algorithm [D64]: runtime safety check that admits a request only if the resulting state is safe. OSEP notes it's "only useful in very limited environments" because real systems don't have max-needs info.
- **Ch. 31 §31.6** "Dining Philosophers" [CM72] — M20 built the problem; M30 is the prevention half.
- **Ch. 32 §32.3** "Detect and Recover" [B+87, K87]: the third school, deferred.

## Files
- `src/MiniWebServer.Host/MiniScheduler/DeadlockSim.cs` — new file. `ResourceLock`, the five scenarios, and the `Banker` class.
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariable.cs` — adds `Wait(lockObj, timeout)`, the `pthread_cond_timedwait` shape M29 deferred.
- `src/MiniWebServer.Host/Program.cs` — `/deadlock/run` route.
- `docs/adr/0020-m30-deadlock-prevention-avoidance.md` — the two spec corrections and the staging decisions.

## Implementation deviations from OSEP
- **Lock ordering by explicit id, not address**: §32.3's TIP orders by the lock's pointer; a C# `object` has no usable address ordering, so `ResourceLock.Id` supplies one. The argument is otherwise identical — what matters is that the order is total and every caller goes through it.
- **Both broken and fixed cases are staged.** §32.3 says of the naive code that deadlock "does not necessarily occur; rather, it may occur", and measured the same for the trylock branch. Without staging the demos teach nothing, so each has a barrier that forces the situation the chapter describes.
- **Banker's algorithm is implemented from [D64] via the standard OS literature**, not from §32.3 — see the citation boundary above.
- **No detection-and-recovery** (wait-for graph + cycle detection): out of scope; the graph appears only as an explanatory diagram.
- **Single-machine locks only**; distributed deadlock is out of scope.
- **Two locks**, so the partial-ordering problem §32.3 discusses (ten lock groups in Linux `mm/filemap.c`) cannot be shown.

## What this slice does NOT do
- **Wait-for graph + cycle detection** (Ch. 32 §32.3 detect-and-recover) — the runtime-detection school. The graph is drawn in the `naive` output as an explanation, but nothing walks it.
- **Partial lock ordering** — two locks cannot demonstrate Linux `mm/filemap.c`'s ten lock groups.
- **Lock-free alternatives** (Ch. 29 + M22) — removing mutual exclusion entirely, a different approach to the same problem.
- **Distributed deadlock.**

## Where this leads
- The Banker is reusable for any future resource-allocator work (a disk-bandwidth bank for I/O QoS, say), though §32.3's verdict — "only useful in very limited environments" — applies.
- `ResourceLock.Id` gives M23's auth path something orderable if a multi-tenant variant ever needs to lock across the RBAC check and the crypto call.
- `ConditionVariable.Wait(lockObj, timeout)` is now available for any future bounded-wait pattern; M30 itself uses `Monitor.TryEnter` rather than the timed form.
