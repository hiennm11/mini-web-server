# Slice 31.1: LFS Segment-Size Cost Model (§43.3)
> **What it does** — adds a `SegmentSizer` helper that computes the optimal segment size from `(bandwidth_MBps, average_file_MB, seek_overhead_ms)` using OSEP's `B = M · S` rule, and a `/lfs/run?scenario=segment-size-sweep` driver that measures effective write-amplification across a range of segment sizes.
## Surface
- `SegmentSizer` (new class in `MiniScheduler`):
  - `static int OptimalBlocks(double bandwidthMBps, double avgFileMB, double seekMs, int blockBytes)` — returns the segment size in blocks.
  - `static double WriteAmplification(int segmentBlocks, double liveBlockRatio, double seekMs)` — the cost model: every flush incurs `1 + (seekMs × segmentBlocks / bandwidth) / dataBytes` writes; cleaning incurs `1 / liveBlockRatio` extra writes per live block.
- The `/lfs/run?scenario=segment-size-sweep` route simulates `K` random writes + cleanings across segment sizes `[4, 8, 16, 32, 64, 128]` blocks and reports effective write amplification per size.
## Files
- `src/MiniWebServer.Host/MiniScheduler/Lfs.cs` — add `SegmentSizer` static class (~50 LOC).
- `src/MiniWebServer.Host/Program.cs` — new route scenario.
## Smoke evidence
- `/lfs/run?scenario=segment-size-sweep&writes=1000&live_ratio=0.4` returns a body showing write-amplification per segment size:
  - 4 blocks: ~2.5× (cleaner runs often, low live ratio)
  - 64 blocks: ~1.4× (the optimal under these params)
  - 256 blocks: ~1.6× (wasted bandwidth on partial flushes)
- The "optimal" marker matches `SegmentSizer.OptimalBlocks` for the given parameters (within the cost-model's rounding).
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds:
  - `Run("segment sizer matches OSEP formula", ...)` — assert `OptimalBlocks(100, 4, 8, 4096) == 16 * 4096 / 4096 == 16` blocks.
  - `Run("segment-size sweep finds a minimum write amplification", ...)` — assert the sweep reports a finite min and that the min is in the middle of the range (not at the edges).
## Source documents
- `docs/learning/m31-lfs-extensions/overview.md` — milestone scope.
- `docs/learning/m25-lfs/overview.md` — predecessor M25 surface.
- OSTEP Ch. 43 §43.3 — segment-size rationale.
