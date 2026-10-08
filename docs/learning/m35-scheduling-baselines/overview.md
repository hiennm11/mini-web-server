# Milestone 35: Scheduling Baselines (Ch. 7)

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

Ch. 8, §9 and §10 all present their schedulers as *improvements*. Improvements on what? What do FIFO, SJF, STCF and Round Robin each do well, and what does each one cost?

## Scope

The baseline policies of OSEP Ch. 7 §7.3–§7.7, run in a simulator and exposed at
`/scheduler/run?algo=baseline&policy=fifo|sjf|stcf|rr&workload=convoy|latearrivals|equal|response&quantum=K`.

- **FIFO / FCFS** (§7.3) — first come, first served. No knowledge of run-time.
- **SJF** (§7.4) — run-to-completion, shortest first. Needs the run-time known.
- **STCF / PSJF** (§7.5) — SJF plus preemption: re-decide on every arrival.
- **Round Robin / time-slicing** (§7.7) — a fixed quantum, then the back of the queue.

Two metrics, both defined by the chapter: **turnaround** (eq. 7.1) and **response** (eq. 7.2).

## Why this is a milestone and not an extension of M13

M13 already built MLFQ (§8), lottery/stride (§9) and multi-CPU (§10). Each is
presented by the chapter as an escape from a Ch. 7 failure, and none of those
failures is reproducible in this repo. §8.4's priority boost exists because STCF
starves long jobs — but STCF is not here to starve one. The advantage is currently
asserted rather than measured.

Three reasons this is separate work rather than a slice of M13:

1. **§7.6 introduces response time.** No M13 milestone reports it. MLFQ's rules are
   designed against exactly this metric, so measuring it changes what MLFQ's
   numbers mean.
2. **The job models differ.** Ch. 7 jobs have an *arrival time*, because response
   time is defined against arrival (§7.6). M13's `Job` has no arrival time — it has
   queue levels and tickets. Unifying them would be a refactor of working code to
   serve a chapter the repo has not started.
3. **The runs are not tick-bounded.** `RunMlfq(jobs, ticks, ...)` simulates a fixed
   number of ticks and reports what happened. A Ch. 7 policy runs until every job
   completes, because turnaround is only defined at completion. Forcing §7 into
   the tick-budget shape would report a partial run's averages as if they were final.

## OSTEP coverage

- **§7.1 Workload assumptions** — equal run-times, simultaneous arrival,
  run-to-completion, CPU-only, known run-times. The simulator takes the first two
  as parameters and the rest as its model. The chapter flags the fifth itself: "the
  run-time of each job is known ... this would make the scheduler omniscient, which,
  although it would be great (probably), is not likely to happen anytime soon."
  Removing that assumption is §7.9's question, and §7.9's answer is §8 — M13.1.
- **§7.2 Scheduling metrics** — turnaround time as the primary metric, plus fairness
  (§7.2 names Jain's Fairness Index). Fairness is named and not implemented; §7.7's
  discussion of RR as "fair" is the qualitative form of it.
- **§7.3 FIFO** — the convoy effect.
- **§7.4 SJF** — optimal for turnaround under §7.1's assumptions; the TIP gives the
  principle's reach beyond OSes ("a ten-items-or-less line").
- **§7.5 STCF** — preemption as the answer to late arrivals. The chapter's ASIDE
  places it: "Virtually all modern schedulers are *preemptive*".
- **§7.6 Response time** — the metric that breaks SJF's advantage.
- **§7.7 Round Robin** — the quantum trade-off and its amortisation TIP.

**Not covered**: §7.8 incorporating I/O, §7.9 the oracle problem. §7.8's fix is to
treat each CPU burst as a job, which is a workload model the M13 simulators already
express (`YieldsEarly`); adding it here would blur the line between a Ch. 7 baseline
and an MLFQ rule. §7.9's answer *is* §8. Deferring both is the point: this milestone
is the baseline, not a second attempt at the fixes.

## Files

- `src/MiniWebServer.Host/MiniScheduler/BaselineScheduler.cs` — `BaselinePolicy`,
  `BaselineJob`, `BaselineWorkload`, `ScheduleResult`, `BaselineScheduler`.
- `src/MiniWebServer.Host/Program.cs` — `/scheduler/run?algo=baseline` branch, and
  `FormatBaselineSchedule`.

## Implementation notes

**Discrete time, one tick per unit of CPU.** The chapter's examples are in seconds
(10 s, 100 s) and its figures are drawn to a time axis; the simulator's unit is
"ticks", which is the same unit at a different scale. Every figure in Ch. 7 is
reproduced exactly, which is the check that the scale change lost nothing.

**`BaselineJob` is separate from `Job`.** The comment in `Job.cs` calls itself the
"OSEP §8.2 Job abstraction" — it carries `CurrentQueue` and `Tickets`, which are §8
and §9 concepts. A Ch. 7 job needs an arrival time instead, and needs nothing else
that `Job` has. Sharing one type would mean nullable queue levels and a `Tickets`
field that is meaningless until §9.

**STCF re-decides only on the arrival edge.** Not every tick. If it re-decided every
tick, it would be Round Robin under a quantum of 1 — a different policy with a
different name, and §7.5 is specifically about reacting to a *new job entering the
system*: "Any time a new job enters the system, the STCF scheduler determines which of
the remaining jobs (including the new job) has the least time left".

**Idle time is jumped, not spun.** When nothing is runnable the clock advances to
the next arrival and the gap is recorded as idle. Response time is measured from
arrival, so a machine with nothing to run must be distinguishable from one that has
not started a job that has already arrived.

## What this slice does NOT do

- **§7.8 I/O** — see above.
- **§7.9 the oracle problem** — M13.1's territory.
- **Jain's Fairness Index** (§7.2) — named in the chapter, never computed. "Fair" in
  §7.7's sense means RR divides the CPU evenly; making that a number needs a second
  metric the chapter does not define.
- **Context-switch cost in the quantum trade-off.** §7.7's TIP says a 10 ms quantum
  with a 1 ms switch wastes ~10% of the CPU. That is an arithmetic result about two
  constants; the simulator reports the metrics, not the overhead, and the TIP is
  quoted in the route output rather than computed.
- **Real timers.** No thread, no timer interrupt, no quantum in wall-clock time.