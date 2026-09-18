# Milestone 13.3: Multi-CPU Scheduling

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does scheduling change when the system has multiple CPUs? What new problems appear (cache coherence, synchronization, cache affinity, load imbalance)? And what are the two main approaches (SQMS vs MQMS)?

## Scope

A user-space simulator that runs jobs on N CPUs, with three modes: **SQMS** (Single-Queue Multi-Processor Scheduling, OSEP §10.4), **MQMS** (Multi-Queue Multi-Processor Scheduling, OSEP §10.5), and **MQMS + Work Stealing** (OSEP §10.5). Produces a per-CPU timeline showing which job ran on which CPU at each tick, plus a parallel-efficiency metric.

## Slice

- **[s1-multicpu.md](./s1-multicpu.md)** — `MultiCpuScheduler` with three modes, two demo workloads (sqms + imbalance + extreme imbalance), HTTP route `/multicpu/run`.

## OSEP coverage

- **Ch. 10 Multiprocessor Scheduling (Advanced)**
  - §10.1 Background: Multiprocessor Architecture — caches + cache coherence.
  - §10.2 Don't Forget Synchronization — locks needed even with coherence.
  - §10.3 One Final Issue: Cache Affinity.
  - §10.4 Single-Queue Scheduling (SQMS) — pros + cons (locking, no affinity).
  - §10.5 Multi-Queue Scheduling (MQMS) — pros + cons (load imbalance) + work stealing.
  - §10.6 Linux Multiprocessor Schedulers — O(1), CFS, BFS (deferred).
  - §10.7 Summary.

## OSEP §-specific deviations

| OSEP § | We do | We defer |
|---|---|---|
| §10.1 cache coherence | Not modeled (jobs are abstract units, not memory addresses). | Real MSI/MESI protocols. |
| §10.2 synchronization | Single-threaded simulator — no locks needed. | Concurrent simulator (would need a real concurrent queue). |
| §10.3 cache affinity | Tracked as `Job.LastCpu` + `Job.MigratedCount`. | Actual cache-warming cost model (OSEP §10 simulator measures wall-time). |
| §10.4 SQMS | Implemented with one global queue + FIFO dispatch. | Lock contention overhead (would require multi-threaded sim). |
| §10.5 MQMS | Per-CPU queue + round-robin job entry. | True randomized placement; affinity-based placement. |
| §10.5 work stealing | Periodic peek at peer queues; steal from fullest if peer has ≥2 jobs. | Cilk-style per-thread victim selection [FLR98]; randomized victim. |
| §10.6 Linux schedulers | Not modeled. | O(1), CFS, BFS — all deferred (covered conceptually in OSEP §9.7 CFS). |

## Key OSEP quotes

> "Thus far we've discussed a number of principles behind single-processor scheduling; how can we extend those ideas to work on multiple CPUs? What new problems must we overcome?" (OSEP §10 intro)

> "The most basic approach is to simply reuse the basic framework for single processor scheduling, by putting all jobs that need to be scheduled into a single queue; we call this single-queue multiprocessor scheduling or SQMS for short." (OSEP §10.4)

> "The first problem is a lack of scalability. To ensure the scheduler works correctly on multiple CPUs, the developers will have inserted some form of locking into the code ... Locks, unfortunately, can greatly reduce performance, particularly as the number of CPUs in the systems grows." (OSEP §10.4)

> "The second main problem with SQMS is cache affinity. For example ... each CPU simply picks the next job to run from the globally-shared queue, each job ends up bouncing around from CPU to CPU, thus doing exactly the opposite of what would make sense from the standpoint of cache affinity." (OSEP §10.4)

> "Because each CPU simply picks the next job to run from the globally-shared queue, each job ends up bouncing around from CPU to CPU." (OSEP §10.4)

> "Some systems opt for multiple queues, e.g., one per CPU. We call this approach multi-queue multiprocessor scheduling (or MQMS)." (OSEP §10.5)

> "If you've been paying attention, you might see that we have a new problem, which is fundamental in the multi-queue based approach: load imbalance." (OSEP §10.5)

> "One basic approach is to use a technique known as work stealing. With a work-stealing approach, a (source) queue that is low on jobs will occasionally peek at another (target) queue ... If the target queue is (notably) more full than the source queue, the source will 'steal' one or more jobs from the target to help balance load." (OSEP §10.5)

> "If you look around at other queues too often, you will suffer from high overhead and have trouble scaling ... If, on the other hand, you don't look at other queues very often, you are in danger of suffering from severe load imbalances. Finding the right threshold remains, as is common in system policy design, a black art." (OSEP §10.5)

## .NET mechanism

- `Job` gains `LastCpu` (the CPU it last ran on) + `MigratedCount` (how many times it moved between CPUs).
- `MultiCpuScheduler` with `MultiCpuMode` enum: `Sqms` / `Mqms` / `MqmsWorkStealing`.
- `CpuTimelineRow` — one row per CPU, with one cell per tick (job name or `.` for idle).
- `Run(maxTicks)` returns a formatted report: jobs header, timeline grid, final state, parallel-efficiency metric.

## Files

- `src/MiniWebServer.Host/MiniScheduler/Job.cs` — `LastCpu` + `MigratedCount` fields.
- `src/MiniWebServer.Host/MiniScheduler/MultiCpuScheduler.cs` (new, ~250 lines):
  - `MultiCpuMode` enum.
  - `MultiCpuScheduler` (SQMS / MQMS / MQMS-WorkStealing).
  - `CpuTimelineRow` (timeline visualization).
- `src/MiniWebServer.Host/MiniScheduler/Workloads.cs` — `SqmsDemoWorkload` (5 jobs × 4 ticks), `MqmsImbalanceWorkload` (4 jobs balanced 2-2), `MqmsExtremeImbalanceWorkload` (5 jobs unbalanced 4-1).
- `src/MiniWebServer.Host/Program.cs` — `/multicpu/run?mode=sqms|mqms|ws&workload=sqms|imbalance|extreme&cpus=N&ticks=M&peek=K` route.

## What this slice does NOT do

- Cache coherence (OSEP §10.1) — jobs are abstract work units, not memory addresses.
- Real lock contention (OSEP §10.4) — single-threaded simulator.
- Real cache-affinity cost model (OSEP §10 homework measures wall-time with warm/cold caches).
- Linux schedulers (O(1), CFS, BFS — OSEP §10.6).
- Cilk-style per-thread victim selection [FLR98] — we always steal from the fullest queue.

## Where this leads

The roadmap continues with **M20 Dining philosophers** (Ch. 31.6) — the classic concurrency problem with multiple processes needing shared resources, which connects to multi-process scheduling in interesting ways.

After M20: **M22 Lock-free** (Ch. 29) — data structures designed to avoid the synchronization overhead OSEP §10.2 mentions.
