# Slice 30.1: Deadlock Prevention & Avoidance
> **What it does** — adds a `DeadlockSim` primitive that demonstrates three of the four Coffman-condition-based prevention strategies (lock ordering, atomic batch acquire, preemption via timeout) plus Banker's algorithm for deadlock avoidance. Driver route `/deadlock/run` shows the cost of each strategy.
## Surface
- `DeadlockSim` (new class in `MiniScheduler`):
  - `void AcquireAll(Lock[] locks)` — atomic batch acquire; holds a meta-monitor while taking each lock in turn. Prevents **hold-and-wait**.
  - `bool AcquireWithTimeout(Lock l, TimeSpan timeout)` — try-lock + retry; on timeout, releases everything held. Prevents **no preemption**.
  - `void AcquireInOrder(Lock[] locks)` — sorts by lock ID and takes in ascending order. Prevents **circular wait**.
  - `class Banker { bool IsSafe(int[] allocation, int[] max, int[] available); bool TryRequest(int tid, int[] request); }` — Banker's safety check + request admission.
- All three prevention strategies share the `Lock` type from M5; the slice does not introduce new primitive types.
## Files
- `src/MiniWebServer.Host/MiniScheduler/DeadlockSim.cs` — new file (~150 LOC).
- `src/MiniWebServer.Host/Program.cs` — `/deadlock/run?scenario=...` route, scenarios:
  - `naive` — current M20-style raw lock acquisition; reproducible deadlock.
  - `ordering` — global lock-ordering enforced; no deadlock, but locks taken in unnatural order.
  - `batch` — `AcquireAll` for any pair of locks a thread needs; deadlock-free but lower concurrency (can't release early).
  - `preempt` — `AcquireWithTimeout`; the losing thread backs off and retries; no deadlock.
  - `banker` — N=5 threads, M=3 resource classes; Banker admits a sequence of requests and reports the safe state at each step.
## Smoke evidence
- `/deadlock/run?scenario=naive&threads=5&resources=2&attempts=100` — deadlock rate ~ X% (reproduces the M20 problem).
- `/deadlock/run?scenario=ordering&...` — 0 deadlocks; threads complete; reports average wait time (typically higher because locks are taken in fixed order even when not strictly needed).
- `/deadlock/run?scenario=banker&threads=5&resources=3&requests=20` — Banker admits all requests; reports "safe" at each step.
- `/deadlock/run?scenario=banker&threads=5&resources=3&requests=20&unsafe=true` — Banker rejects the unsafe request and reports "unsafe — would have deadlocked".
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds:
  - `Run("deadlock ordering prevents circular wait", ...)` — 1000 random orderings under the ordering rule, no deadlock.
  - `Run("deadlock batch acquire prevents hold-and-wait", ...)` — assert no thread holds a partial set of locks when blocked.
  - `Run("banker rejects unsafe request", ...)` — assert Banker rejects the canonical unsafe allocation from OSEP §32.3 figure 32.9.
  - `Run("banker admits safe request", ...)` — assert Banker admits a known-safe sequence.
## Source documents
- `docs/learning/m30-deadlock/overview.md` — milestone scope.
- `docs/learning/m20-dining-philosophers/overview.md` — M20's deadlock as the problem statement.
- OSTEP Ch. 32 §32.3 — prevention strategies + Banker's algorithm.
