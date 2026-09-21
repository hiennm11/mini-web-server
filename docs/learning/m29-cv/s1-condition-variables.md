# Slice 29.1: Condition Variables
> **What it does** — adds a `ConditionVariable` primitive (POSIX `pthread_cond_*` semantics on top of `Monitor.Wait` / `Monitor.Pulse`) and a `/cv/run` driver that reproduces the OSEP §32.2 lost-wakeup bug, then fixes it with the new primitive. Includes a bounded-buffer producer-consumer demo.
## Surface
- `ConditionVariable` (new class in `MiniScheduler`):
  - `void Wait(object lockObj)` — atomically: releases the lock, parks the thread on the CV queue, on wake re-acquires the lock, returns.
  - `void Signal()` — wakes one waiter (FIFO).
  - `void Broadcast()` — wakes all waiters.
- Both `Wait` and `Signal` are documented as **must be called while holding the lock** (POSIX convention; BCL `Monitor` enforces this implicitly via the lock-object identity).
## Files
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariable.cs` — new file (~50 LOC).
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariableDemos.cs` — two demo scenarios:
  1. `LostWakeupDemo(producers, consumers, items)` — producer sets `flag = true`; consumer checks `flag` and goes to wait. Without CV: consumer waits forever if it lost the race. With CV: producer's `Pulse` reaches the waiter.
  2. `BoundedBufferDemo(capacity, items)` — classic bounded-buffer with `notFull` + `notEmpty` CVs sharing one lock; producers call `Wait(notFull)`; consumers call `Wait(notEmpty)`.
- `src/MiniWebServer.Host/Program.cs` — `/cv/run?scenario=lost-wakeup&producers=N&consumers=M&items=K` route.
## Smoke evidence
- `/cv/run?scenario=lost-wakeup&producers=4&consumers=4&items=100` returns a JSON-ish body showing:
  - **Naive (no CV)**: deadlock after K=10–50 items; consumers stuck on `flag == false`.
  - **With CV**: all 100 items produced + consumed; final counters match.
- `/cv/run?scenario=bounded-buffer&capacity=5&items=1000` reports produced = consumed = 1000, queue never exceeds capacity 5.
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds:
  - `Run("cv lost-wakeup is reproduced without cv", ...)` — assert the naive variant deadlocks.
  - `Run("cv fixes lost-wakeup", ...)` — assert the CV variant completes all items.
  - `Run("cv bounded buffer respects capacity", ...)` — assert max-in-flight never exceeds `capacity`.
## Source documents
- `docs/learning/m29-cv/overview.md` — milestone scope.
- `docs/learning/m6-bounded-worker-pool/overview.md` — M6's hand-rolled `Monitor.Wait` / `Monitor.Pulse` (predecessor pattern).
- OSTEP Ch. 32 §32.2 — the canonical lost-wakeup story.
