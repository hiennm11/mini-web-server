# Milestone 31: LFS Extensions (§43.3 + §43.12)
> **Overview** - what this milestone covers and where to start. The slices live in this folder.
## Question
How do you pick the LFS segment size? Why does a too-small segment thrash the cleaner, and a too-large segment waste bandwidth on partial writes? How do real LFS implementations avoid the "two-CR-write deadlock" when writing the checkpoint region (CR) overlaps with a segment flush?
## Scope
Two slices extending the M25 LFS simulator:
1. **§43.3 Segment-size math**: a tiny cost model that, given write bandwidth, average file size, and disk seek cost, computes an optimal segment size. The simulator exposes `/lfs/run?scenario=segment-size-sweep&segment_block_count=...` and reports the effective write-amplification factor.
2. **§43.12 Two-CR alternating writes**: a robustness fix that adds a second checkpoint region (CR2) at the opposite end of the disk; on flush, the writer alternates CR0/CR1, and recovery reads both, taking the newer of the two. This avoids the deadlock where the writer needs to update CR but the disk blocks are taken by an in-progress segment flush.
## Slices
- **[s1-segment-size.md](./s1-segment-size.md)** — segment-size cost model + sweep route.
- **[s2-two-cr-alternation.md](./s2-two-cr-alternation.md)** — dual-CR alternating writes + recovery that picks the newer CR.
## OSTEP coverage
- **Ch. 43 §43.3 "Segment Size"** [RO91]: the cost-model argument that segment size should be large enough to amortize seek + rotational latency but small enough to keep the cleaner working set manageable. The OSEP textbook formula: optimal segment size = `B · (M / S)` where `B = bytes per segment`, `M = average file size`, `S = bandwidth × seek overhead`.
- **Ch. 43 §43.12 (deferred in M25)** "A New Approach: Solving the Problem": the two-CR alternating-write pattern that real LFS implementations use to keep CR updates atomic without coupling to the segment-cleaning cycle.
## Files
- `src/MiniWebServer.Host/MiniScheduler/Lfs.cs` — extend with `SegmentSizer` helper class + `DualCheckpointRegion` rotation state.
- `src/MiniWebServer.Host/Program.cs` — two new scenarios on `/lfs/run`.
## Implementation deviations from OSEP
- **Cost model is approximate**: the slice uses OSEP's simplified `B · (M / S)` formula. Real LFSes tune via empirical sweeps on the target hardware.
- **CR alternation is in-memory only**: the M25 simulator doesn't persist; the alternation is observable in the segment-flush log, not on disk across restarts.
- **No third "immutable" CR variant** (some implementations have CR0 (live) + CR1 (live) + CR-old (read-only archive)): deferred.
## What this slice does NOT do
- **Real disk geometry** (RPM, seek curves) — the cost model uses OSEP's simplified parameters.
- **Imap fragment placement** (some LFSes spread imap fragments across the disk to lower CR-update frequency): deferred.
- **Multi-disk CR** (RAID-style) — out of scope.
- **Cross-CRC validation**: the slice uses CR-internal sequence numbers only; an attacker who can write to disk can lie. Real LFS adds a MAC per CR block.
## Where this leads
- Segment-size awareness affects cleaner policy in M25; future cleaner could pick policy based on live-block count distribution.
- Dual-CR alternation sets up future work on **in-place updates** (the "hybrid" approach some LFSes use for hot metadata).
