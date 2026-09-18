# Milestone 13: MLFQ Scheduler

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does a scheduler learn from job behavior to give good service to both interactive and CPU-bound jobs?

## Scope

A user-space MLFQ (Multi-Level Feedback Queue) simulator with the textbook rules R1-R5. Synthetic workloads (long CPU-bound + short interactive) and a per-tick trace output. Exposed via `/scheduler/run` HTTP route.

## Slice

- **[s1-mlfq.md](./s1-mlfq.md)** — `MiniScheduler` library: `Job`, `TraceEvent`, `Mlfq` (4 queues, doubling slices 10/20/40/80, periodic boost), `Workloads` (TwoJobs/TwoCpuBound/MixedWorkload), `SchedulerRunner.RunMlfq()`. HTTP route `/scheduler/run?workload=two|cpu|mixed&ticks=N&boost=M`.

## OSEP coverage

- **Ch. 8 Multi-Level Feedback**
  - §8.1 MLFQ: Basic Rules — R1 (priority) + R2 (RR within same priority).
  - §8.2 Attempt #1 — R3 (new jobs at top), R4a (demote on slice expiry), R4b (stay on yield).
  - §8.3 Attempt #2: The Priority Boost — R5 (periodic priority boost).
  - §8.4 Attempt #3: Better Accounting — **refines** R4a+4b into a single R4: "Once a job uses up its time allotment at a given level (regardless of how many times it has given up the CPU), its priority is reduced". Anti-gaming accounting.
  - §8.5 Tuning MLFQ — parameterization (Solaris: 60 queues, 20ms-100ms slices, ~1s boost), FreeBSD's decay-usage scheduler.

## OSEP §-specific deviations

| OSEP §8 rule | Our implementation |
|---|---|
| §8.2 Rule 4a (demote on slice expiry) | ✓ (when `YieldsEarly=false`) |
| §8.2 Rule 4b (stay on yield) | ✓ (when `YieldsEarly=true`) |
| §8.4 Rule 4 (anti-gaming — total CPU time at level) | ✗ — we use a single slice; §8.4's "allotment" could span multiple slices |
| §8.5 Solar is TS default (60 queues, ~1s boost) | ✗ — we use 4 queues, 50 ticks boost (smaller for simulator) |

The most significant deviation is **§8.4 vs §8.2**: OSEP explicitly refines the rules in §8.4 to fix gaming. A job that yields just before the slice ends can stay at the same queue (per §8.2 R4b). The §8.4 Rule 4 fixes this by tracking **total CPU time consumed at the level** and demoting when the allotment is exhausted (regardless of yield patterns). Our `YieldsEarly` boolean hint approximates §8.2 behavior; it does not implement §8.4 accounting.

## Key OSEP quotes

> "The fundamental problem MLFQ tries to address is two-fold. First, it would like to optimize *turnaround time*, which... is done by running shorter jobs first... Second, MLFQ would like to make a system feel responsive to interactive users." (OSEP §8 intro)

> "Rule 4: Once a job uses up its time allotment at a given level (regardless of how many times it has given up the CPU), its priority is reduced." (OSEP §8.4 final rule)

> "The real culprit here, as you might have guessed, are [voodoo constants]. One could try to make the system learn a good value, but that too is not straightforward." (OSEP §8.3 TIP — Ousterhout's Law)

## Implementation deviations (detailed)

- OSEP §8.5 mentions "voodoo constants" — we use defaults: 4 queues, slices doubling per level (10, 20, 40, 80 ticks), boost every 50 ticks. Tunable per workload.
- OSEP §8.5 mentions **decay-usage** schedulers (FreeBSD) — not implemented.

## .NET mechanism

- `Queue<Job>` per level — FIFO within priority.
- `Trace` is `List<TraceEvent>`; events are immutable records.
- The simulator is pure CPU simulation, no threads. Each call to `Tick()` advances by one tick.

## Files

- `src/MiniWebServer.Host/MiniScheduler/Job.cs`
- `src/MiniWebServer.Host/MiniScheduler/TraceEvent.cs`
- `src/MiniWebServer.Host/MiniScheduler/Mlfq.cs`
- `src/MiniWebServer.Host/MiniScheduler/Workloads.cs` + `SchedulerRunner.RunMlfq()`
- `src/MiniWebServer.Host/Program.cs` — `/scheduler/run` route.

## Where this leads

- M13.2 (Stride / Lottery) and M13.3 (Multi-CPU) — per `docs/adr/0006-mini-scheduler-mlfq-proportional-multicpu.md`.
