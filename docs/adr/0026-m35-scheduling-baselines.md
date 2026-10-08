# ADR 0026: M35 Scheduling Baselines as a Separate Milestone from M13 (OSEP Ch. 7)

## Status

Accepted

## Date

2026-10-08

## Context

ADR 0025 named Ch. 7 as M35 and decided it would be a new milestone rather than an extension of M13. This records how it was built and why the shape held.

### Why the baselines had to be built at all

M13.1, M13.2 and M13.3 implement MLFQ (§8), lottery/stride (§9) and multi-CPU scheduling (§10). Every one of them is presented by OSTEP as an *improvement*, and each improvement is defined against something in Ch. 7:

- §8's priority boost exists because STCF starves long jobs. Nothing in this repo could starve.
- §8.4's I/O-awareness exists because §7.8's CPU-only assumption is wrong. Nothing here assumed anything about I/O.
- §8.5's anti-gaming rules exist because §7.1's "run-time is known" assumption makes the scheduler omniscient. The M13 simulator grants that oracle unconditionally and never says so.

So the three scheduling milestones state advantages that nothing in the repo can reproduce. The gap is not cosmetic: a reader who wants to know *why* MLFQ helps cannot run the alternative.

### Why not a slice of M13

Three reasons, in order of how much they mattered:

1. **The job models do not fit.** `Job.cs` carries `CurrentQueue` (§8) and `Tickets` (§9) and has no arrival time. Ch. 7's response time is defined against arrival (§7.6, eq. 7.2: `T_response = T_firstrun − T_arrival`). Reusing `Job` means either a nullable arrival on a struct most M13 call sites construct, or a second type anyway.

2. **The run shape differs.** `RunMlfq(jobs, ticks, ...)` simulates a fixed tick budget and reports what happened during it. Turnaround time is only defined at completion. Running a Ch. 7 policy for a tick budget and reporting the averages would be reporting a partial run's numbers as if they were final — the failure mode being most subtle when the budget is generous and almost every job finished.

3. **The metric differs.** §7.6 introduces response time. No M13 milestone reports it, and MLFQ's rules are designed against it. Adding it to `ScheduleResult`-shaped output would either change M13's existing output or fork it.

Merging would have meant a refactor of working, tested code to serve a chapter that had not been started. M31–M33 set the opposite precedent: extend an existing milestone only when the new work is one § of code that already exists.

## Decision

A separate milestone with its own job type, its own run loop, and its own route branch.

### Two job types, deliberately

`BaselineJob` has `Name`, `Length`, `Arrival`, `Remaining`, `FirstRunAt`, `CompletionAt`. `Job` keeps its queue level and tickets. Neither is a generalisation of the other: they share a name and nothing else. `Job.cs`'s existing header already scopes itself as "OSEP §8.2 / §8.4 Job abstraction", which is the reason — the two types are the two chapters' models, and collapsing them would hide which chapter a number came from.

### The route compares all four policies, always

`/scheduler/run?algo=baseline&policy=…` reports the requested policy *and* a table of all four on the same workload. A single policy's numbers are not interpretable: SJF's 50.00 means nothing without FIFO's 110.00 next to it. §7.10's trade-off is only visible as a comparison, so the comparison is part of every response rather than a second scenario.

The comparison table rebuilds its workload from the same `BaselineWorkload.FromName` the requested run used. Two switches over the same workload names would drift, and the drift would show up as a comparison across two different workloads — the most misleading output the route could produce.

### STCF preempts on the arrival edge only

The chapter's wording is precise: "Any time a new job enters the system, the STCF scheduler determines which of the remaining jobs (including the new job) has the least time left". So STCF compares the incumbent against arrivals and returns the loser to the queue. It does **not** re-decide every tick — that would be Round Robin with a quantum of 1, a different policy that happens to have a similar name.

### Known run-times kept, and labelled

§7.1's fifth assumption is kept: "the run-time of each job is known ... this would make the scheduler omniscient". SJF and STCF are *defined* in terms of remaining time, so removing the oracle removes the policies rather than improving them. §7.9 names the problem and hands it to §8, which this repo has.

## Consequences

### Positive

- **§7.3-§7.7 are runnable**, and every figure the chapter prints is reproduced exactly: 20.00, 110.00, 50.00, 103.33, 3.33, 1.00, 14.00. The tests assert those numbers, so the simulator cannot drift from the chapter without failing.
- **The trade-off is an inequality, not a sentence.** Across all four workloads RR has the best response and the shortest-job family has the best turnaround. §7.10's "both are bad where the other is good" is now something the route shows rather than states.
- **§7.7's "worse than FIFO" is bounded correctly.** The chapter says "in many cases". RR beats FIFO on the convoy workload (59.67 against 110.00), so the tests pin the ordering per workload instead of asserting a universal.

### Negative

- **Two job models coexist.** A reader who wants to compare Ch. 7 against Ch. 8 cannot hand the same job list to both. That is the honest cost: the two chapters do not share a model, and §7.8 is the place they would start to converge.
- **No I/O, no oracle (§7.8, §7.9).** Both are the *fixes* for what Ch. 7 demonstrates, and both belong to §8. Implementing them here would blur the line between a baseline and the successor.
- **Fairness is named and not computed.** §7.2 cites Jain's Fairness Index; there is no number for it in the route.
- **No context-switch cost.** §7.7's amortisation TIP is quoted, not computed. Charging a per-switch cost would change which quantum is optimal — a real lesson, but it needs a constant the chapter mentions only inside a TIP.

### Neutral

- Nine bugs were found during the build. Four by the tests as each was written; four by
  review *after* the suite was green; and one by the regression test written for one of
  the review findings. Three of the nine were the same mistake at different levels — a list
  that the admission loop drains was used for reporting, for the loop condition, and for
  the iteration guard. Two more were the same mistake about *what bounds memory*: the
  guard overflowed in `int`, and the fix used `int.MaxValue` as the horizon, which the
  per-tick trace then turned into an `OutOfMemoryException`.
- **The quantum bug and its coverage gap had the same shape.** No test used a quantum other
  than 1, so the route could hardcode 1 in the comparison table without failing. The
  regression test for it is the only one that exercises a quantum ≠ 1.

## Verification

- 10 tests. Seven assert figures printed in Ch. 7 rather than recomputing them:
  - fig 7.1's 20.00, plus the response value derived from that figure's own bars
  - fig 7.2's 110.00
  - fig 7.3's 50.00 against 110.00 on the same workload
  - §7.4's 103.33 for SJF against §7.5's 50.00 for STCF
  - §7.7's 1.00 response and 14.00 turnaround, plus both directions of the trade-off
    *on that workload* — the trade-off is not asserted across workloads because
    non-preemptive SJF is worse than RR on `latearrivals`
- Three more are project contracts rather than chapter figures: the workload is not
  mutated by a run; arrivals are keyed by name rather than position; and a horizon the
  simulator cannot represent is rejected with a message naming its cause.
- Smoke: all four policies and all four workloads return 200; the quantum sweep now
  produces distinct results (59.67 / 58.33 / 66.67 / 110.00 turnaround at q = 1 / 5 / 25 /
  100 on the convoy workload); an unknown policy, an unknown workload and `quantum=0`
  return 400; `/scheduler/run?algo=mlfq` is unchanged.
- 108/108 tests pass (98 before M35 + 10).

> **Scope claim corrected by ADR 0029.** This milestone implements §7.3-§7.7, not the whole chapter; Ch. 7 §7.8 is "Tips" and §7.9-§7.10 were read for the hand-off to §8 but not implemented as simulation.

## Source Documents

- `docs/learning/m35-scheduling-baselines/overview.md` — scope and what is deliberately excluded.
- `docs/learning/m35-scheduling-baselines/s1-baseline-policies.md` — the chapter's figures, measured tables, and the four build bugs.
- `docs/adr/0025-remaining-coverage-gaps-m35-m36-m37.md` — why M35 is a new milestone.
- `docs/learning/m13-mlfq/overview.md` — the successor that exists to escape these.
- OSTEP Ch. 7 §7.1, §7.2, §7.3, §7.4, §7.5, §7.6, §7.7, §7.10.