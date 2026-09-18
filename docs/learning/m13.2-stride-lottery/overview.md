# Milestone 13.2: Stride & Lottery Scheduling

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How do we share the CPU proportionally between jobs (instead of optimizing turnaround or response time)? And what are the two main approaches — random (lottery) vs deterministic (stride)?

## Scope

Add two proportional-share schedulers (Stride and Lottery) alongside the existing MLFQ (M13.1). Both use **tickets** as the share representation. Stride is deterministic (OSEP §9.6 "Stride scheduling gets them exactly right at the end of each scheduling cycle"); Lottery is random (OSEP §9.4 "Lottery scheduling achieves this probabilistically").

## Slice

- **[s1-stride-lottery.md](./s1-stride-lottery.md)** — both schedulers + a `proportional` workload to verify the share.

## OSEP coverage

- **Ch. 9 Scheduling: Proportional Share**
  - §9.1 Basic Concept: Tickets Represent Your Share — lottery scheduling.
  - §9.2 Ticket Mechanisms — ticket currency, ticket transfer, ticket inflation.
  - §9.3 Implementation — the OSEP lottery pseudocode (Figure 9.1).
  - §9.4 An Example — fairness study (F = first-finish / second-finish).
  - §9.5 How To Assign Tickets? — the open question.
  - §9.6 Stride Scheduling — Waldspurger's deterministic version.
  - §9.7 The Linux Completely Fair Scheduler (CFS) — production reference (out of scope for this slice).

## OSEP §-specific deviations

- **STRIDE_CONST = 10_000** (OSEP §9.4 also uses 10_000).
- **Tie-breaking**: Stride's "lowest pass wins" tie is broken randomly (OSEP doesn't specify — `min` on a heap is the usual approach).
- **Job allocation**: jobs start at pass=0 and get scheduled until burst runs out. OSEP §9.4 examples use new-job entry with pass=0; same here.
- **No ticket currency / transfer / inflation** (OSEP §9.2) — those are mechanisms for users to compose tickets; our simulator is single-process-per-ticket-holder.
- **No CFS** (OSEP §9.7) — out of scope; we cover the two textbook approaches.

## Key OSEP quotes

> "Every so often, hold a lottery to determine which process should get to run next; processes that should run more often should be given more chances to win the lottery." (OSEP §9.1)

> "Each job in the system has a stride, which is inverse in proportion to the number of tickets it has." (OSEP §9.6)

> "The basic idea is simple: at any given time, pick the process to run that has the lowest pass value so far; when you run a process, increment its pass counter by its stride." (OSEP §9.6)

> "Lottery scheduling achieves the proportions probabilistically over time; stride scheduling gets them exactly right at the end of each scheduling cycle." (OSEP §9.6)

> "when the job length is not very long, average fairness can be quite low. Only as the jobs run for a significant number of time slices does the lottery scheduler approach the desired fair outcome." (OSEP §9.4 — fairness study)

## .NET mechanism

- `Job` gains `Tickets` (share) + `Stride` (STRIDE_CONST / Tickets) + `Pass` (counter that increments per run).
- `StrideScheduler.Tick()` — pick lowest pass job, run it, increment pass.
- `LotteryScheduler.Tick()` — pick random ticket, walk jobs, pick the one whose ticket range contains the winner.
- `SimTraceLine` — flat trace record (proportional-share schedulers have no queues / boost events, so the existing `TraceEvent` designed for MLFQ is overkill).

## Files

- `src/MiniWebServer.Host/MiniScheduler/Job.cs` — `Tickets` + `Stride` + `Pass` fields + auto-computed `Stride` in constructor.
- `src/MiniWebServer.Host/MiniScheduler/ProportionalScheduler.cs` (new, ~230 lines):
  - `SimTraceLine` (flat trace record).
  - `StrideScheduler` (OSEP §9.3 + §9.6).
  - `LotteryScheduler` (OSEP §9.1 + §9.3 Figure 9.1).
- `src/MiniWebServer.Host/MiniScheduler/Workloads.cs` — `ProportionalWorkload` (3 jobs with tickets 100/50/250).
- `src/MiniWebServer.Host/Program.cs` — `/scheduler/run?algo=stride|lottery&workload=proportional` parameters.

## What this slice does NOT do

- Ticket currency / transfer / inflation (OSEP §9.2).
- Linux CFS implementation (OSEP §9.7).
- Red-black tree scheduling data structure (CFS uses rb-tree; our simple list is fine for the simulator).
- Virtual runtime (vruntime) accumulation.
- Niceness mapping (the `prio_to_weight[40]` table).

## Where this leads

- Combine with M13.1 MLFQ for hybrid schedulers (e.g., MLFQ-within-ticket-group).
- The roadmap continues with M13.3 (Multi-CPU scheduling, OSEP Ch. 10).
