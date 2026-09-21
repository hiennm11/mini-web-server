# Slice 31.2: Two-CR Alternating Writes (§43.12)
> **What it does** — adds a second checkpoint region (CR1) at a fixed offset opposite CR0; the writer alternates CR0 → CR1 → CR0 on each flush; recovery reads both and picks the one with the higher sequence number. Solves the "I want to write the CR but the segment I'm flushing needs to be reclaimed first" deadlock.
## Surface
- `Lfs` (extended in `MiniScheduler`):
  - `int _activeCrIndex` — 0 or 1; toggles on every CR flush.
  - `void FlushCheckpointRegion()` — writes to `_crs[_activeCrIndex]`, then toggles.
  - `void Recover()` (or `Mount()`) — reads both CRs; takes the one with the higher sequence number as the live state; the other is ignored (it may be stale or partially written).
- The simulator exposes `/lfs/run?scenario=dual-cr-recovery&cr0_seq=X&cr1_seq=Y` which sets the two CR sequence numbers and reports which is picked.
## Files
- `src/MiniWebServer.Host/MiniScheduler/Lfs.cs` — extend `Lfs` with `DualCheckpointRegion` rotation + recovery selector.
- `src/MiniWebServer.Host/Program.cs` — new route scenario.
## Smoke evidence
- `/lfs/run?scenario=dual-cr-recovery&cr0_seq=10&cr1_seq=20` reports "CR1 picked (seq 20 > CR0 seq 10)".
- `/lfs/run?scenario=dual-cr-recovery&cr0_seq=20&cr1_seq=10` reports "CR0 picked (seq 20 > CR1 seq 10)".
- `/lfs/run?scenario=dual-cr-recovery&cr0_seq=10&cr1_seq=10` reports "tie — first found wins (CR0)" — the recovery code's documented tie-break policy.
- `/lfs/run?scenario=demonstrate-alternation&flushes=10` shows the alternation pattern: CR0, CR1, CR0, CR1, ... over 10 flushes.
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds:
  - `Run("dual-cr recovery picks higher sequence", ...)` — assert newer CR wins.
  - `Run("dual-cr alternation toggles index", ...)` — assert N flushes end at `_activeCrIndex == N % 2`.
  - `Run("dual-cr survives partial CR write", ...)` — simulate a crash mid-CR0 write; assert recovery still works from CR1.
## Source documents
- `docs/learning/m31-lfs-extensions/overview.md` — milestone scope.
- `docs/learning/m25-lfs/overview.md` — predecessor M25 surface (single CR).
- OSTEP Ch. 43 §43.12 — two-CR alternating-write pattern.
