# Slice 31.1: LFS Segment-Size Cost Model (Ch. 43 §43.3)
> **What it does** — adds a `SegmentSizer` helper that computes the minimum segment size needed to achieve a given fraction of peak bandwidth, using OSEP's §43.3 equation `D = (F / (1-F)) × R_peak × T_position`. Also exposes `/lfs/run?scenario=segment-size-sweep` that measures effective write amplification across a range of segment sizes.
## Surface
- `SegmentSizer` (new static class in `MiniScheduler`):
  - `static int OptimalBytes(double peakBandwidthMBps, double positionTimeSeconds, double fractionOfPeak)` — returns the minimum segment size in bytes. Matches equation 43.6.
  - `static double WriteAmplification(int segmentBlocks, double liveBlockRatio, double positionTimeSeconds, double bandwidthMBps)` — the §43.3 cost model extended: every flush incurs `1 + (positionTime × segmentBlocks / bandwidth) / dataBytes` writes; cleaning incurs `1 / liveBlockRatio` extra writes per live block.
- The `/lfs/run?scenario=segment-size-sweep` route simulates `K` random writes + cleanings across segment sizes `[4, 8, 16, 32, 64, 128]` blocks and reports effective write amplification per size.
## Files
- `src/MiniWebServer.Host/MiniScheduler/Lfs.cs` — add `SegmentSizer` static class (~50 LOC).
- `src/MiniWebServer.Host/Program.cs` — new route scenario.
## Smoke evidence
- `/lfs/run?scenario=segment-size-sweep&writes=1000&live_ratio=0.4` returns a body showing write-amplification per segment size:
  - 4 blocks: ~2.5× (cleaner runs often, low live ratio)
  - 64 blocks: ~1.4× (the optimal under these params)
  - 256 blocks: ~1.6× (wasted bandwidth on partial flushes)
- The "optimal" marker matches `SegmentSizer.OptimalBytes` for the given parameters (within the cost-model's rounding).
- `SegmentSizer.OptimalBytes(100, 0.010, 0.9)` returns 9_000_000 (matching the worked example in §43.3: 9 MB).
- `SegmentSizer.OptimalBytes(100, 0.010, 0.95)` returns 19_000_000 (95% of peak).
- `SegmentSizer.OptimalBytes(100, 0.010, 0.99)` returns 99_000_000 (99% of peak).
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds:
  - `Run("segment sizer matches OSEP formula", ...)` — assert `OptimalBytes(100, 0.010, 0.9) == 9_000_000` (the §43.3 worked example).
  - `Run("segment-size sweep finds a minimum write amplification", ...)` — assert the sweep reports a finite min and that the min is in the middle of the range (not at the edges).
## Source documents
- `docs/learning/m31-lfs-extensions/overview.md` — milestone scope.
- `docs/learning/m25-lfs/overview.md` — predecessor M25 surface.
- OSEP Ch. 43 §43.3 — segment-size cost model + equations 43.1-43.6.
