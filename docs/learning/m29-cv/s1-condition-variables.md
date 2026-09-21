# Slice 29.1: Condition Variables
> **What it does** — adds a `ConditionVariable` primitive (POSIX `pthread_cond_*` semantics on top of `Monitor.Wait` / `Monitor.Pulse`) and a `/cv/run` driver that reproduces the OSEP §30.1 lost-wakeup bug, then fixes it with the new primitive. Includes a §30.2 producer/consumer bounded-buffer demo with two CVs.
## Surface
- `ConditionVariable` (new class in `MiniScheduler`):
  - `void Wait(object lockObj)` — atomically: releases the lock, parks the thread on the CV queue, on wake re-acquires the lock, returns.
  - `void Signal()` — wakes one waiter (FIFO).
  - `void Broadcast()` — wakes all waiters.
- Both `Wait` and `Signal` are documented as **must be called while holding the lock** (POSIX convention; BCL `Monitor` enforces this implicitly via the lock-object identity).
## Files
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariable.cs` — new file (~50 LOC).
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariableDemos.cs` — three demo scenarios:
  1. `LostWakeupDemo(producers, consumers, items)` — the §30.1 "join without state variable" broken case: producer sets `done = 1` and calls `signal`, but consumer was past the check before producer ran → consumer waits forever. The fix: a CV protected by the lock + a state variable, with `while (!done) Wait()`.
  2. `SingleCvBoundedBuffer(capacity, items)` — the §30.2 broken case: one CV with `if` → wake-up-the-wrong-type bug (consumer wakes consumer).
  3. `TwoCvBoundedBuffer(capacity, items)` — the §30.2 correct solution: two CVs (`empty` + `full`) with `while` loops; producers signal `full`, consumers signal `empty`.
- `src/MiniWebServer.Host/Program.cs` — `/cv/run?scenario=lost-wakeup|single-cv|two-cv&producers=N&consumers=M&items=K` route.
## Smoke evidence
- `/cv/run?scenario=lost-wakeup&producers=4&consumers=4&items=100` returns a JSON-ish body showing:
  - **Naive (no CV)**: deadlock after K=10–50 items; consumers stuck on `flag == false`.
  - **With CV**: all 100 items produced + consumed; final counters match.
- `/cv/run?scenario=single-cv&capacity=5&items=1000` reports deadlock (the wake-up-the-wrong-type bug from §30.2).
- `/cv/run?scenario=two-cv&capacity=5&items=1000` reports produced = consumed = 1000, queue never exceeds capacity 5.
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds:
  - `Run("cv lost-wakeup is reproduced without cv", ...)` — assert the naive variant deadlocks.
  - `Run("cv fixes lost-wakeup", ...)` — assert the CV variant completes all items.
  - `Run("cv single-cv is broken under multiple consumers", ...)` — assert the §30.2 single-CV variant deadlocks with ≥2 consumers.
  - `Run("cv two-cv bounded buffer respects capacity", ...)` — assert max-in-flight never exceeds `capacity`.
## Source documents
- `docs/learning/m29-cv/overview.md` — milestone scope.
- `docs/learning/m6-bounded-worker-pool/overview.md` — M6's hand-rolled `Monitor.Wait` / `Monitor.Pulse` (predecessor pattern).
- OSTEP Ch. 30 §30.1 — CV definition + Mesa/Hoare distinction.
- OSEP Ch. 30 §30.2 — producer/consumer with one CV (broken) and two CVs (correct).
- OSEP Ch. 30 §30.3 — covering conditions + `Broadcast`.
