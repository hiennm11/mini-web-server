# Slice 13.3.1: Multi-CPU Scheduling (SQMS vs MQMS vs Work-Stealing)

## What it does

Adds a multi-CPU scheduler that supports three modes, with a per-CPU timeline view:

- **SQMS** (Single-Queue Multi-Processor Scheduling, OSEP §10.4): one global FIFO queue, each idle CPU pulls the next job from the front. Simple and naturally balanced, but bad cache affinity (every job can land on any CPU).
- **MQMS** (Multi-Queue Multi-Processor Scheduling, OSEP §10.5): one queue per CPU, each CPU pulls from its own queue. Perfect cache affinity, but prone to load imbalance.
- **MQMS + Work Stealing** (OSEP §10.5): MQMS where idle CPUs periodically peek at peer queues and steal one job from the fullest queue if it has at least 2 jobs.

The HTTP route `/multicpu/run?mode=sqms|mqms|ws&workload=sqms|imbalance|extreme&cpus=N&ticks=M&peek=K` drives all three.

## Files added/changed

- `src/MiniWebServer.Host/MiniScheduler/Job.cs` — `LastCpu` + `MigratedCount` fields.
- `src/MiniWebServer.Host/MiniScheduler/MultiCpuScheduler.cs` (new, ~250 lines):
  - `MultiCpuMode` enum.
  - `MultiCpuScheduler` (SQMS / MQMS / MQMS-WorkStealing).
  - `CpuTimelineRow` (per-CPU timeline cells).
- `src/MiniWebServer.Host/MiniScheduler/Workloads.cs` — `SqmsDemoWorkload` (5 jobs × 4 ticks), `MqmsImbalanceWorkload` (4 jobs balanced 2-2), `MqmsExtremeImbalanceWorkload` (5 jobs unbalanced 4-1).
- `src/MiniWebServer.Host/Program.cs` — `/multicpu/run` route.

## OSEP alignment

Implements OSEP §10.4 (SQMS), §10.5 (MQMS + work stealing), §10.3 (cache affinity tracking).

## Smoke evidence

### SQMS with extreme workload (4 jobs on CPU 0, 1 on CPU 1)
```
=== timeline (10 ticks) ===
tick    |   0 |   1 |   2 |   3 |   4 |   5 |   6 |   7 |   8 |   9 |
CPU 0  |   A |   C |   E |   B |   D |   A |   C |   E |   B |   D |
CPU 1  |   B |   D |   A |   C |   E |   B |   D |   A |   C |   E |

parallel efficiency: 100.0%   (10 ticks used, 10 ideal)
```
Perfect parallelism — single queue naturally balances. But **all 5 jobs bounce between CPUs** every tick (the OSEP §10.4 cache-affinity problem).

### MQMS (no stealing) with extreme workload
```
=== timeline (12 ticks) ===
tick    |   0 |   1 |   2 |   3 |   4 |   5 |   6 |   7 |   8 |   9 |  10 |  11 |
CPU 0  |   A |   C |   E |   A |   C |   E |   A |   C |   E |   A |   C |   E |
CPU 1  |   B |   D |   B |   D |   B |   D |   B |   D |   . |   . |   . |   . |

parallel efficiency: 83.3%   (12 ticks used, 10 ideal)
```
CPU 1 idles for 4 ticks (visible as `.` in the timeline) — exactly the OSEP §10.5 "load imbalance" problem.

### MQMS + Work Stealing (peek=1) with extreme workload
```
=== timeline (11 ticks) ===
tick    |   0 |   1 |   2 |   3 |   4 |   5 |   6 |   7 |   8 |   9 |  10 |
CPU 0  |   A |   C |   E |   A |   C |   E |   A |   C |   E |   C |   E |
CPU 1  |   B |   D |   B |   D |   B |   D |   B |   D |   A |   . |   . |

J1 A: ran=4/4 (100.0%) last-cpu=1 migrations=1   ← A migrated to CPU 1!
parallel efficiency: 90.9%   (11 ticks used, 10 ideal)
```
At tick 8 CPU 1 (which just finished B, D) steals `A` from CPU 0. `migrations=1` for A; 11 ticks instead of 12.

## OSEP concept

> "Because each CPU simply picks the next job to run from the globally-shared queue, each job ends up bouncing around from CPU to CPU, thus doing exactly the opposite of what would make sense from the standpoint of cache affinity." (OSEP §10.4)

> "If you've been paying attention, you might see that we have a new problem, which is fundamental in the multi-queue based approach: load imbalance." (OSEP §10.5)

> "With a work-stealing approach, a (source) queue that is low on jobs will occasionally peek at another (target) queue ... If the target queue is (notably) more full than the source queue, the source will 'steal' one or more jobs from the target to help balance load." (OSEP §10.5)

## .NET mechanism

- `Queue<Job>` for both SQMS global queue and per-CPU MQMS queues.
- `CpuTimelineRow.Cells` is a `List<string>` — one entry per tick.
- `MultiCpuScheduler.Tick()` runs all N CPUs' picks in lock-step (no race conditions; the simulator is single-threaded).
- `Random` is used for the seed (currently unused — work-stealing picks the fullest queue deterministically).

## What this slice does NOT do

- Real cache-coherence protocols (MSI/MESI) — OSEP §10.1.
- Real lock-contention overhead — OSEP §10.4.
- Cilk-style per-thread victim selection — we always steal from the fullest queue.
- Linux O(1), CFS, BFS — OSEP §10.6 (all deferred; covered conceptually in M13.2 §9.7).
- Cache-warming cost model — OSEP homework (Ch. 10 multi.py) measures wall-time with warm/cold caches; we report logical ticks only.

## Deferred (other multi-CPU extensions)

- Affinity-aware placement (when a job arrives, prefer to put it on a CPU that recently ran it).
- Per-CPU cache model (warm/cold counter, access latency).
- Cilk-style randomized victim selection.
- NUMA-aware placement (which CPU/memory bank).
