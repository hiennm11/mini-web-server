# Slice 35.1: FIFO, SJF, STCF, Round Robin

> **What it does** — runs the four Ch. 7 baseline policies over a workload, reports the
> chapter's two metrics, and prints all four policies side by side so §7.10's trade-off is
> visible in one response.

## Route

```
/scheduler/run?algo=baseline&policy=fifo|sjf|stcf|rr
               &workload=convoy|latearrivals|equal|response
               &quantum=K          # Round Robin time slice, default 1
```

Each response reports the requested policy's per-job timings, its two averages, and a
comparison table of all four policies on the same workload. An unknown policy or workload
answers 400; `quantum=0`, or a quantum on a run-to-completion policy, answers 400.

## The chapter's own numbers

Every expectation below is a figure OSTEP prints, not one this simulator produced. That
is the only oracle worth having: a test that recomputes the metric the way the code does
would pass by construction. Three exceptions are marked where they occur — the response
value on the equal-length workload, the `AvgWait` figures, and FIFO's turnaround on the
5s workload are derived or are project contracts rather than chapter averages, and each
says so.

| § | Workload | Policy | Figure | Chapter says | Simulator |
|---|---|---|---|---|---|
| §7.3 fig 7.1 | 3×10s, all at t=0 | FIFO | avg turnaround | "(10+20+30)/3 = 20" | 20.00 |
| §7.3 fig 7.2 | A=100, B=C=10 | FIFO | avg turnaround | "a painful 110 seconds" | 110.00 |
| §7.4 fig 7.3 | A=100, B=C=10 | SJF | avg turnaround | "reduces average turnaround from 110 seconds to 50" | 50.00 |
| §7.4 fig 7.4 | A=100@0, B=C=10@10 | SJF | avg turnaround | "103.33 seconds" | 103.33 |
| §7.5 fig 7.5 → §7.6 | A=100@0, B=C=10@10 | STCF | avg turnaround | "50 seconds" | 50.00 |
| §7.6 (on fig 7.5's schedule) | A=100@0, B=C=10@10 | STCF | avg response | "0 for job A, 0 for B, and 10 for C (average: 3.33)" | 3.33 |
| §7.7 fig 7.6-7.7 | 3×5s, all at t=0 | SJF | avg response | "(0+5+10)/3 = 5" | 5.00 |
| §7.7 fig 7.7 | 3×5s, all at t=0, q=1 | RR | avg response | "(0+1+2)/3 = 1" | 1.00 |
| §7.7 | 3×5s, all at t=0, q=1 | RR | avg turnaround | "A finishes at 13, B at 14, and C at 15, for an average of 14" | 14.00 |

## Measured: all four policies per workload

```
workload=convoy        (§7.3 fig 7.2 — one long job ahead of two short ones)
policy | avg turnaround | avg response | order
FIFO   |         110.00 |        70.00 | ABC
SJF    |          50.00 |        10.00 | BCA
STCF   |          50.00 |        10.00 | BCA
RR     |          59.67 |         1.00 | BCA
```

```
workload=latearrivals  (§7.4-§7.5 — B and C arrive while A is running)
policy | avg turnaround | avg response | order
FIFO   |         103.33 |        63.33 | ABC
SJF    |         103.33 |        63.33 | ABC
STCF   |          50.00 |         3.33 | BCA
RR     |          59.67 |         1.00 | BCA
```

This is the workload where the three shortest-job policies separate. FIFO and SJF tie at
103.33 because SJF is non-preemptive: it cannot act on a job that arrives after A has
started. STCF halves the turnaround by preempting. §7.4 explains the tie — "even though
B and C arrived shortly after A, they still are forced to wait until A has completed".

```
workload=equal          (§7.3 fig 7.1 — three jobs of 10s)
policy | avg turnaround | avg response | order
FIFO   |          20.00 |        10.00 | ABC
SJF    |          20.00 |        10.00 | ABC
STCF   |          20.00 |        10.00 | ABC
RR     |          29.00 |         1.00 | ABC
```

```
workload=response       (§7.6-§7.7 — three jobs of 5s)
policy | avg turnaround | avg response | order
FIFO   |          10.00 |         5.00 | ABC
SJF    |          10.00 |         5.00 | ABC
STCF   |          10.00 |         5.00 | ABC
RR     |          14.00 |         1.00 | ABC
```

On equal-length jobs all three shortest-job policies are identical, because "shortest
remaining" is a tie and every tiebreak picks the same job. That is not a bug in the
simulator; it is §7.4's assumption 1 doing its job — SJF only differs from FIFO once
run-times differ, which is why §7.3 relaxes assumption 1 immediately after figure 7.1.

## The trade-off, measured

Two facts hold across all four workloads:

- **RR always has the best response** (1.00 on every one of them).
- **RR always loses on response.** Its response is worse than every other policy's on
  every workload, by a wide margin.

What does *not* hold universally is any single ordering on turnaround. §7.10's summary —
"The first runs the shortest job remaining and thus optimizes turnaround time; the
second alternates between all jobs and thus optimizes response time. Both are bad where
the other is good" — is a statement about *families of policies*, and the data shows why
it needs qualifying: within the shortest-job family, **only STCF** beats RR on turnaround,
and non-preemptive SJF does not.

On the `latearrivals` workload SJF averages 103.33 against RR's 59.67 — SJF is the *worst*
policy there, because it is non-preemptive and cannot react to B and C arriving at t=10.
§7.4 says so directly: "even though B and C arrived shortly after A, they still are forced
to wait until A has completed, and thus suffer the same convoy problem". STCF, the same
family with preemption added, averages 50.00 and beats RR.

So the precise statement is: **STCF beats RR on turnaround and RR beats STCF on
response.** For SJF the turnaround comparison is workload-dependent, because SJF's
advantage is conditional on everything arriving at once — which is §7.1 assumption 2, and
the assumption §7.5 exists to relax.

### One correction to the chapter's wording

§7.7 says Round Robin is "nearly pessimal, even worse than simple FIFO in many cases".
"In many cases" is load-bearing. On the 3×5s workload the claim holds — FIFO averages 10
and RR averages 14 — but on the convoy workload FIFO averages 110 and RR averages 59.67,
so RR *beats* FIFO. The chapter's claim is about the general tendency, not an identity,
and a test that asserted "RR > FIFO" as a universal would fail on half the workloads here.
The tests pin the ordering per workload rather than asserting a universal.

## Tests

Ten, all in `tests/MiniWebServer.Host.Tests/Program.cs`:

- `fifo reproduces the chapter's equal-length example` — fig 7.1's 20.00, plus the
  response figure derived from that figure's own bars (A first runs at 0, B at 10, C at
  20). The chapter prints no response number for this workload, and the test says so
  rather than presenting the value as quoted.
- `fifo reproduces the chapter's convoy example` — fig 7.2's 110.00.
- `sjf halves the convoy and beats fifo on turnaround` — fig 7.3's 50.00 against the
  same workload's 110.00, and completion order BCA.
- `stcf preempts the long job for late arrivals` — §7.4's 103.33 for SJF and §7.5's 50.00
  for STCF on the same workload.
- `round robin trades turnaround for response` — §7.7's 1.00 and 14.00, plus both
  directions of the trade-off **on that workload only** (three 5s jobs). The trade-off is
  not asserted across all workloads because it does not hold there: see the
  `latearrivals` table above, where non-preemptive SJF is worse than RR on turnaround.
- `a longer quantum trades response for turnaround` — response 1.00 / 5.00 / 20.00 at
  quantum 1 / 5 / 25, and RR degenerating to FIFO's numbers at quantum 100. This test
  exists because the suite previously only ever used quantum 1, which is why the route
  could ignore the parameter for a whole review cycle without a failure.
- `baseline policies do not mutate the caller's workload` — running two policies on one
  `List<BaselineJob>` must give the same answer twice. The simulation decrements
  `Remaining` as it runs, so sharing the input list would make the second run see every job
  already spent.
- `baseline scheduler rejects inputs the chapter's model cannot express` — quantum 0
  (§7.7: a slice below one tick is not a slow quantum, it is an unrunnable scheduler), a
  quantum on a non-RR policy (silently measuring RR while reporting FIFO), an empty
  workload, and a zero-length or negatively-arriving job (§7.1).
- `baseline result carries arrivals by name, not by position` — regression for the
  index-mismatch bug below.
- `baseline scheduler rejects a horizon it cannot represent` — regression for the
  overflow bug below.

## Bugs found while building this

Eight in total, with a ninth found by the regression test written for the eighth. The
first four were found by the tests as each was written; the next four by review *after* the
suite was green; the last by the test written to pin the eighth.

**Found by the tests:**

1. **The simulation stopped after one tick.** The loop condition asked whether any job was
   still *waiting to arrive*, but admission drains that list into the run queue — so after
   the first tick the list was empty and the run ended with every other job's
   `CompletionAt` still -1. The symptom was an average turnaround of -1. Termination is
   "some job has not completed", and those are different predicates.

2. **`Summarize` averaged an empty sequence.** It was passed the same drained list the
   admission loop consumed. The metrics need every job, not only the ones that happened to
   be waiting at the end. Two lists: one drains, one is kept for reporting.

3. **The guard limit read `Max` off the drained list.** `Enumerable.Max` throws on an
   empty sequence — the same bug as above, one level up, in the line that computes the
   iteration bound. Guarding one `Max` and not the other is how this survived the first
   fix.

4. **STCF did not preempt.** It chose a job only when the CPU was idle, which is SJF with
   extra steps. §7.5 re-decides *when a new job enters the system*: the arriving job must
   be compared against the incumbent's remaining time, and the incumbent returned to the
   queue if it loses. This is the one bug the tests caught by asserting a number (103.33
   where 50.00 was expected) rather than by structure — SJF and STCF are indistinguishable
   on the equal-length and response workloads, so only the late-arrivals case exposes it.

**Found by review, with the suite already green:**

5. **The comparison table hardcoded RR's quantum at 1.** Every response reported all four
   policies with quantum 1 regardless of what the caller passed, so `?policy=rr&quantum=25`
   showed the requested policy's own numbers at the top and a *different* set in the
   comparison table below, both labelled "requested". Nothing failed because no test used
   a quantum other than 1 — the coverage gap and the bug had the same shape.

6. **Fixing 5 introduced a 400.** Passing the caller's quantum to all four rows made
   `Run` reject the quantum on FIFO, SJF and STCF, and the route's `catch` turned the
   formatter's exception into a 400 — discarding a run that had already succeeded. The
   quantum now reaches the RR row only. This one was visible in the smoke output as an
   empty body at `quantum=25`, which the first smoke pass walked past.

7. **The per-job table paired the wrong job's numbers.** Job names came from the trace's
   *execution* order and indexed the result's *input*-ordered response list. On the convoy
   workload SJF executes B, C, A, so B was rendered with A's response — a job that started
   at t=0 displayed as having waited 70. The aggregates were right; only the table was
   wrong, which is why no aggregate test caught it. `ScheduleResult.Arrivals` is now
   keyed by name and the renderer joins on that.

8. **The iteration guard overflowed.** One job of length `int.MaxValue` arriving at 0
   makes `Sum + Max + 1` wrap negative in `int`, so the run failed its own guard with
   "simulation failed to terminate" — rejecting a workload whose answer fits exactly in
   `int`. Two large jobs overflow `Enumerable.Sum` first. The bound is computed in `long`
   and the range is rejected explicitly with a message naming the horizon.

9. **Fixing 8 chose the wrong horizon.** The replacement limit was `int.MaxValue`, which is
   correct for *time* and wrong for *memory*: `Trace` records one entry per tick, so the
   regression test's own workload threw `OutOfMemoryException` before the guard could fire.
   Same mistake one level up — bounding a quantity whose cost is not the quantity. The
   horizon is now an explicit 1,000,000-tick cap, and the message says the trace costs one
   entry per tick so the reason is legible from the error.

## Deviations from OSEP

- **Ticks, not seconds.** The chapter's figures are in seconds; the simulator counts ticks.
  Every printed figure is reproduced exactly, which is the check that nothing was lost.
- **Known run-times.** §7.1's fifth assumption, kept deliberately: SJF and STCF are defined
  in terms of remaining time, and removing the oracle is §7.9's problem, answered by §8.
- **No context-switch cost.** §7.7's amortisation TIP (10 ms quantum, 1 ms switch → ~10%
  wasted) is quoted in the route output, not computed. Charging a cost per switch would
  change the quantum's optimal value, which is a lesson worth having, but it needs a
  second constant the chapter introduces only inside a TIP.
- **Fairness is not computed.** §7.2 names Jain's Fairness Index; it is not implemented.

## Source documents

- `docs/learning/m35-scheduling-baselines/overview.md` — milestone scope.
- `docs/adr/0025-remaining-coverage-gaps-m35-m36-m37.md` — why this is a new milestone
  rather than an M13 extension.
- `docs/learning/m13-mlfq/overview.md` — the successor that exists to escape these.
- OSTEP Ch. 7 §7.1 assumptions, §7.2 metrics, §7.3 FIFO, §7.4 SJF, §7.5 STCF,
  §7.6 response time, §7.7 Round Robin, §7.10 summary.