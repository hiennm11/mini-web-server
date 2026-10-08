# Slice 33.1: Scrubbing Schedule (Ch. 45 §45.7 + §45.8)

> **What it does** — adds the scheduling half of OSEP §45.7 on top of M27's one-shot scrubber: a pass that covers a bounded batch and resumes where it stopped, a repeatable schedule, and the probability that a policy catches a corruption before the next one masks it.

## Surface

- `Scrubber(IntegrityStore, batchSize)` — incremental sweep.
  - `ScrubBatch()` verifies the next `BatchSize` blocks, advancing a cursor that **wraps at the end of the disk** and survives between passes.
  - `Cursor`, `PassesCompleted`, `BlocksScrubbed` — what the sweep has actually done.
  - `Schedule(interval, throttle)` — background loop on the thread pool. `Stop()` **joins** the worker, so nothing scrubs after it returns. `IsRunning` reports the task's real state.
- `CatchProbability` — `OfSweepPeriod(T, MTTF)` and `SweepPeriodHours(interval, blocks, batch)`. Derived, not quoted; the derivation is in the doc comments.
- `ChecksumOverhead.SpacePercent(checksumBytes, blockBytes)` — §45.8's space figure as a function.

## Route

```
/integrity/run?scenario=scrub-schedule&blocks=N&interval_hours=H&block_mtbf_hours=M
/integrity/run?scenario=scrub-sweep&blocks=N&batch_size=B&faults=F
/integrity/run?scenario=checksum-overhead
```

Non-positive `interval_hours`/`block_mtbf_hours`, a non-positive or oversized `batch_size`, and a negative `faults` answer 400.

## Measured results

`scenario=scrub-schedule&blocks=1000&block_mtbf_hours=100000`:

| schedule | interval | batch | sweep period | P(catch before masked) |
|---|---|---|---|---|
| whole disk | 24.0 h | 1000 | 24.0 h | **99.976%** |
| 168h, whole disk | 168.0 h | 1000 | 168.0 h | **99.832%** |
| quarter of the disk | 24.0 h | 250 | 96.0 h | **99.904%** |
| 1% of the disk | 24.0 h | 10 | 2400.0 h | **97.629%** |

The last row is the trade-off in one line. Bound how much work a pass does and each block waits proportionally longer between visits. The higher-interval row is worse than the lower-interval one, which is the direction §45.7's two cadences imply: scanning less often means a longer time before any given block is rechecked.

`scenario=scrub-sweep&blocks=12&batch_size=4&faults=3`:

```
pass | cursor before | blocks | ok | bad | found
-----|---------------|--------|----|-----|------
   1 |             0 |      4 |  3 |   1 | 0
   2 |             4 |      4 |  3 |   1 | 4
   3 |             8 |      4 |  3 |   1 | 8
```

Three faults, three different passes, twelve blocks covered exactly once.

## The catch probability, derived

OSEP gives no formula, so this is derived and the derivation is recorded.

A latent corruption appears in a block at some instant. The scrubber revisits that block every `T` hours. Errors affecting one block arrive as a Poisson process of rate `lambda = 1/MTTF`, so the gap `G` to the next corruption of the same block is exponential with mean `MTTF`.

The corruption is caught exactly when a scrub visit lands before that next corruption masks it — i.e. when `G > T`. For an exponential gap:

$$P(\text{caught}) = P(G > T) = e^{-T/\mathrm{MTTF}}$$

The limits are the sanity check: `T → 0` gives 1 (a scrub right after the fault always catches it), `T → ∞` gives 0 (nothing is ever caught in time). So the quantity rises with sweep frequency, which is exactly the pressure §45.7 applies.

## The sweep period

$$\text{sweep period} = \text{interval} \times \left\lceil \frac{N}{B} \right\rceil$$

The number of passes to sweep the disk, times the interval between passes. Batch size appears because a smaller batch means more passes, and therefore a longer wait for any individual block.

## Bugs found while building this

1. **The sweep period formula was inverted.** The spec's `interval × totalBlocks / batchSize` makes a *larger* batch produce a *longer* period, which is backwards — covering more of the disk per pass shortens it. See ADR 0023.
2. **`Stop()` cleared the worker reference before joining**, so `IsRunning` reported "stopped" while the loop was still winding down. The reference is now held until the join completes.
3. **The thread-count assertion was not a leak test.** `Process.Threads.Count` did not move when the `Wait` was removed from `Stop()`, because the thread pool reuses threads and `WaitHandle.WaitOne` returns on cancellation immediately. The test now asserts the observable outcome — nothing scrubs after `Stop` returns — which is what actually matters.

A second arithmetic slip was mine rather than the spec's: an early test assumed a corruption at block 7 of 10 was found on the third pass of size 4, when it is found on the second (passes cover 0–3, then 4–7).

## Bugs found by review, after the slice's own tests were green

4. **The "weekly" row was a seven-*hour* row.** The candidate was built as `intervalHours * 7 / 24`, so with the default 24-hour interval it produced 7 h — a scan *seven times more frequent* than nightly, labelled "weekly". It then scored a *higher* catch probability than the nightly row, inverting the comparison §45.7's "nightly or weekly" sets up. It is `intervalHours * 7`.
5. **`Schedule`/`Stop` were not atomic.** Each field access was locked, but the stop-start-publish sequence was not: two concurrent `Schedule` calls could both finish stopping before either published, orphaning the first worker — still scrubbing, unreachable to any later `Stop`. A lifecycle lock now covers the whole sequence, separately from the sweep lock so the join never blocks the worker it waits for.
6. **`Stop` reported success when its join timed out.** `Task.Wait` returns a bool that was being ignored, so a worker still inside a batch would have its reference cleared and its `CancellationTokenSource` disposed while it was running. `Stop` now keeps ownership and throws `TimeoutException` instead of claiming it stopped.
7. **Durations the timer cannot express were accepted.** `WaitHandle.WaitOne(TimeSpan)` takes whole milliseconds bounded by `int.MaxValue`, so a sub-millisecond interval became a zero-length wait (a spin loop) and a 30-day interval faulted the worker. Both are now rejected at `Schedule`, where the caller can see why.
8. **Non-positive `blockSize` and NaN parameters closed the connection instead of answering 400.** `blockSize=0` threw inside a constructor; `blockSize=1` threw because the fixed two-byte seed did not fit; `interval_hours=NaN` passed a `<= 0` check and threw downstream; `interval_hours=1e309` printed a table of infinities. All now answer 400, and the sweep's seed is sized to the block.

Two of these — the lifecycle race and the interval range — have mutation-checked tests. The `Stop` timeout is not: `Task.Wait` almost always completes promptly after cancellation, so the timeout branch is hard to reach deliberately.

## Tests

- `scrubber covers a batch per pass and resumes where it stopped` — pass sizes 4, 4, 4 over a 10-block disk with the cursor wrapping; the fault at block 7 surfaces on pass 2, and 16 blocks are read over four passes.
- `scrub catch probability follows exp(-T/MTTF) and is monotone in T` — equals 1 at `T = 0`, matches `exp(-24/100000)` at a known point, strictly decreases as `T` doubles up, and returns 1 for an infinite MTBF.
- `scrub schedule derives the sweep period from interval and batch size` — 96 h for a quarter-disk batch, 24 h for a whole-disk batch, and a smaller batch lowers the per-block catch probability.
- `checksum overhead matches the §45.8 worked figure` — the printed 0.19% and the exact `8/4096 = 0.1953125%` are both asserted, since the chapter rounds.
- `scrubber schedules and stops cleanly without leaking the worker` — the schedule runs, `Stop()` returns with `IsRunning` false, and the pass counter does not move afterwards.
- `scrubber does not scrub while stopped` — a scrubber that was never scheduled performs no passes and leaves the cursor at zero.
- `scrubber rejects intervals the timer cannot express` — a one-tick interval, a 30-day interval and a 30-day throttle all throw at `Schedule`, and a rejected schedule leaves nothing running.
- `concurrent Schedule and Stop calls cannot orphan a worker` — four threads run 25 lifecycle calls each; no exception escapes, nothing is running after the final `Stop`, and the pass counter is stable afterwards.

Five of these were mutation-checked (cursor reset per pass, inverted probability, dropped percentage conversion, removed lifecycle lock, dropped interval validation). The `Stop()` join and the join-timeout branch are not — see bug 6 and the note above.

## Two corrections to the original spec

1. **The sweep-period formula was wrong**, as above.
2. **The "Poisson-arrival model" was attributed to §45.8; OSEP states no probability model at all.** The model here is derived, with the derivation recorded in the code and in ADR 0023, so a reader can disagree with it rather than having to reverse it.

A third, smaller one: the spec described a `LogBuffer`-style throttle as the main lever. It matters, but the batch size is the lever that changes the *coverage* of a block, and that is what the route reports.

## What this slice does NOT do

- **Repair on detect.** A corrupted block is reported, not reconstructed from a redundant copy. That would combine M27 with M24's RAID.
- **Non-uniform error rates.** §45.1 notes that LSEs cluster ("Most disks with LSEs have less than 50" bad sectors); the model here assumes a uniform MTTF across blocks, which understates what a scrubber finds when it hits a bad region.
- **I/O priority.** The throttle is a timed wait, not `ionice`-class scheduling.
- **ZFS-style end-to-end checksums** (parent pointers in inodes), which M27 already deferred.

## Source documents

- `docs/learning/m27-integrity/overview.md` — predecessor one-shot scrubber.
- `docs/adr/0023-m33-scrubbing-schedule.md` — decision record.
- OSEP Ch. 45 §45.7 — periodic scrubbing, nightly-or-weekly.
- OSEP Ch. 45 §45.8 — space and time overheads, the 8-byte-per-4 KB figure.