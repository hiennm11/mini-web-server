# Slice 31.1: LFS Segment-Size Cost Model (Ch. 43 §43.3)

> **What it does** — implements OSEP §43.3's sizing formula and the effective-rate formula it inverts, so segment size can be computed from disk parameters instead of guessed; exposes `/lfs/run?scenario=cost-model|segment-size-sweep`.

## Question

How many updates should LFS buffer before flushing? The answer depends entirely on the disk — and chasing the last few percent of bandwidth costs far more memory than the chapter's headline example suggests.

## OSTEP coverage

- **§43.3 "How Much To Buffer?"**. "Every time you write, you pay a fixed overhead of the positioning cost. Thus, how much do you have to write in order to amortize that cost? The more you write, the better (obviously), and the closer you get to achieving peak bandwidth."
- The chapter's chain: write time (43.1), effective rate (43.2), set it to `F × R_peak` (43.3), solve (43.4-43.6) to get **`D = (F/(1-F)) × R_peak × T_position`**.
- Worked example, verbatim: "with a disk with a positioning time of 10 milliseconds and peak transfer rate of 100 MB/s; assume we want an effective bandwidth of 90% of peak (F = 0.9). In this case, D = 0.9/0.1 × 100 MB/s × 0.01 seconds = 9 MB."
- The chapter then asks "how much is needed to reach 95% of peak? 99%?" — the blow-up is the point.

## Surface

`SegmentSizer` (new, `MiniScheduler/SegmentSizer.cs`):

- `OptimalBytes(R_peak, T_position, F)` — equation 43.6, in bytes, decimal MB (1 MB = 1_000_000) so the chapter's arithmetic carries over unchanged.
- `EffectiveBandwidthFraction(D, R_peak, T_position)` — equation 43.2. Kept public so a test can invert and check the round trip rather than trusting the closed form.
- `WriteAmplification(segmentBlocks, liveRatio, blockBytes, T_position, R_peak)` — write-path positioning overhead plus the cleaner's `(1-liveRatio)/liveRatio` rewrite term.
- `WritePathOverhead(...)` — the same with a fully-live segment, i.e. positioning only.

## Smoke evidence

`/lfs/run?scenario=cost-model`:

```
disk: R_peak = 100 MB/s, T_position = 10 ms

F=90%:  D =     9.00 MB   achieved 90.00% of peak
F=95%:  D =    19.00 MB   achieved 95.00% of peak
F=99%:  D =    99.00 MB   achieved 99.00% of peak
```

The F=90% row is the chapter's example. The other two are its follow-up question, and the progression is the lesson: going from 90% to 99% of peak costs **eleven times** the buffer.

## The amplification model does not have a minimum, and that is the finding

The milestone spec asked for a model with "a minimum in the middle of the range": small segments bad because of positioning, large segments bad because they accumulate garbage.

**That is not what the arithmetic gives.** The implemented model is

```
A = 1/u + T / (T + n·B/10⁶R)
```

with `n` = blocks per segment, `B` = block bytes, `T` = `T_position`, `R` = `R_peak`, `u` = live ratio. Its derivative in `n` is `-T·(B/10⁶R) / (T + n·B/10⁶R)²`, negative for positive `T`, `B`, `R` — so the total falls monotonically. The cleaner's `(1-u)/u` term carries no `n` at all: the cleaner reads M segments and writes N < M new ones, and the rewrite-to-freed ratio depends on how dirty a segment is, not on how many blocks it held. A "minimum in the middle" assertion passed only because the model that produced it was wrong.

`/lfs/run?scenario=segment-size-sweep`:

```
blocks/seg   segment MB   effective BW   write amp
4            0.016        1.6%           3.48x
8            0.033        3.2%           3.47x
16           0.066        6.2%           3.44x
32           0.131        11.6%          3.38x
64           0.262        20.8%          3.29x
128          0.524        34.4%          3.16x
256          1.049        51.2%          2.99x
```

Two readings, both real:

- **The write path improves monotonically.** Positioning is amortised over more data, and at 256 blocks the write runs at half of peak even though the segment is only 1 MB — which is exactly why §43.3's formula says 9 MB is what 90% actually requires.
- **The cleaner's term dominates and does not move.** At a 40% live ratio it is a flat `1.5x` across every row. Halving the live ratio to 20% raises every row by the same 2.5x.

So there is no total-cost optimum under this model. Segment size is still not free to choose — larger segments hold more unpersisted updates in memory before a flush, so a crash loses more — but §43.3 sizes the write path, and does not claim a total-cost optimum. That trade-off is not modelled here.

## Bugs found while building this

All caught by asserting outcomes rather than shapes, or by review:

1. **The amplification model had no minimum, and the test that "confirmed" one was passing against the wrong thing.** See above — the spec's U-shape, the spec's derivation `(T/R + 1 + u)/u`, and a test asserting a minimum in the middle. The correct description of the implemented model is `A = 1/u + T/(T + n·B/10⁶R)`, whose derivative in `n` is negative for positive `T, B, R`; the cleaner's independence is a property of the chosen term, not something derived from the chapter.
2. **`AssertClose` accepted NaN.** Written as `if (difference > threshold) throw`, and every comparison with NaN is false — so a helper mutated to return NaN passed every assertion that used it. Now `if (!(difference <= threshold)) throw` plus an explicit finiteness check. Mutation-checked: reverting the fix makes the new `assert helpers reject NaN` test fail.
3. **`OptimalBytes` overflowed on its intermediate.** Left-associated as `odds * R_peak * T`, the pair `(1e308, 1e-308)` overflowed to Infinity for an answer that is 9 MB. Now `(R_peak * T) * odds`, with a regression asserting the finite value.
4. **`T_position = 0` was accepted.** It returns 0 — a "0-byte optimal segment" — and its inverse cannot recover F, since a disk with no positioning overhead reaches peak at any size. Now rejected, with the reason in the exception message.

## Tests

- `segment sizer reproduces the OSEP §43.3 worked example` — 9 MB / 19 MB / 99 MB, then `OptimalBytes` fed back through `EffectiveBandwidthFraction` must return F for F ∈ {0.5, 0.9, 0.95, 0.99}. That round trip is what catches a unit or sign error; asserting only the closed form would pass even if the two disagreed. Also: the optimum grows for slower disks and longer seeks, and F outside (0,1) is rejected — at F = 1 the formula divides by zero.
- `segment-size write amplification falls with size and is cleaning-independent` — strictly decreasing across 4→256 blocks; the gap between two sizes is the same whether the live ratio is 0.4 or 1.0, proving the cleaner term is segment-size independent; a fully-live segment costs exactly `1 + positioning`; a lower live ratio costs more. F ∈ (0,1] is validated.
- `segment sizer rejects non-invertible and overflowing inputs` — `T_position = 0` rejected; the `(1e308, 1e-308)` pair returns a finite 9 MB rather than Infinity; the inverse stays finite there; NaN and Infinity parameters rejected.
- `assert helpers reject NaN rather than silently passing` — pins the comparison form so the helper cannot be regressed into a NaN-permeable one.

## What this slice does NOT do

- **Roll-forward recovery** — §43.12's second half, deferred to slice 31.2's section.
- **A second-order cost model** — no seek curves, no rotational latency as a function of position, no cleaner selection policy, no segment-summary overhead.
- **Memory budgeting** — the formula returns a size, not a RAM recommendation.

## Source documents

- `docs/learning/m31-lfs-extensions/overview.md` — milestone scope.
- `docs/learning/m25-lfs/overview.md` — predecessor surface.
- `docs/adr/0021-m31-lfs-extensions.md` — the spec correction and why.
- OSTEP Ch. 43 §43.3 (equations 43.1-43.6), §43.9 (cleaning argument).