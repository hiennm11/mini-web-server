# Milestone 33: Scrubbing Schedule (§45.8)
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
How often should a data-integrity scrubber run? What fraction of the disk can it cover per pass without impacting foreground I/O? When does periodic scrubbing fail to catch latent corruption before it spreads?
## Scope
A scheduling layer on top of the M27 integrity simulator's one-shot `Scrubber`. Adds:
- **Periodic schedule**: a background loop that runs the scrubber every `intervalMinutes`, covering `batchSize` blocks per pass (the un-scanned portion is left for the next pass).
- **I/O throttling**: between batches, the scheduler sleeps for `throttleMicros` microseconds so the scrubber doesn't starve foreground reads/writes.
- **Latent-corruption detection budget**: given a per-block MTTF (mean time to failure), the slice computes the **probability of catching corruption before it spreads** under different `intervalMinutes × batchSize` combinations. Mirrors the §45.8 latency/frequency trade-off.
- **Smoke-driven route**: `/integrity/run?scenario=scrub-schedule&interval_minutes=I&batch_size=B&block_mtbf_hours=MTTF` reports catches-per-pass and probability-of-spread.
## Slice
- **[s1-scrubbing-schedule.md](./s1-scrubbing-schedule.md)** — periodic scrubber + throttle + catch-probability model.
## OSTEP coverage
- **Ch. 45 §45.7** "Scrubbing" [PGK08] — the M27 precursor; this slice adds the *scheduling* half.
- **Ch. 45 §45.8** (deferred in M27) — the latency/frequency argument: scrubbing too often hurts performance; too rarely misses silent corruption that may spread.
## Files
- `src/MiniWebServer.Host/MiniScheduler/Integrity.cs` — extend `IntegrityStore` with `Scrubber.Schedule(interval, batch, throttle)`.
- `src/MiniWebServer.Host/Program.cs` — `/integrity/run` route handles `scrub-schedule` scenario.
## Implementation deviations from OSEP
- **Throttling via `Thread.Sleep`** in the smoke loop. Real systems use IO priority classes (ionice on Linux).
- **No memory pressure awareness**: the scheduler runs at fixed intervals. Real scrubbers back off under memory pressure.
- **Catch-probability is a closed-form approximation** — uses OSEP §45.8's Poisson-arrival model. Real systems use hardware-level BER (bit error rate) data.
## What this slice does NOT do
- **ZFS-style end-to-end checksum tree** (M27 already deferred this; the slice doesn't add it back).
- **Repair-on-detect**: when a corruption is found, the slice reports it; auto-repair (copying from a redundant copy) would be a future milestone that combines M27 + M24 (RAID).
- **Cross-disk scrubbing coordination** (for storage arrays) — out of scope for a single disk.
## Where this leads
- Combined with M24 RAID, a future slice could demonstrate repair-on-detect: scrub detects a bad block, looks up its stripe, reconstructs from the surviving N-1 blocks, and writes back.
- The schedule parameters are reusable for any future data-integrity subsystem (e.g., M12 `minifs.img` background integrity checks).
