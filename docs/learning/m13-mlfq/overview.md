# Milestone 13: MLFQ Scheduler

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does a scheduler learn from job behavior to give good service to both interactive and CPU-bound jobs?

## Scope

A user-space MLFQ (Multi-Level Feedback Queue) simulator with the textbook rules R1-R5. Synthetic workloads (long CPU-bound + short interactive) and a per-tick trace output. Exposed via `/scheduler/run` HTTP route.

## Slice

- **[s1-mlfq.md](./s1-mlfq.md)** — `MiniScheduler` library: `Job`, `TraceEvent`, `Mlfq` (4 queues, doubling slices 10/20/40/80, periodic boost), `Workloads` (TwoJobs/TwoCpuBound/MixedWorkload), `SchedulerRunner.RunMlfq()`. HTTP route `/scheduler/run?workload=two|cpu|mixed&ticks=N&boost=M`.

## OSEP concept

- **Ch. 8 Multi-Level Feedback** — the canonical "learned" scheduler.
- **§8.1** — R1 (priority), R2 (RR within same priority).
- **§8.2 → §8.4** — R3 (new jobs at top), R4 (demote on allotment used; refined to track total CPU time at level).
- **§8.3** — R5 (periodic priority boost prevents starvation).
- **§8.5** — Solaris default (~60 queues, ~1s boost); our defaults are 4 queues / 50 ticks (smaller for simulator).

## Implementation deviations

Our `Mlfq` class uses a `YieldsEarly` flag rather than tracking total CPU time at the current level (the §8.4 anti-gaming refinement). Documented as a simplification in the slice doc.

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
