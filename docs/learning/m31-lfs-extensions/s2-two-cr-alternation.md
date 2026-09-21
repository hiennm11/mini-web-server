# Slice 31.2: Two-CR Alternating Writes (Ch. 43 §43.12)
> **What it does** — adds a second checkpoint region (CR1) at a fixed offset opposite CR0; the writer alternates CR0 → CR1 → CR0 on each flush using the §43.12 three-block protocol (header with timestamp, then body, then trailer with timestamp). Recovery reads both CRs and takes the one with consistent timestamps as the live state; the other is ignored. Solves the "I want to write the CR but the segment I'm flushing needs to be reclaimed first" deadlock.
## Surface
- `Lfs` (extended in `MiniScheduler`):
  - `int _activeCrIndex` — 0 or 1; toggles on every CR flush.
  - `void FlushCheckpointRegion()` — writes header → body → trailer to `_crs[_activeCrIndex]`, then toggles. The header and trailer both carry the same monotonic timestamp.
  - `void Recover()` (or `Mount()`) — reads both CRs; checks `(header.timestamp == trailer.timestamp)` for each; takes the one with the higher timestamp as the live state. If timestamps tie, first-found wins.
- The simulator exposes `/lfs/run?scenario=dual-cr-recovery&cr0_seq=X&cr1_seq=Y` which sets the two CR sequence numbers and reports which is picked.
## Files
- `src/MiniWebServer.Host/MiniScheduler/Lfs.cs` — extend `Lfs` with `DualCheckpointRegion` rotation + recovery selector.
- `src/MiniWebServer.Host/Program.cs` — new route scenario.
## Smoke evidence
- `/lfs/run?scenario=dual-cr-recovery&cr0_seq=10&cr1_seq=20` reports "CR1 picked (seq 20 > CR0 seq 10)".
- `/lfs/run?scenario=dual-cr-recovery&cr0_seq=20&cr1_seq=10` reports "CR0 picked (seq 20 > CR1 seq 10)".
- `/lfs/run?scenario=dual-cr-recovery&cr0_seq=10&cr1_seq=10` reports "tie — first found wins (CR0)".
- `/lfs/run?scenario=demonstrate-alternation&flushes=10` shows the alternation pattern: CR0, CR1, CR0, CR1, ... over 10 flushes.
- `/lfs/run?scenario=cr-corruption-detected&cr0_timestamp=20&cr0_header_ts=20&cr0_trailer_ts=20&cr1_timestamp=10&cr1_header_ts=10&cr1_trailer_ts=10` reports "CR0 picked (CR1 has inconsistent timestamps — header 10 != trailer 20)".
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds:
  - `Run("dual-cr recovery picks higher timestamp", ...)` — assert newer CR wins.
  - `Run("dual-cr alternation toggles index", ...)` — assert N flushes end at `_activeCrIndex == N % 2`.
  - `Run("dual-cr survives partial CR write", ...)` — simulate a crash mid-CR0 write (header.ts ≠ trailer.ts); assert recovery picks CR1.
  - `Run("dual-cr header/trailer timestamp match required", ...)` — assert a CR with mismatched header/trailer is rejected.
## Source documents
- `docs/learning/m31-lfs-extensions/overview.md` — milestone scope.
- `docs/learning/m25-lfs/overview.md` — predecessor M25 surface (single CR).
- OSEP Ch. 43 §43.12 — two-CR alternating-write pattern + header/body/trailer protocol.
