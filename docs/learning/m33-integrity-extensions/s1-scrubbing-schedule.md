# Slice 33.1: Scrubbing Schedule (§45.8)
> **What it does** — adds a periodic scheduler on top of the M27 one-shot scrubber. The scheduler runs every `intervalMinutes`, covers `batchSize` blocks per pass, throttles between batches so it doesn't starve foreground I/O, and reports the probability of catching latent corruption before it spreads (Poisson-arrival model from §45.8).
## Surface
- `IntegrityStore` (extended in `MiniScheduler`):
  - `Scrubber.Schedule(TimeSpan interval, int batchSize, TimeSpan throttle)` — starts a background `Task` that loops: scrub `batchSize` blocks, sleep `throttle`, repeat.
  - `Scrubber.Stop()` — cancels the schedule.
  - `static double ProbabilityOfCatch(int intervalMinutes, int batchSize, int totalBlocks, double blockMtbfHours)` — the §45.8 Poisson model.
- The comparison route reports per-config:
  - `blocksCoveredPerPass`
  - `meanTimeBetweenPasses` (interval × blocks / batchSize × totalBlocks ratio)
  - `probabilityOfCatchBeforeSpread` (the §45.8 model)
## Files
- `src/MiniWebServer.Host/MiniScheduler/Integrity.cs` — add `Scrubber.Schedule` + the probability helper (~80 LOC).
- `src/MiniWebServer.Host/Program.cs` — new route scenario.
## Smoke evidence
- `/integrity/run?scenario=scrub-schedule&interval_minutes=60&batch_size=1000&block_mtbf_hours=100000` reports a near-1 catch probability.
- `/integrity/run?scenario=scrub-schedule&interval_minutes=1440&batch_size=100&block_mtbf_hours=100000` reports a measurably lower catch probability (the §45.8 "too rare" case).
- A side-by-side comparison: weekly-nightly vs daily-1%-batch vs continuous-full — the catch-probability curve.
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds:
  - `Run("scrubber covers a fraction per pass", ...)` — assert that one pass covers exactly `batchSize` blocks.
  - `Run("scrub probability matches OSEP formula", ...)` — assert `ProbabilityOfCatch(I, B, N, MTTF)` matches a hand-calculated value for known parameters.
  - `Run("scrubber schedules and stops cleanly", ...)` — assert the background task stops on `Scrubber.Stop()` within 100 ms.
## Source documents
- `docs/learning/m33-integrity-extensions/overview.md` — milestone scope.
- `docs/learning/m27-integrity/overview.md` — predecessor M27 one-shot scrubber.
- OSEP Ch. 45 §45.8 — schedule vs probability trade-off.
