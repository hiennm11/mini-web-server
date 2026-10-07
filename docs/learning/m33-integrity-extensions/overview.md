# Milestone 33: Scrubbing Schedule (Ch. 45 §45.7)
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
How often should a data-integrity scrubber run? What fraction of the disk can it cover per pass without impacting foreground I/O? When does periodic scrubbing fail to catch latent corruption before it spreads?
## Scope
A scheduling layer over M27's `IntegrityStore` (Ch. 45 §45.7). OSEP §45.7 says: "By periodically reading through every block of the system, and checking whether checksums are still valid, the disk system can reduce the chances that all copies of a certain data item become corrupted. Typical systems schedule scans on a nightly or weekly basis." M27's `Scrub()` is one-shot; the slice adds the policy the chapter describes:
- **Incremental sweep**: a pass covers `batchSize` blocks and resumes where the previous one stopped, wrapping at the end of the disk. A batched pass that restarted at block 0 would never reach the tail.
- **Periodic schedule**: a background loop fires every `interval`, with a `throttle` pause between passes so the scrubber does not monopolise the device. `Stop()` joins the worker — and on a 5 s join timeout it keeps ownership and throws `TimeoutException` rather than reporting a stop that did not happen (bug 6 in the slice doc).
- **Catch-probability model**: given a per-block MTBF, `P(caught) = exp(-sweepPeriod / MTBF)`, with the sweep period derived from the interval and batch size. The model is derived here, not quoted - OSEP states no formula.
- **Smoke-driven route**: `/integrity/run?scenario=scrub-schedule`, `scrub-sweep` and `checksum-overhead`.

## Slice
- **[s1-scrubbing-schedule.md](./s1-scrubbing-schedule.md)** — periodic scrubber + throttle + catch-probability model.

## OSTEP coverage
- **Ch. 45 §45.7** "Scrubbing" [K+08]: the M27 precursor; this slice adds the *scheduling* half. The chapter cites: "Disk scrubbing is useful (most LSEs were found this way)" and lists scheduling choices as nightly or weekly.
- **Ch. 45 §45.8** (deferred in M27) — the space/time overheads argument that motivates why scrubbing isn't free: scrubbing too often hurts performance; too rarely misses silent corruption. OSEP §45.8 also lists the on-disk space overhead (8-byte checksum per 4 KB block ≈ 0.19%) and CPU overhead of computing checksums on every read.
- **Ch. 45 §45.5** (physical ID for misdirected writes) and **§45.6** (write sequence for lost writes) — both run during scrubbing.

## Files
- `src/MiniWebServer.Host/MiniScheduler/Scrubber.cs` — `Scrubber` (incremental sweep + `Schedule`/`Stop`), `CatchProbability` (derived model), `ChecksumOverhead` (§45.8 figures).
- `src/MiniWebServer.Host/Program.cs` — `/integrity/run` gains `scrub-schedule`, `scrub-sweep` and `checksum-overhead`, dispatched before M27's store is constructed.

## Implementation deviations from OSEP
- **Throttling is a timed wait, not I/O priority.** Real systems use IO priority classes (ionice on Linux).
- **No memory pressure awareness**: the scheduler runs at fixed intervals. Real scrubbers back off under memory pressure.
- **Catch-probability is a model derived here, not quoted** — OSEP states no probability formula. The derivation is in the code and in ADR 0023. Real systems use hardware-level BER (bit error rate) data, and §45.1's LSE findings show real errors are clustered rather than uniform.
- **No ZFS-style end-to-end checksum tree** (M27 already deferred this; the slice doesn't add it back).

## What this slice does NOT do
- **Repair-on-detect**: when a corruption is found, the slice reports it; auto-repair (copying from a redundant copy) would be a future milestone that combines M27 + M24 (RAID).
- **Cross-disk scrubbing coordination** (for storage arrays) — out of scope for a single disk.
- **Scrubbing policy tuning** based on disk temperature, age, or recent error log — out of scope.

## Where this leads
- Combined with M24 RAID, a future slice could demonstrate repair-on-detect: scrub detects a bad block, looks up its stripe, reconstructs from the surviving N-1 blocks, and writes back.
- The schedule parameters are reusable for any future data-integrity subsystem (e.g., M12 `minifs.img` background integrity checks).
