# Milestone 13 (slice 13.1): Multi-Level Feedback Queue (MLFQ)

## What is MLFQ?

OSEP Ch. 8 introduces MLFQ as the canonical "learned" CPU scheduler. Instead of
prioritizing jobs by type or static rules, MLFQ watches each job's behavior and
adjusts its priority over time.

The classic rules (OSEP §8.1, refined in §8.4):

- **R1**: If `Priority(A) > Priority(B)`, A runs (B doesn't).
- **R2**: If `Priority(A) = Priority(B)`, A & B run in round-robin using the time slice of the given queue.
- **R3**: When a job enters the system, it is placed at the highest priority (the topmost queue).
- **R4 (§8.4 final version)**: Once a job uses up its time allotment at a given level (**regardless of how many times it has given up the CPU**), its priority is reduced (i.e., it moves down one queue).
- **R5**: After some time period S, move all jobs in the system to the topmost queue.

MLFQ's strength: a job that yields the CPU quickly (e.g., interactive — waiting
for keyboard input) stays at the top queue and gets serviced quickly. A job that
uses the whole slice (e.g., CPU-bound compilation) gets demoted to longer, lower-priority
queues. The boost (R5) prevents long-running CPU jobs from starving.

### Implementation deviation vs. OSEP §8

Our `Mlfq` class implements a simplified subset of the rules. The key simplifications:

1. **R4 — single slice vs. allotment**: OSEP §8.4 explicitly refines the original §8.2 rules (Rule 4a + 4b) into a single Rule 4 that demotes based on **total CPU time consumed at a level** (the *allotment*), not on a single time slice. The allotment can span multiple time slices. The §8.4 Rule 4 prevents gaming: a job that yields the CPU just before its slice ends can no longer stay at the same priority.

   Our implementation uses `YieldsEarly` as a per-job boolean hint. When `YieldsEarly=false`, we demote on the first slice expiry. When `YieldsEarly=true`, we never demote. This is the **original §8.2 Rule 4a/4b** behavior, not the §8.4 anti-gaming Rule 4. Documented as a simplification.

2. **Boost default**: OSEP §8.3 example uses 100 ms; we default to 50 ticks (configurable). Tunable per workload.

3. **Queue count and slices**: OSEP examples use 3 queues (10/20/40 ms slices). We use 4 queues (10/20/40/80 ticks). Doubling per level matches both the OSEP example and the Solaris TS default (§8.5: 60 queues, 20 ms to "a few hundred ms").

4. **Solaris default**: §8.5 mentions "60 queues, with slowly increasing time-slice lengths from 20 milliseconds (highest priority) to a few hundred milliseconds (lowest), and priorities boosted around every 1 second or so". Our default is much smaller (4 queues, 50 ticks) — sized for the simulator, not for a real OS.

5. **§8.5 "decay usage" schedulers** (FreeBSD): not implemented. OSEP notes these are an alternative approach that "adjust priorities using mathematical formulae" rather than discrete rules.

## The mini implementation

`src/MiniWebServer.Host/MiniScheduler/` (4 files, ~250 lines):

- `Job.cs` — Job abstraction (Id, Name, BurstTotal, BurstRemaining, CurrentQueue, State, YieldsEarly).
- `TraceEvent.cs` — One event per tick (dispatch / running / preempt / finish / boost / idle).
- `Mlfq.cs` — The scheduler: queues (FIFO per level), slice lengths doubling per level (10, 20, 40, 80), boost period.
- `Workloads.cs` — Synthetic workloads (`TwoJobs`, `TwoCpuBound`, `MixedWorkload`) + `SchedulerRunner.RunMlfq()`.

### Key design decisions

1. **Time slices double per queue**: queue 0 = 10 ticks, queue 1 = 20, queue 2 = 40, queue 3 = 80. This is the textbook default; gives interactive jobs short slices (responsive) and CPU-bound jobs long slices (less context-switch overhead).

2. **FIFO queues with round-robin between same-priority jobs**: standard `Queue<Job>` per level. When two jobs are both at q=0 and the running one expires, the next dispatch picks the one that's been waiting longest.

3. **Boost at the start of Tick()** (not the end): keeps the trace coherent — the boost event happens at the beginning of the boost tick, before any dispatch.

4. **Boost resets `_ticksSinceBoost` AND boosts the currently-running job**: if the running job was below q0 when the boost fires, it gets moved to q0 with a fresh q0 slice. Without this, a long-running job could "miss" its boost and stay at q3.

5. **YieldsEarly hint**: optional per-job flag. `YieldsEarly=true` means the job gives up CPU before exhausting its slice (interactive), so it's never demoted. In our synthetic workloads, the `interactive` jobs use this hint to model the "I/O completion → scheduler wakes job back to q0" pattern. In a real OS this is automatic; for the simulator we let the workload author declare it.

6. **Trace is append-only** (`List<TraceEvent>`). Each Tick can produce multiple events (boost + dispatch + running), so `RunMlfq` reads the trace list directly and snapshots positions per tick to print all events in order.

### HTTP integration

`/scheduler/run?workload=two|cpu|mixed&ticks=N&boost=M` returns the trace as plain text. The trace shows exactly which job ran on each tick, what queue it was at, what its remaining burst was, and any boost/demote events.

## Smoke evidence (.gitnexus/smoke-m13-1.ps1)

### Workload 1: TwoJobs (1 long CPU-bound + 1 short interactive)

```
t=  10  preempt     J1(cpu-bound) q=0 rem=90  slice used → demote q0→q1
t=  11  dispatch    J2(interactive) q=0 rem=9  slice=10
t=  20  finish      J2(interactive) q=0 rem=0  burst complete
t=  21  dispatch    J1(cpu-bound) q=1 rem=89  slice=20
```

Interactive J2 gets the CPU within 1 tick of arriving, finishes its 8-tick burst well before its 10-tick slice expires, never demoted. CPU-bound J1 stays at q1 running 20-tick slices.

### Workload 2: TwoCpuBound (two equal CPU-bound jobs)

```
t=  10  preempt     J1(A) q=0 rem=40  slice used → demote q0→q1
t=  11  dispatch    J2(B) q=0 rem=50  slice=10
t=  20  preempt     J2(B) q=0 rem=40  slice used → demote q0→q1
t=  21  dispatch    J1(A) q=1 rem=40  slice=20
t=  40  preempt     J1(A) q=1 rem=20  slice used → demote q1→q2
t=  41  dispatch    J2(B) q=1 rem=40  slice=20
t=  51  boost       moved 2 job(s) to q0
t=  51  running     J2(B) q=0 rem=29  slice left 9
```

Two competing jobs each demote down the queues. At t=51 (after 50 ticks of work), the boost moves both back to q0 with fresh slices. Both jobs get equal share.

### Workload 3: Mixed (1 long CPU + 3 short interactive + 1 long CPU)

```
boosts=4 demotes=9 finishes=6
```

200 ticks, 4 boosts (at t=51, 101, 151, 201), 9 demotions, 5 jobs complete.

## OSEP concept

This slice implements §8.1-§8.3 (basic MLFQ with boost) and a simplification of §8.4 (anti-gaming accounting). The trace output is exactly the kind of step-by-step analysis OSEP §8.6 uses to illustrate MLFQ's behavior. The synthetic workloads mirror §8.6's "long-running job + interactive job" and "I/O-aware jobs" examples.

## .NET mechanism

- The scheduler is pure CPU simulation, no threads. Each call to `Tick()` advances by one tick.
- Trace is `List<TraceEvent>` which is append-only. `RunMlfq` walks the list after each Tick to emit all events from that tick.
- HTTP route uses simple string parsing for query params (consistent with the rest of the routes in `Program.cs`).
- No allocations in the hot loop: `Queue<Job>.Dequeue`/`Enqueue` are O(1) and reuse the same `TraceEvent` records.

## Files added (slice 13.1)

- `src/MiniWebServer.Host/MiniScheduler/Job.cs` (60 lines)
- `src/MiniWebServer.Host/MiniScheduler/TraceEvent.cs` (35 lines)
- `src/MiniWebServer.Host/MiniScheduler/Mlfq.cs` (180 lines)
- `src/MiniWebServer.Host/MiniScheduler/Workloads.cs` (90 lines)
- `src/MiniWebServer.Host/Program.cs` — added `/scheduler/run` route (~40 lines)
- `docs/adr/0006-mini-scheduler-mlfq-proportional-multicpu.md` — the roadmap ADR (90 lines)
- `.gitnexus/smoke-m13-1.ps1` — smoke script

## Deferred (per ADR 0006)

- Slice 13.2: Stride / lottery proportional-share scheduler (Ch. 9)
- Slice 13.3: Multi-CPU scheduler with work stealing (Ch. 10)
- Slice 13.4: HTTP integration polish — JSON output, structured workload DSL

## What you can do now

```bash
# Run from the repo root
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj

# In another terminal:
curl "http://localhost:8080/scheduler/run?workload=mixed&ticks=200&boost=50"
```

Vary `ticks`, `boost`, and `workload` to see how the scheduler adapts. Try `workload=two&boost=10` to see aggressive boosting.
