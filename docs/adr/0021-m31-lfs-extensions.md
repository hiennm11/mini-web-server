# ADR 0021: M31 LFS Extensions — Segment Sizing and Two-CR Recovery (OSEP Ch. 43 §43.3 + §43.12)

## Status

Accepted

## Date

2026-10-07

## Context

M25's `Lfs` covers the LFS data path: segments, the imap, a single checkpoint region, liveness, and the cleaner. Two pieces of Ch. 43 are missing.

**§43.3 "How Much To Buffer?"** asks how many updates to accumulate before flushing, and answers with a cost model: every write pays a fixed positioning cost, and the segment must be large enough to amortise it. The chapter derives equation 43.6 and works an example.

**§43.12 "Crash Recovery And The Log"** asks what happens if the system crashes mid-update. A single CR is updated in place, so a crash mid-write destroys the one structure the whole file system depends on. The chapter's answer is two CRs written alternately with a header/body/trailer protocol.

### Two corrections to the milestone spec

**1. The spec's U-shape has no basis.** `s1-segment-size.md` asked for a write-amplification model with "a minimum in the middle of the range" — small segments bad because of positioning, large segments bad because they accumulate garbage.

Measured, that is not what the arithmetic gives. The model is

```
A = 1/u + T / (T + n·B/10⁶R)
```

where `n` is blocks per segment, `B` the block size, `T` = `T_position`, `R` = `R_peak`, `u` the live ratio. Its derivative in `n` is `-T·(B/10⁶R) / (T + n·B/10⁶R)²`, which is negative for positive `T`, `B`, `R` — so the total falls monotonically with segment size. The cleaner's `(1-u)/u` term carries no `n` at all: the cleaner reads M segments and writes N < M new ones, and the rewrite-to-freed ratio depends on how dirty a segment is, not on how many blocks it held. A "minimum in the middle" assertion passed only because the model that produced it was wrong.

The model now says what is actually true: **write-path cost falls monotonically with segment size, and the cleaner's term does not depend on it.** The route output and the tests say so too. Segment size is still not free to choose — larger segments hold more unpersisted updates in memory, so a crash loses more — but §43.3 sizes the write path and makes no total-cost claim.

**2. "Time-stamped" means monotonic, not wall-clock.** The spec called for timestamps; the simulator uses a monotonic counter so the consistency property is testable without the tests depending on timing.

## Decision

### `SegmentSizer` (§43.3)

- `OptimalBytes(R_peak, T_position, F)` — equation 43.6, in bytes with decimal MB (1 MB = 1_000_000) to match the chapter's own arithmetic. `OptimalBytes(100, 0.010, 0.9)` = 9 MB, the chapter's worked example.
- `EffectiveBandwidthFraction(D, ...)` — equation 43.2, the quantity `OptimalBytes` inverts. Kept as a separate public method so a test can feed the optimum back and confirm the round trip, rather than trusting the closed form.
- `WriteAmplification(...)` — `1 + positioningOverhead + (1-liveRatio)/liveRatio`, with the reasoning above.

F is validated to be strictly between 0 and 1: at F = 1 the formula divides by zero, and F ≥ 1 is not a meaningful request.

### `DualCheckpointRegion` (§43.12)

Two CR images, alternated on every write. Each write stamps a header, a body, and a trailer; the trailer is written last, so `header == trailer` is the proof the write completed. `Recover()` takes the most recent consistent CR and reports why the other was rejected.

A CR that was never written, or both CRs inconsistent, raises rather than mounting a file system with no anchor — an unmountable filesystem should be an error, not an empty one.

`Write(bodyTimestamp, trailerTimestamp)` takes the body and trailer timestamps as parameters purely so a test can represent a crash between them. That is the only reason the method has a signature rather than being a plain `Write()`.

### Route

Five scenarios on the existing `/lfs/run`, dispatched before the M25 simulator is constructed since none of them need it: `segment-size-sweep`, `cost-model`, `dual-cr-recovery`, `cr-alternation`, `cr-crash-during-write`.

## Consequences

### Positive

- **Both OSTEP sections are runnable**, with the chapter's own numbers reproduced exactly.
- **The cost model is verified by inversion**, not by matching a literal. `OptimalBytes` and `EffectiveBandwidthFraction` are checked against each other, so a sign error or a unit slip in either would fail.
- **A wrong spec assumption is corrected rather than implemented.** The U-shape would have taught the opposite of what §43.9 says.

### Negative

- **The cost model is a first-order approximation.** It ignores disk geometry (seek curves, rotational latency as a function of position), the cleaner's segment-selection policy, and the fact that a real cleaner batches M segments at a time rather than reclaiming one.
- **Recovery is simulated, not exercised.** `DualCheckpointRegion` does not persist across restarts and is not wired into M25's `Lfs`, whose CR is still the single `segment 0, block 0` block M25 uses. The two coexist: M25 models a running system, M31 models the crash-recovery protocol.
- **`WriteAmplification` is not a total LFS cost.** It is the write path plus the cleaner's rewrite. Segmented layout has further costs (segment summary blocks, partially-filled segments at the end of the log) that it omits.
- **No roll-forward.** §43.12's second half — replaying the log past the last checkpoint to recover writes since it was taken — is deferred.

## Verification

- Build clean (`dotnet build`).
- 4 new tests: `OptimalBytes` reproduces 9 MB / 19 MB / 99 MB and round-trips through `EffectiveBandwidthFraction`; the optimum grows with slower disks and longer seeks, and rejects F outside (0,1); write amplification is monotonically decreasing in segment size, the cleaner's term is segment-size independent, and a fully-live segment costs exactly `1 + positioning`; recovery picks the newest consistent CR, rejects both when neither is consistent, and falls back to the intact CR after a simulated mid-write crash.
- Smoke: all five scenarios return 200; unknown scenario returns 400.

## Source Documents

- `docs/learning/m31-lfs-extensions/overview.md` — milestone scope.
- `docs/learning/m31-lfs-extensions/s1-segment-size.md`, `s2-two-cr-alternation.md` — slice docs + smoke evidence.
- `docs/learning/m25-lfs/overview.md` — predecessor surface (single CR, segment cleaner).
- OSTEP Ch. 43 §43.3 "How Much To Buffer?" — equations 43.1–43.6, worked example (100 MB/s, 10 ms, F = 0.9 → 9 MB).
- OSTEP Ch. 43 §43.9 — the M-in, N-out cleaning argument used to correct the amplification model.
- OSTEP Ch. 43 §43.12 "Crash Recovery And The Log" — two CRs, alternation, header/body/trailer timestamps, "the most recent CR that has consistent timestamps".