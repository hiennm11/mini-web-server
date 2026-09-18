# Milestone 20: Dining Philosophers

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How do you synchronize N processes that each need access to multiple shared resources in a cyclic pattern, without deadlock? What does the classic "Dining Philosophers" problem teach us about the difference between **circular wait** and **broken cycle**?

## Scope

A user-space simulator that runs N real OS threads (philosophers) competing for N `SemaphoreSlim` instances (forks), with two modes:
- **Broken** (OSEP §31.6 "Broken Solution"): every philosopher grabs left-then-right. Classic deadlock pattern.
- **Fixed** (Dijkstra's solution, OSEP §31.6): the highest-numbered philosopher goes right-then-left; everyone else goes left-then-right. The cycle is broken.

Exposed via `/dining/run?mode=broken|fixed&seconds=N&think=…&eat=…` HTTP route. Reports per-philosopher eat/think counts and detects deadlock.

## Slice

- **[s1-dining.md](./s1-dining.md)** — `DiningPhilosophers` class with two modes; `ManualResetEventSlim` start gate so all philosophers contend simultaneously; HTTP route.

## OSEP coverage

- **Ch. 31 Semaphores** (§31.6 The Dining Philosophers).
- **Ch. 32 Common Concurrency Problems** (implicit): circular wait → deadlock, breaking dependency.

OSEP §31.6 introduces the problem:
- Five philosophers, five forks (one between each pair).
- Each philosopher needs both their left and right fork to eat.
- "The contention for these forks, and the synchronization problems that ensue, are what makes this a problem we study in concurrent programming."

OSEP §31.6 "Broken Solution":
- Every philosopher grabs `forks[left(p)]` then `forks[right(p)]`.
- "If each philosopher happens to grab the fork on their left before any philosopher can grab the fork on their right, each will be stuck holding one fork and waiting for another, forever."

OSEP §31.6 "A Solution: Breaking The Dependency":
- "Let's assume that philosopher 4 (the highest numbered one) gets the forks in a *different* order than the others ... the cycle of waiting is broken."

## OSEP §-specific deviations

| OSEP §31.6 | We do | We defer |
|---|---|---|
| §31.6 5 philosophers + 5 forks | Configurable N (default 5) | N=2 edge case has different semantics (always works in either mode). |
| §31.6 `while(1) { think; get_forks; eat; put_forks; }` | Real `Thread` per philosopher, `SemaphoreSlim(1,1)` per fork. | Pure spin-wait. |
| §31.6 broken solution | `if (p == N-1) right→left else left→right` | Test-and-set based "test if neighbor is eating". |
| §31.6 breaking-the-dependency | Last philosopher in reverse order. | Resource hierarchy (number all forks, always grab lower number first). |
| §31.6 thinker not specified | 50-100ms random `Thread.Sleep` between cycles (default). | Configurable via `?think=` & `?eat=` params. |
| §31.6 no deadlock detection | After timeout, mark any non-eating philosopher as "DEADLOCK". | Wait-for-graph cycle detector. |

## Key OSEP quotes

> "Assume there are five 'philosophers' sitting around a table. Between each pair of philosophers is a single fork (and thus, five total). The philosophers each have times where they think, and don't need any forks, and times where they eat. In order to eat, a philosopher needs two forks, both the one on their left and the one on their right." (OSEP §31.6)

> "If each philosopher happens to grab the fork on their left before any philosopher can grab the fork on their right, each will be stuck holding one fork and waiting for another, forever." (OSEP §31.6)

> "Let's assume that philosopher 4 (the highest numbered one) gets the forks in a *different* order than the others ... Because the last philosopher tries to grab right before left, there is no situation where each philosopher grabs one fork and is stuck waiting for another; the cycle of waiting is broken." (OSEP §31.6)

## .NET mechanism

- `System.Threading.Thread` — one per philosopher (real OS thread).
- `System.Threading.SemaphoreSlim(1, 1)` — counting semaphore with initial value 1; equivalent to OSEP `sem_t forks[i]` initialized to 1.
- `System.Threading.ManualResetEventSlim` — start gate; all philosophers pause here, then begin simultaneously.
- `System.Threading.CancellationTokenSource` — signals all philosophers to stop after `maxSeconds`.

## Files

- `src/MiniWebServer.Host/MiniScheduler/DiningPhilosophers.cs` (new, ~280 lines):
  - `DiningMode` enum (Broken / Fixed).
  - `PhilosopherStats` (per-philosopher eat/think/deadlock counts).
  - `DiningPhilosophers` (the simulator).
- `src/MiniWebServer.Host/Program.cs` — `/dining/run?mode=broken|fixed&philosophers=N&seconds=M&think=T&eat=E` route.

## What this slice does NOT do

- Resource hierarchy solution (always grab the lower-numbered fork first) — alternative to OSEP §31.6's "break one philosopher's order".
- Wait-for-graph deadlock detection (we use a heuristic: didn't eat at all → deadlocked).
- Starvation analysis (OSEP §31.6 only requires no deadlock, not no starvation).
- Cigarette smoker's problem / sleeping barber problem (other OSEP "famous" concurrency problems).

## Where this leads

The roadmap continues with **M22 Lock-free** (Ch. 29) — data structures designed to avoid the synchronization overhead that motivated the dining-philosophers problem in the first place. After M22 we return to FS-related work (M21 FFS, Ch. 41).
