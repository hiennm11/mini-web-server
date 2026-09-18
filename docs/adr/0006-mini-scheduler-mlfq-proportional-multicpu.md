# ADR 0006: Mini Scheduler (MLFQ + Proportional Share + Multi-CPU)

## Status

Accepted

## Date

2026-09-18

## Context

ADR 0004 lists OSEP Part I (Ch. 7-10: scheduling) as out of scope, deferring it to its own ADR. The repo is now a complete OS concepts lab for OSEP's three pieces (Virtualization via the bounded worker pool, Concurrency via the locks/pools/caches, Persistence via the journaled Mini FS) but has no coverage of the CPU scheduling algorithms themselves. These are foundational to the rest of OSEP Part II and historically one of the most-discussed topics in OS courses.

This ADR records the next concrete milestones for the scheduling chapters. The work does not touch the running MiniWebServer worker pool — it's a self-contained library that simulates a scheduling algorithm against a synthetic workload, exposed via HTTP routes for observability. That keeps it orthogonal to the existing server.

## Decision

We will add a `MiniScheduler` library plus a series of lesson slices, one per major algorithm family, scoped small enough to land as a single commit per slice.

| ID | Name | OSEP chapter(s) | Lesson slices | Status |
|---|---|---|---|---|
| **M13 (13.1)** | Mini MLFQ: multi-level feedback queue with priority boost | Ch. 8 | 1 slice (4 queues, time-slice demotion, periodic boost) | Future |
| **M13 (13.2)** | Stride / lottery proportional-share scheduler | Ch. 9 | 1 slice (stride deterministic + lottery randomized) | Future |
| **M13 (13.3)** | Multi-CPU scheduler (single shared queue + affinity hints) | Ch. 10 | 1 slice (N virtual CPUs, per-CPU runqueue, work stealing) | Future |
| **M13 (13.4)** | Scheduling-trace HTTP route + replayable workloads | n/a | 1 slice (workload DSL + `/scheduler/run` route + smoke) | Future |

Ordering rationale:

- **M13 (MLFQ)** is the most famous algorithm in OSEP Part I and demonstrates the key OS pattern of multi-queue + history-based demotion + periodic anti-starvation boost. It's the natural first slice.
- **M13.2 (stride / lottery)** contrasts with MLFQ: deterministic (stride) vs randomized (lottery), proportional-share instead of priority-class. The two implementations are small enough to fit in one slice.
- **M13.3 (multi-CPU)** introduces the cross-CPU load-balancing problem (work stealing vs single shared queue). Closes Ch. 10.
- **M13.4 (HTTP integration)** wires the scheduler into the running server as a `/scheduler/run` route, so we can run a synthetic workload and inspect the trace through a normal HTTP client. Mirrors how M12 routes were added per-slice.

These milestones do **not** replace the actual MiniWebServer worker pool. They are observable simulations: the user picks a workload (or supplies one), picks a scheduler, and gets a step-by-step trace of which job ran when. This makes the algorithms easy to compare side-by-side.

## Non-goals

- **Real kernel scheduling**: this is a user-space simulator. We are not modifying Linux CFS or Windows UMS.
- **Multiprocessor synchronization primitives**: Ch. 10's single-queue multi-CPU scenario can deadlock if not done carefully; we will note the lock contention in the trace but won't model real cross-CPU lock contention.
- **Real-time scheduling (Ch. 32, 33)**: out of scope; the repo already covers Ch. 33 via the bounded worker pool.
- **Energy-aware scheduling (Ch. 11)**: out of scope.

## Architecture

```
src/MiniWebServer.Host/MiniScheduler/
  Job.cs             // id, name, totalCpuNeeded, burstRemaining, state, queueLevel, tickets
  Mlfq.cs            // MLFQ scheduler: queues[], slice[], boostPeriod
  Stride.cs          // stride + lottery schedulers: tickets, stride, pass
  MultiCpu.cs        // N virtual CPUs + per-CPU runqueue + work stealing
  Workload.cs        // synthetic workload DSL (JobSpec list)
  Trace.cs           // per-tick trace event (Time, JobId, Action)
  SchedulerRunner.cs // entry point: pick scheduler + workload, run N ticks, return Trace
```

The HTTP integration (M13.4) lives in `Program.cs` as `/scheduler/run?algo=mlfq|workload=...` returning the trace as text or JSON.

The smoke tests live in `.gitnexus/smoke-m13-*.ps1`, one per slice.

## OSTEP Coverage Map (after M13 lands)

| Chapter | Algorithm | Slice |
|---|---|---|
| Ch. 7 | Process API | not covered (we use threads, not processes) |
| Ch. 8 | MLFQ | 13.1 |
| Ch. 9 | Stride / Lottery | 13.2 |
| Ch. 10 | Multi-CPU | 13.3 |
| Ch. 11 | Energy-aware | not covered |

Ch. 7 (fork/exec/wait) is implicitly covered by M11 (raw syscalls) and the rest of the repo's process model. The remaining gap is Ch. 11, which is out of scope for this ADR.

## Consequences

Good:

- Closes OSEP Part I (Ch. 7-10) for the lab. The repo becomes a complete walkthrough of OSEP Parts I (intro + virtualization + concurrency) and II (persistence) at least.
- Each slice is small (~30-60 min) and self-contained, so partial progress is still useful.
- MLFQ / stride / multi-CPU are all classic algorithms with well-known behaviors that make good demonstrations.

Tradeoffs:

- A simulated scheduler is not the same as a real one. There's a risk that students mistake the simulation for production behavior.
- The scheduler library is detached from the actual MiniWebServer worker pool. Linking them would require a full rewrite of M6/M7, which is not the goal.

## Verification

- A new slice doc under `docs/learning/` is the unit of work. Each is independently testable per `README.md`.
- `dotnet build MiniWebServer.sln` and the existing test project remain the green bar.
- Each slice adds its own smoke (PowerShell) and learning note.
- The roadmap is complete when the Status column here is all Done.

## Next Steps

1. Land M13 (MLFQ) first. This ADR is committed alongside slice 13.1's code so the roadmap and the first slice land together.
2. After MLFQ, evaluate whether stride/lottery (M13.2) should come before multi-CPU (M13.3). Both are small.
3. M13.4 (HTTP integration) lands last and wires all three into a `/scheduler/run` route.
