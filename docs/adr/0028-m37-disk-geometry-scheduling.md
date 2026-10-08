# ADR 0028: M37 Disk Geometry and Scheduling as the Last Milestone (OSEP Ch. 37)

## Status

Accepted

## Date

2026-10-08

## Context

ADR 0025 named Ch. 37 as M37, the last item in the roadmap table, and recorded that `ROADMAP.md` had excluded it as *"Ch. 37 buses"*. Buses are Ch. 36 §36.7-§36.9; Ch. 37 is *Hard Disk Drives*. The exclusion was a chapter-number mistake that hid a real dependency.

### The dependency

M21 (FFS) and M31 (LFS segment sizing) both treat positioning time as a constant. M31's cost model is `D = (F/(1-F)) × R_peak × T_position`, where `T_position` is a single number the caller supplies — and ADR 0021 flags twice that this "ignores disk geometry (seek curves, rotational latency as a function of position)". ADR 0027's free-space work made a third such claim, that `FreeBytes` measures what is allocatable.

Ch. 37 is where positioning time is defined. Without it, all three milestones state a caveat in prose that nothing in the repo can demonstrate or refute.

## Decision

Two simulators, because the chapter splits into two questions that share only arithmetic.

**`DriveGeometry`** — §37.2-§37.4. The parts, block-to-track decomposition, and the I/O-time arithmetic of equations 37.1 and 37.2, with both datasheet drives from figure 37.5.

**`DiskScheduler`** — §37.5. FIFO, SSTF, NBF, SCAN and C-SCAN over a queue at a known head position, reporting the order and the head travel.

### Binary units, because the chapter uses them

§37.4's dimensional analysis writes KB as 1024 bytes, and its worked figures only reproduce that way: a 4 KB read at 6 ms gives 0.66 MB/s with 2^20 and 0.68 with 10^6. A decimal implementation disagrees with the textbook by 2% on every rate it reports, which is the kind of drift this repo's other milestones were careful to avoid.

### Distance is reported as distance

§37.4's ASIDE derives the average seek over distance and says so — "it arises from a simple calculation based on average seek *distance*, not time". The route reports head travel in tracks and does not multiply it by the datasheet's average seek time, because that number is a manufacturer's average over unknown distances and §37.2 warns a full seek "would likely take two or three times longer". An earlier version did the multiplication and reported ~40,000 ms of seeking for a whole-disk span on a drive whose full seek is 8-12 ms.

### Track 0 is outermost

§37.3's layout puts "the first sectors (0 through 11)" on the outermost track, so ascending track numbers move inward. This is load-bearing: it is what makes C-SCAN's "outer-to-inner" sweep the increasing direction, and it is why C-SCAN must leave a request behind the head for the next sweep.

## Consequences

### Positive

- **Ch. 37 §37.2-§37.5 is runnable**, and every figure the chapter prints is reproduced: the 4 ms / 2 ms / 6 ms Cheetah random case, its 0.66 MB/s, the Barracuda's 13.2 ms and 0.31 MB/s, the 800/950 ms sequential figures, the 125/105 MB/s peak rates, and the 200×/300× random-to-sequential gap.
- **Three later milestones can now point at a chapter** for what `T_position` should depend on.
- **The chapter's ASIDE is corrected rather than copied.** Its N/3 comes from integrating `|x-y|` continuously; the exact discrete mean over N² track pairs is (N²−1)/(3N). The difference is 1/(3N) tracks, which is why the rule of thumb survives, and the route prints both.
- **SCAN and C-SCAN are distinguishable**, which the chapter's whole paragraph about them requires and which an earlier implementation — where the two shared a switch branch — made impossible to check.

### Negative

- **No seek curve, so head travel stays a distance.** The chapter gives no seek-time-versus-distance relationship, so converting tracks to milliseconds is not possible from the text. Reporting distance is the honest option; anything else would be an invented constant.
- **SPTF is not implemented.** §37.5's answer to rotation-aware scheduling is deliberately excluded, for the chapter's own reason: it "is usually performed inside a drive". The route mentions it rather than modelling it.
- **The starvation claim is only partly demonstrable.** §37.5's mechanism needs an unbounded arrival stream, so a finite queue can only show that SCAN's order depends on sweep position while SSTF's depends on distance. The first version of that test asserted SCAN serves the far request first, which a finite queue cannot show; it was replaced rather than kept.
- **Track skew, multi-zone and write-back caching are named but not modelled.** §37.2 gives each a paragraph and no figures.

### Neutral

- Four scheduler defects and two arithmetic defects, all found before commit. The two that mattered most were SCAN selecting the *farthest* eligible track (the opposite of "servicing requests in order across the tracks") and NBF measuring from a reconstructed track start rather than the last serviced block.
- The Barracuda's sequential figure does not reproduce exactly: 9 + 4.17 + 952.38 = 965.55 ms against the chapter's "about 950". The Cheetah's is consistent (4 + 2 + 800 = 806, "about 800"), so the discrepancy is the chapter's rounding of one workload and not a units error. The test asserts the chapter's figure within the width its own "about" licenses and says why.

## Verification

- 8 tests, all asserting figures printed in Ch. 37:
  - equations 37.1 and 37.2 for the Cheetah, including the exact transfer time against the chapter's rounded "30 microseconds"
  - both drives' random and sequential figures and the 200×/300× gap, with tolerances sized to the chapter's own "about" and "very nearly"
  - the ASIDE's N/3 as a limit, brute-forced at N = 4, 5, 10, 17 against (N²−1)/(3N)
  - §37.5 figure 37.7: head on the inner track, requests for sectors 21 and 2, 21 first
  - SCAN takes the next track ahead rather than the farthest, and reverses when the sweep direction empties
  - NBF measures from the last serviced block, not a reconstructed track start
  - SCAN's order follows sweep position while SSTF's follows distance alone
  - C-SCAN and SCAN produce different orders, both sweeping outward-to-inward
- Smoke: four scenarios return 200; unknown scenario, policy and drive, a non-integer
  parameter, a duplicate queue block, and a non-positive `bytes` return 400.
- 124/124 tests pass (116 before M37 + 8).

## Source Documents

- `docs/learning/m37-disk-geometry/overview.md` — scope and what is deliberately excluded.
- `docs/learning/m37-disk-geometry/s1-geometry-timing-scheduling.md` — the chapter's figures, measured tables, and the six bugs.
- `docs/adr/0025-remaining-coverage-gaps-m35-m36-m37.md` — why this is a new milestone.
- `docs/adr/0021-m31-lfs-extensions.md` — the `T_position` caveat this chapter addresses.
- OSTEP Ch. 37 §37.1, §37.2, §37.3, §37.4 including its ASIDE, §37.5.