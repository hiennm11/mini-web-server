# Milestone 31: LFS Extensions (Ch. 43 §43.3 + §43.12)
> **Overview** - what this milestone covers and where to start. The slices live in this folder.
## Question
How much data should LFS buffer before flushing? Why does a too-small segment thrash the cleaner, and a too-large segment waste bandwidth on partial writes? How do real LFS implementations avoid the "two-CR-write deadlock" when a segment flush overlaps with a CR update?
## Scope
Two slices extending the M25 LFS simulator:
1. **§43.3 segment-size math** [RO91]: a cost model that, given write bandwidth `R_peak`, positioning time `T_position`, and desired fraction-of-peak `F`, computes the minimum segment size needed to achieve that fraction. The simulator exposes `/lfs/run?scenario=segment-size-sweep&segment_block_count=...` and reports the effective write-amplification factor.
2. **§43.12 two-CR alternating writes**: a robustness fix that adds a second checkpoint region (CR1) at the opposite end of the disk; on flush, the writer alternates CR0/CR1 with the header/body/trailer protocol OSEP describes ("first writes out a header (with timestamp), then the body of the CR, and then finally one last block (also with a timestamp)"). Recovery reads both and picks the one with consistent timestamps.

## Slices
- **[s1-segment-size.md](./s1-segment-size.md)** — segment-size cost model + sweep route.
- **[s2-two-cr-alternation.md](./s2-two-cr-alternation.md)** — dual-CR alternating writes + recovery that picks the newer CR.

## OSTEP coverage
- **Ch. 43 §43.3** "How Much To Buffer?" [RO91]: the cost-model argument. Equation 43.6 from the chapter: `D = (F / (1 - F)) × R_peak × T_position`. With T_position = 10 ms, R_peak = 100 MB/s, F = 0.9 → D = 9 MB. The slice reproduces this arithmetic as `SegmentSizer.OptimalBytes(peakBandwidth, positionTime, fraction)`.
- **Ch. 43 §43.12** "Crash Recovery And The Log" [R92]: "To ensure that the CR update happens atomically, LFS actually keeps two CRs, one at either end of the disk, and writes to them alternately. LFS also implements a careful protocol when updating the CR with the latest pointers to the inode map and other information; specifically, it first writes out a header (with timestamp), then the body of the CR, and then finally one last block (also with a timestamp). If the system crashes during a CR update, LFS can detect this by seeing an inconsistent pair of timestamps."

## Files
- `src/MiniWebServer.Host/MiniScheduler/Lfs.cs` — extend with `SegmentSizer` helper class + `DualCheckpointRegion` rotation state.
- `src/MiniWebServer.Host/Program.cs` — two new scenarios on `/lfs/run`.

## Implementation deviations from OSEP
- **Cost model is the simplified §43.3 form**: `D = (F / (1-F)) × R_peak × T_position`. Real LFSes tune via empirical sweeps on the target hardware; this is a sanity-check formula.
- **CR alternation is in-memory only**: the M25 simulator doesn't persist; the alternation is observable in the segment-flush log, not on disk across restarts.
- **Timestamps are simulated integers**, not real wall-clock. The slice increments a monotonic counter on every CR write and uses inequality of `(timestamp, header)` tuples for consistency.

## What this slice does NOT do
- **Real disk geometry** (RPM, seek curves) — the cost model uses OSEP's simplified parameters.
- **Imap fragment placement** (some LFSes spread imap fragments across the disk to lower CR-update frequency): deferred.
- **Multi-disk CR** (RAID-style) — out of scope.
- **Cross-CRC validation**: the slice uses CR-internal sequence numbers only; an attacker who can write to disk can lie. Real LFS adds a MAC per CR block.
- **Roll-forward recovery** (the second half of §43.12 — reading the log past the last consistent CR to recover lost writes): deferred.

## Where this leads
- Segment-size awareness affects cleaner policy in M25; a future cleaner could pick policy based on live-block count distribution.
- Dual-CR alternation sets up future work on **in-place updates** (the "hybrid" approach some LFSes use for hot metadata).
