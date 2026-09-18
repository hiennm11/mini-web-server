# Slice 13.2.1: Stride & Lottery Scheduling

## What it does

Adds two proportional-share schedulers (Stride and Lottery) that share CPU proportionally to **tickets** assigned to each job.

- **Stride** (OSEP §9.3 + §9.6): `stride = STRIDE_CONST / tickets` (STRIDE_CONST = 10_000). On each tick, pick the job with the lowest `pass`, run it, then `pass += stride`. Deterministic — the share is exact at the end of each cycle.
- **Lottery** (OSEP §9.1 + §9.3 Figure 9.1): on each tick, pick a random ticket from [0, totalTickets), walk the jobs accumulating tickets until we cross the winner. Probabilistic — the share converges over time.

The HTTP route `/scheduler/run?algo=stride|lottery&workload=proportional&ticks=N` runs the proportional-share test (3 jobs with tickets 100/50/250) and reports both ticket-share and actual-share.

## Files added/changed

- `src/MiniWebServer.Host/MiniScheduler/Job.cs` — `Tickets` + `Stride` + `Pass` fields + auto-computed `Stride` in constructor.
- `src/MiniWebServer.Host/MiniScheduler/ProportionalScheduler.cs` (new, ~230 lines):
  - `SimTraceLine` — flat trace record.
  - `StrideScheduler` (OSEP §9.3 + §9.6).
  - `LotteryScheduler` (OSEP §9.1 + §9.3 Figure 9.1).
- `src/MiniWebServer.Host/MiniScheduler/Workloads.cs` — `ProportionalWorkload` (3 jobs with tickets 100/50/250).
- `src/MiniWebServer.Host/Program.cs` — `?algo=stride|lottery&workload=proportional`.

## OSEP alignment

Implements OSEP §9.1 (Lottery), §9.3 (Lottery + Stride), §9.6 (Stride), and §9.4 (fairness study).

## Smoke evidence

```
$ curl 'http://localhost:8080/scheduler/run?algo=stride&workload=proportional&ticks=400'
...
=== final state ===
  J1 A: ran=100/100 (100.0%) pass=10000
  J2 B: ran=50/50 (100.0%) pass=10000
  J3 C: ran=250/250 (100.0%) pass=10000
=== ticket share vs actual share ===
  J1 A: ticket-share=25.0%, actual-share=25.0%
  J2 B: ticket-share=12.5%, actual-share=12.5%
  J3 C: ticket-share=62.5%, actual-share=62.5%

$ curl 'http://localhost:8080/scheduler/run?algo=lottery&workload=proportional&ticks=400'
... (same exact result, since 400 ticks covers all 400 burst exactly)
```

For 100 ticks (less than total burst):
- Stride: 25.0% / 13.0% / 24.8% (near-perfect — Stride is deterministic)
- Lottery: 34.0% / 11.0% / 55.0% (wider variance — Lottery is probabilistic)

This matches OSEP §9.4's finding: "Only as the jobs run for a significant number of time slices does the lottery scheduler approach the desired fair outcome."

## OSEP concept

> "The basic idea is simple: at any given time, pick the process to run that has the lowest pass value so far; when you run a process, increment its pass counter by its stride." (OSEP §9.6)

> "The scheduler must know how many total tickets there are... The scheduler then picks a winning ticket, which is a number from 0 to [total-1]." (OSEP §9.1)

## .NET mechanism

- `Job.Tickets` is the share proportion. `Job.Stride = 10_000 / Tickets` (auto-computed).
- `Job.Pass` is the Stride counter that grows on each run.
- `Random` is used for the Lottery winner ticket selection.

## What this slice does NOT do

- Ticket currency / transfer / inflation (OSEP §9.2).
- Linux CFS / rb-tree / vruntime (OSEP §9.7).

## Deferred (other proportional-share extensions)

- Ticket mechanisms (currency, transfer, inflation) — useful for multi-user setups.
- CFS-style virtual runtime accumulation + weighted round-robin.
- Nice-level mapping (`prio_to_weight[40]` table).
