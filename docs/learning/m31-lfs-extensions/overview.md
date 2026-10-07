# Milestone 31: LFS Extensions (Ch. 43 §43.3 + §43.12)
> **Overview** - what this milestone covers and where to start. The slices live in this folder.
## Question
How much data should LFS buffer before flushing, given the disk's positioning overhead? And what happens to the file system if the machine crashes while the checkpoint region is being written?
## Scope

Two slices extending the M25 LFS story:

1. **§43.3 segment-size math**: `SegmentSizer` implements equation 43.6 (`D = (F/(1-F)) × R_peak × T_position`) and the effective-rate formula (43.2) it inverts, plus a write-amplification model. Routes: `/lfs/run?scenario=cost-model` and `?scenario=segment-size-sweep`.
2. **§43.12 two-CR alternating writes**: `DualCheckpointRegion` keeps two CRs and alternates writes to them using the header/body/trailer timestamp protocol. Recovery mounts the most recent CR whose header and trailer agree. Routes: `/lfs/run?scenario=dual-cr-recovery`, `?scenario=cr-alternation`, `?scenario=cr-crash-during-write`.

### Two corrections to the original spec

- **The write-amplification model has no interior minimum.** The spec asked for a U-curve where small segments are bad (positioning) and large ones are bad (accumulated garbage). §43.9's arithmetic does not support it: the cleaner reads M segments and writes N<M, so the reclaim cost per unit of live data is `(T_position/R_peak + 1 + liveRatio)/liveRatio` with the segment size cancelling. Both terms fall with segment size. The model, the route output and the tests now say what is true — see `s1-segment-size.md`.
- **"Timestamp" means monotonic counter**, not wall-clock, so the CR consistency property is testable without the tests depending on timing.

## Slices
- **[s1-segment-size.md](./s1-segment-size.md)** — equation 43.6, its inverse, and the amplification sweep.
- **[s2-two-cr-alternation.md](./s2-two-cr-alternation.md)** — two CRs, alternation, header/body/trailer, crash recovery.

## OSTEP coverage
- **Ch. 43 §43.3** "How Much To Buffer?" [RO91]: the cost-model argument. Equation 43.6 from the chapter: `D = (F / (1 - F)) × R_peak × T_position`. With T_position = 10 ms, R_peak = 100 MB/s, F = 0.9 → D = 9 MB. The slice reproduces this arithmetic as `SegmentSizer.OptimalBytes(peakBandwidth, positionTime, fraction)`.
- **Ch. 43 §43.12** "Crash Recovery And The Log" [R92]: "To ensure that the CR update happens atomically, LFS actually keeps two CRs, one at either end of the disk, and writes to them alternately. LFS also implements a careful protocol when updating the CR with the latest pointers to the inode map and other information; specifically, it first writes out a header (with timestamp), then the body of the CR, and then finally one last block (also with a timestamp). If the system crashes during a CR update, LFS can detect this by seeing an inconsistent pair of timestamps."

## Files
- `src/MiniWebServer.Host/MiniScheduler/SegmentSizer.cs` — new file. `SegmentSizer` (equation 43.6, its inverse, the amplification model) and `DualCheckpointRegion` (the §43.12 protocol).
- `src/MiniWebServer.Host/MiniScheduler/LfsExtensionsDemos.cs` — new file. The five route scenarios.
- `src/MiniWebServer.Host/Program.cs` — five new scenarios on `/lfs/run`, dispatched before the M25 simulator is constructed.

## Implementation deviations from OSEP
- **The cost model is the simplified §43.3 form**: `D = (F / (1-F)) × R_peak × T_position`. Real LFSes tune by empirical sweep on the target hardware; this is the sanity-check formula. The chapter's §43.9 extension (the segment-size term cancelling out of the cleaning cost) is used to correct the amplification model, not to replace §43.3.
- **The CR protocol is modelled standalone, not wired into M25.** `DualCheckpointRegion` is a model of the crash-recovery protocol; M25's `Lfs` still uses its single `segment 0, block 0` CR block. They are separate concerns — M25 is a running system, this is what happens when it stops mid-write.
- **Timestamps are a monotonic counter**, not wall-clock, so the consistency property is testable without the tests depending on timing.
- **Recovery failure is an exception.** Neither CR written, or both torn, raises rather than mounting a filesystem with no anchor.
- **`Write(bodyTimestamp, trailerTimestamp)` takes parameters** purely so a test can represent a crash between body and trailer. Nothing else needs them.

## What this slice does NOT do
- **Real disk geometry** (RPM, seek curves, rotational latency by position) — the cost model uses OSEP's simplified parameters.
- **Imap fragment placement** (some LFSes spread imap fragments across the disk to lower CR-update frequency): deferred.
- **Multi-disk CR** (RAID-style) — out of scope.
- **Cross-CR validation**: a CR is trusted on its internal timestamp agreement alone, so anything that can write to disk can forge a consistent-looking CR. Real LFS adds a MAC per CR block.
- **Roll-forward recovery** (the second half of §43.12 — reading the log past the last consistent CR to recover writes since it was taken): deferred.
- **Wiring the CR update into M25's flush path.**

## Where this leads
- Segment-size awareness affects cleaner policy in M25; a future cleaner could pick policy based on live-block count distribution.
- Dual-CR alternation sets up future work on **in-place updates** (the "hybrid" approach some LFSes use for hot metadata).
