# ADR 0023: M33 Scrubbing Schedule (OSEP Ch. 45 §45.7 + §45.8)

## Status

Accepted

## Date

2026-10-07

## Context

M27's `IntegrityStore.Scrub()` is one-shot: it walks every block, verifies it, and returns. §45.7 describes something else.

> "By periodically reading through every block of the system, and checking whether checksums are still valid, the disk system can reduce the chances that all copies of a certain data item become corrupted. Typical systems schedule scans on a nightly or weekly basis."

The chapter says *what* scrubbing is for and gives a schedule in units of time — nightly or weekly. It does not say how much work one pass should do, how a partial pass should behave, or what the schedule is worth.

§45.8 supplies the reason the schedule is a trade-off at all:

> "some checksumming schemes can induce extra I/O overheads, particularly when checksums are stored distinctly from the data (thus requiring extra I/Os to access them), and for any extra I/O needed for background scrubbing... The middle of the night, when most (not all!) productive workers have gone to bed, may be a good time to perform such scrubbing activity."

and the space figure: "an 8-byte checksum per 4 KB data block, for a 0.19% on-disk space overhead."

### Two corrections to the milestone spec

**1. `meanTimeBetweenPasses` = `interval × totalBlocks / batchSize` is wrong.** The spec's formula has the sweep getting *longer* the more of the disk a pass covers, so a pass over the whole disk would take `interval × N/B` — a single full-disk nightly scan becoming thousands of intervals. The quantity that matters is the gap between two visits to **one block**, which is `interval × ceil(N / batchSize)`: the number of passes needed to sweep the disk, times the interval between passes. A whole-disk nightly pass gives a one-day period; a 1%-per-pass nightly schedule gives a hundred-day period.

**2. "Poisson-arrival model" was attributed to §45.8; OSEP gives no such model.** The chapter states a probability nowhere. The implementation derives one and says so, rather than citing a formula the book does not contain.

## Decision

### `Scrubber`

Wraps an `IntegrityStore` and adds what the one-shot scan lacks:

- a cursor that **survives between passes** and wraps at the end of the disk. A partial sweep that restarted at block 0 would never reach the tail — this is the whole reason the batch exists.
- `BatchSize` bounds how much work one pass does, which is what §45.8's "can be tuned" is about.
- `Schedule(interval, throttle)` runs a background loop; `Stop()` **joins** the worker rather than only signalling it, so a caller can rely on no background read happening after it returns.

### `CatchProbability`

`OfSweepPeriod(T, MTTF) = exp(-T/MTTF)`, derived rather than quoted. A latent corruption appears at some instant; the scrubber revisits that block every `T`; errors on one block arrive as a Poisson process of rate `1/MTTF`, so the gap `G` to the next corruption of the same block is exponential. The corruption is caught exactly when a scrub visit lands before that next corruption masks it — i.e. when `G > T` — and `P(G > T) = exp(-T/MTTF)`.

The limits are the check: `T → 0` gives 1, `T → ∞` gives 0, so the quantity rises with sweep frequency, which is the pressure §45.7 applies. An infinite MTBF is handled explicitly rather than by dividing, so it returns 1 instead of 0.

`SweepPeriodHours(interval, blocks, batch)` = `interval × ceil(blocks / batch)`.

### `ChecksumOverhead`

§45.8's space figure as a function. The chapter prints 0.19%; the exact ratio `8/4096` is 0.1953125%, so both are asserted.

### Route

Three scenarios on the existing `/integrity/run`, dispatched before M27's store is constructed: `scrub-schedule` (a policy comparison), `scrub-sweep` (a real scrubber over a faulted disk, pass by pass), `checksum-overhead` (§45.8's figures). Non-positive `interval_hours`/`block_mtbf_hours`, a non-positive or oversized `batch_size`, and a negative `faults` answer 400.

## Consequences

### Positive

- **The scheduling half of §45.7 is runnable**, and the cursor makes the failure mode visible: the sweep table shows which pass reaches which block.
- **Batch size has a measured price.** On a 1000-block disk with a 100 000 h MTBF, a 24-hour whole-disk schedule catches 99.976% before masking, a weekly whole-disk schedule catches 99.832%, and a 24-hour schedule covering 1% per pass catches 97.63% — each block waits 2400 hours between visits in that last case.
- **Every derived number carries its derivation** in the doc comments, so a reader can disagree with the model rather than having to reverse it from a call.

### Negative

- **The catch model assumes a single masking error.** Real corruption can spread through a parity scheme, a replica, or a file system's own writes; §45.7's motivation is precisely that all copies may decay, which this model does not represent.
- **MTTF is uniform across blocks.** §45.1's Bairavasundaram findings note spatial and temporal locality — "Most disks with LSEs have less than 50" bad sectors — so errors are clustered, and a uniform rate understates what a scrubber finds when it hits a bad region.
- **The schedule is a real background thread**, so the route does not exercise it; the schedule is covered by tests, not by the smoke route.
- **The catch model is a per-block race, not a spreading failure.** §45.7's motivation is that *all copies* of an item may decay, which a single-block masking model does not represent.
- **Throttling is `WaitHandle.WaitOne`, not I/O priority.** Real scrubbers yield to foreground I/O (`ionice` and friends), which this does not model.

## Verification

- Build clean (`dotnet build`).
- 7 tests: a pass covers exactly its batch and the cursor resumes and wraps; the catch probability equals `exp(-T/MTTF)` at a known point, is strictly monotone in `T`, and returns 1 for an infinite MTBF; the sweep period follows `interval × ceil(N/B)` and a smaller batch lowers the per-block catch probability; the §45.8 overhead matches both the printed 0.19% and the exact ratio; the schedule runs, does real work, and `Stop()` joins the worker with nothing scrubbing afterwards; an unscheduled scrubber never touches the disk; durations the timer cannot express are rejected at `Schedule`; and four threads hammering `Schedule`/`Stop` concurrently cannot orphan a worker.
- Mutation-checked: resetting the cursor each pass, inverting the probability to `1 - exp(...)`, dropping the percentage conversion, removing the lifecycle lock, and dropping the interval-range validation each make the corresponding test fail.
- **Not** mutation-checked: removing the `Wait` from `Stop()`, or disposing the CTS when the join times out. Both still pass — `WaitHandle.WaitOne` returns on cancellation immediately, so the join is not what ends the loop, and `Task.Wait` almost always completes promptly after cancellation, so the timeout branch is hard to reach. The tests assert the observable outcome rather than a mechanism they cannot pin.
- Smoke: all three scenarios return 200, bad parameters return 400 (including NaN, infinite and overflowing intervals), M27's `compute`/`corrupt`/`scrub` are unchanged, and 30 M33 requests leave the thread count flat or lower.

### Defects found by review after the slice's own tests were green

Five, all fixed:

1. **The "weekly" candidate was a seven-*hour* row** (`intervalHours * 7 / 24` instead of `* 7`), so the route presented a scan three times *more* frequent than nightly while labelling it weekly, and it scored a higher catch probability — inverting the nightly-versus-weekly comparison §45.7 sets up.
2. **`Schedule`/`Stop` were not atomic.** Field accesses were individually locked, but the stop-start-publish sequence was not, so two concurrent `Schedule` calls could both stop before either published and orphan the first worker. A lifecycle lock now covers the sequence, separate from the sweep lock so the join never blocks the worker it waits for.
3. **`Stop` claimed success when its join timed out.** `Task.Wait`'s result was discarded, so a worker still inside a batch would have its reference cleared and its CTS disposed while running. `Stop` now keeps ownership and throws `TimeoutException`.
4. **Durations `WaitHandle.WaitOne` cannot express were accepted** — a sub-millisecond interval became a zero-length wait and a spin loop, and a 30-day interval faulted the worker silently.
5. **Non-positive `blockSize`, a two-byte seed on a one-byte block, NaN parameters and overflowing intervals all closed the connection instead of answering 400.** The route now validates them, and sizes the sweep's payload to the block.

## Source Documents

- `docs/learning/m33-integrity-extensions/overview.md` — milestone scope.
- `docs/learning/m33-integrity-extensions/s1-scrubbing-schedule.md` — slice doc.
- `docs/learning/m27-integrity/overview.md` — predecessor one-shot scrubber.
- OSEP Ch. 45 §45.1 — LSE statistics, including the locality findings this model ignores.
- OSEP Ch. 45 §45.7 — what scrubbing is for, and the nightly-or-weekly baseline.
- OSEP Ch. 45 §45.8 — space and time overheads, the 8-byte-per-4 KB figure, and the tunable background I/O.