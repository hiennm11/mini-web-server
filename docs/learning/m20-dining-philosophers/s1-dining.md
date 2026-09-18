# Slice 20.1: Dining Philosophers (Broken vs Fixed)

## What it does

Runs N real OS threads (philosophers) competing for N `SemaphoreSlim` instances (forks). Two modes:

- **Broken** (OSEP §31.6 "Broken Solution"): every philosopher grabs left-then-right. Classic deadlock: each holds one fork, waits for the next, nobody can proceed.
- **Fixed** (Dijkstra's fix, OSEP §31.6): the highest-numbered philosopher grabs right-then-left. The cycle is broken; no deadlock.

The HTTP route `/dining/run?mode=broken|fixed&philosophers=N&seconds=M&think=T&eat=E` drives both.

## Files added/changed

- `src/MiniWebServer.Host/MiniScheduler/DiningPhilosophers.cs` (new, ~280 lines):
  - `DiningMode` enum (Broken / Fixed).
  - `PhilosopherStats` (per-philosopher eat/think/deadlock counts).
  - `DiningPhilosophers` (the simulator).
- `src/MiniWebServer.Host/Program.cs` — `/dining/run` route.

## OSEP alignment

Implements OSEP §31.6 "The Dining Philosophers" — both the broken solution (deadlock) and Dijkstra's fix (last philosopher in reverse order).

## Smoke evidence

### Broken mode (think=0, eat=0) — true OSEP §31.6 deadlock
```
=== Dining Philosophers (M20 / OSEP §31.6) ===
mode: Broken  philosophers: 5  duration: 3s
think: 0-0ms  eat: 0-0ms

=== philosopher stats ===
  P0: ate=0 thought=1 (DEADLOCK)
  P1: ate=16 thought=17
  P2: ate=0 thought=1 (DEADLOCK)
  P3: ate=0 thought=1 (DEADLOCK)
  P4: ate=0 thought=1 (DEADLOCK)

=== summary ===
  total eats: 16
  total thinks: 21
  philosophers deadlocked: 4/5
```
4 of 5 philosophers got stuck holding one fork waiting for another — the classic OSEP §31.6 "Broken Solution" deadlock. (P1 was lucky; it grabbed both forks before the cycle formed.)

### Fixed mode (think=0, eat=0) — Dijkstra's fix
```
=== philosopher stats ===
  P0: ate=29 thought=30
  P1: ate=18 thought=19
  P2: ate=5  thought=6
  P3: ate=24 thought=25
  P4: ate=12 thought=13

=== summary ===
  total eats: 88         (vs 16 in broken mode — 5.5x more)
  philosophers deadlocked: 0/5
  
*** NO DEADLOCK ***
All philosophers managed to eat at least once.
This is OSEP §31.6 'Breaking The Dependency' — the last philosopher
grabs right then left, breaking the cycle of waiting.
```
All 5 philosophers ate (88 total eats vs only 16 in broken mode). No deadlock — Dijkstra's fix works.

## OSEP concept

> "If each philosopher happens to grab the fork on their left before any philosopher can grab the fork on their right, each will be stuck holding one fork and waiting for another, forever." (OSEP §31.6)

> "Because the last philosopher tries to grab right before left, there is no situation where each philosopher grabs one fork and is stuck waiting for another; the cycle of waiting is broken." (OSEP §31.6)

## .NET mechanism

- `Thread` — one per philosopher.
- `SemaphoreSlim(1, 1)` — one per fork; equivalent to OSEP `sem_t forks[i] = 1`.
- `ManualResetEventSlim` — start gate; all threads pause here so they all enter the contention phase at the same time (otherwise threads start at slightly different times and the deadlock is hard to reproduce).
- `CancellationTokenSource.Cancel()` — after `maxSeconds` elapses, breaks every thread out of its `SemaphoreSlim.Wait(ct)` call. Threads that were stuck holding one fork release it via `ReleaseHeld(p)`.

## What this slice does NOT do

- Resource hierarchy solution (always grab lower-numbered fork first) — an alternative fix that doesn't pick one "different" philosopher.
- Wait-for-graph cycle detector — we use a simple heuristic (didn't eat at all → deadlocked).
- Starvation analysis — OSEP §31.6 only requires no deadlock.
- Cigarette smoker's problem / sleeping barber — other OSEP "famous" concurrency problems.

## Deferred (other solutions)

- Chandy/Misra solution (message-passing).
- Tanenbaum's solution (arbitrator).
- Resource hierarchy by fork number.
