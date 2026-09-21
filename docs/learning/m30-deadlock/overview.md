# Milestone 30: Deadlock Prevention & Avoidance (Ch. 32 §32.3)
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
What are the four Coffman conditions for deadlock, and how do the three prevention strategies (cycle prevention, hold-and-wait prevention, preemption) plus Banker's avoidance algorithm dismantle them? Why is prevention rarely used in practice but is the textbook foundation for understanding avoidance?
## Scope
A standalone deadlock simulator in `src/MiniWebServer.Host/MiniScheduler/DeadlockSim.cs` that demonstrates the canonical four Coffman conditions and the three prevention strategies from OSEP §32.3:
1. **Mutual exclusion** — inherent to locks; can't be removed without re-designing the resource.
2. **Hold-and-wait** — prevented by acquiring all locks atomically at once (`AcquireAll(Lock[])` helper that holds a meta-lock during the batch).
3. **No preemption** — prevented by releasing all locks on timeout (try-lock + retry).
4. **Circular wait** — prevented by enforcing a global lock ordering (every thread acquires locks in ascending numeric order).

Plus a **Banker's algorithm** demo [D64]: given N threads and M resource classes, the banker grants an allocation request only if the resulting state is safe (every thread can still complete). The slice drives these from `/deadlock/run?scenario=...` and reports which strategy fixed (or failed to fix) the deadlock.

## Slice
- **[s1-prevention-avoidance.md](./s1-prevention-avoidance.md)** — preventive primitives + Banker's algorithm demo + lock-ordering case study.

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
- `src/MiniWebServer.Host/MiniScheduler/DeadlockSim.cs` — new file. `AcquireAll`, `AcquireWithTimeout`, `AcquireInOrder`, plus `Banker.IsSafe(allocation, max, available)`.
- `src/MiniWebServer.Host/Program.cs` — `/deadlock/run` route.

## Implementation deviations from OSEP
- **Banker's algorithm assumes fixed max claims** — every thread declares its maximum resource needs up front. Real systems don't have this information. The slice makes this assumption explicit in the route output ("max claim: 3 units of R1, 1 unit of R2").
- **No detection-and-recovery** (OSEP §32.3 "Detect and Recover" via wait-for graphs): the slice is purely on prevention + avoidance.
- **Single-machine locks only**: distributed deadlock (Ch. 33's transactional memory analog) is out of scope.
- **The §32.3 "TIP: ENFORCE LOCK ORDERING BY LOCK ADDRESS" example** is what `AcquireInOrder` implements: when `m1 > m2`, acquire `m1` first; otherwise `m2` first. Same idea.

## What this slice does NOT do
- **Wait-for graph + cycle detection** (Ch. 32 §32.3 detect-and-recover) — the runtime-detection school.
- **Lock-free alternatives** (Ch. 29 + M22) — a different approach to the same problem.
- **Per-resource-class priority donation** — for the lab, all locks are equally important.
- **Real-time scheduling under resource constraints** — Ch. 23 §23.5's deferred domain.

## Where this leads
- The Banker primitive is reusable for any future work on resource allocators (e.g., a "disk bandwidth bank" for QoS in the I/O scheduler — out of scope).
- Lock ordering is the standard advice for production code; this slice makes it testable.
- Future: M23's `AcquireAll`-style meta-lock could be used to harden the M23.1 + M23.3 RBAC path (avoid auth deadlocks in a future multi-tenant variant).
