# ADR 0025: Remaining OSTEP Gaps as Three New Milestones (M35-M37)

## Status

Accepted

## Date

2026-10-08

## Context

Every milestone through M34 is implemented and the roadmap table is closed. A coverage audit against the official OSTEP v1.10 table of contents found 10 content chapters without code, and — more usefully — found that two of them had been misrecorded rather than merely omitted.

### Two misrecorded chapters

**Process API is Ch. 5, not Ch. 7.** `CONTEXT.md`, `ROADMAP.md`, and ADR 0008 all named the unimplemented `fork`/`exec`/`wait` chapter "Ch. 7 Process API". Ch. 7 is *Scheduling: Introduction* — FIFO, SJF, STCF, response time, Round Robin, and I/O interleaving (§7.1-§7.10). Two chapters were conflated under one number, which made a real gap invisible: ADR 0006 built MLFQ (§8), lottery/stride (§9), and multi-CPU (§10) — all three of which the chapter presents as improvements over Ch. 7's baselines — while never implementing those baselines. ADR 0006's own table said "Ch. 7 not covered" and its Consequences section nevertheless claimed it "closes OSEP Part I (Ch. 7-10)".

**Ch. 17 free-space management was recorded as covered.** `CONTEXT.md` and ADR 0007 both say it is "implicitly covered by the journal (M12)". M12 has bitmaps (`TestBit`/`SetBit`/`ClearBit`) and FFS has `HasFreeBlock()` — both record *what is free*. Neither chooses a block: both scan for the first clear bit. §17.3's strategies (stack, linked list, bitmap-based) and §17.4's buddy system are about the choice, and the choice is what is missing.

**Ch. 37 was also misrecorded.** `ROADMAP.md` excluded "Ch. 37 buses". Ch. 37 is *Hard Disk Drives* — geometry and disk scheduling (§37.1-§37.5 plus a §37.6 summary). Buses are §36.1 "System Architecture", already listed as deferred under Ch. 36.

### New milestone or extend an existing one?

The repo has two established milestone shapes, and the remaining work falls on the seam between them:

| | Extends an existing M (M8-M15, M13.2/3, M23.x) | New milestone (M16-M22, M24-M27, M34) |
|---|---|---|
| Source | a § of a chapter that already has code | a chapter with no code at all |
| Oracle | assert on the existing primitive | a new route + scenario |
| ADR | one ADR covering several (0004/0006/0007) | one ADR each (0014-0024) |

M31-M33 are the recent precedent for the ambiguous middle: they extend an existing milestone (M25, M26, M27) but each takes **one § nobody had done** and gets its own ADR. That is the shape the remaining gaps need.

## Decision

Three new milestones, in this order. Each takes a chapter that has no code, gets its own ADR, and is a new milestone rather than an extension.

### M35 — Scheduling baselines (OSEP Ch. 7)

A new milestone, not an extension of M13. §7 introduces **response time**, a metric that MLFQ's rules are designed against; folding FIFO/SJF/STCF into M13.1 would bury the thing §8 is arguing with. The existing `/sched/run` harness and `Job.cs` are reused, so the cost is the policies and the metrics rather than new plumbing.

The point is comparative: with MLFQ (§8), lottery/stride (§9), and multi-CPU (§10) already built, the baselines turn three schedulers whose advantages are asserted into three schedulers whose advantages are measured. Specifically, SJF-with-partial-knowledge (STCF, §7.5) is the policy MLFQ's Rule 1 and its I/O-awareness imitate, and its starvation pathology is what §8's boost exists to fix.

### M36 — Free-space management (OSEP Ch. 17)

A new milestone. Pure data structures with no device: §17.2's three mechanisms (stack, linked list, bitmap), §17.3's strategies (first fit, best fit, next fit) with the external-fragmentation measurement that distinguishes them, and §17.4's buddy system. Small, self-contained, and it retires a false claim rather than adding a chapter.

Also corrects M12 and FFS, which are first-fit-only by accident rather than by decision — once the alternatives exist, the implicit choice becomes an explicit one.

### M37 — Disk geometry and scheduling (OSEP Ch. 37)

A new milestone, and last. §37.2's geometry (tracks, sectors, cylinders, RPM) and §37.4's transfer-time math are what M21's FFS locality and M31's §43.3 segment-size cost model both assume as constants. Modeling them turns "positioning time" from a number into a function of position, which is the caveat ADR 0021 already flags twice and cannot fix from inside a persistence simulator.

Ordering note: M36 before M37. Both are small, but M36 corrects a documentation falsehood that is currently live, while M37 is additive.

### Excluded, with reasons

Ch. 5 (Process API) — `fork`/`exec`/`wait` have no meaning in .NET. Ch. 14 (memory API) — `malloc`/`free` are the GC's problem. Ch. 16 (segmentation) — paging replaced it outright. Ch. 48-50 (distributed systems, NFS, AFS) — a different book-length topic.

Ch. 15 (address translation / base+bounds) is the one judgement call. It is the mechanism paging replaced, so a simulation would exist only to say "look at what replaced this". Recorded as excluded rather than deferred, which is the honest status: no plan exists and none is warranted.

## Consequences

### Positive

- The three remaining *internal* gaps become named milestones with the same shape as M31-M33, so `/implement` can pick one up without re-litigating scope.
- M35 retroactively justifies three existing milestones. Every MLFQ, stride, and multi-CPU claim is currently unfalsifiable because the thing they beat is absent.
- M36 converts two documentation lies ("Ch. 17 implicitly covered by M12"; "Ch. 37 buses") into either implemented work or an honest exclusion.
- The coverage number stops being an estimate. It becomes "35 of 45, and these three would make it 38" rather than "~33 of ~50".

### Negative

- Three more milestones is three more folders, three ADRs, and roughly 20 new tests. None of it changes what the web server *does*; this is lab surface, not product.
- M37 in particular is the weakest of the three on its own terms: its value is entirely in what it lets M21 and M31 claim, and it could equally be scoped as "add geometry to two existing simulators" — which is the extension shape this ADR explicitly rejects, and a future reader may reasonably disagree.
- Coverage percentages will keep drifting as ADRs are corrected. The count in `CONTEXT.md` is now sourced to the official TOC rather than estimated, but it is still a manual tally.

### Neutral

- The two misrecorded chapters were fixed in the same pass that found them (commit `d3bc7e8`), before this ADR. The ADR records the correction; it does not authorise it.

## Verification

- Coverage re-counted against `pages.cs.wisc.edu/~remzi/OSTEP/toc.pdf` v1.10: 57 numbered chapters, 12 dialogue chapters, 45 content chapters, 35 with code.
- Chapter numbers in every ADR and in `CONTEXT.md` checked against that TOC; five wrong references corrected (Ch. 7, Ch. 11, Ch. 13/14, Ch. 14, Ch. 37).
- Build clean; 98/98 tests pass at the time of writing.

## Source Documents

- `docs/learning/ROADMAP.md` — the closed milestone table and the exclusion list.
- `CONTEXT.md` §OSEP Coverage — the chapter tally and the deferred list.
- `docs/adr/0006-mini-scheduler-mlfq-proportional-multicpu.md` — M13's coverage map, whose §7 gap this ADR makes explicit.
- `docs/adr/0007-mini-pager-mmu-tlb-multilevel-replacement.md` — M14's coverage map, whose Ch. 17 claim this ADR makes explicit.

### A note on numbering

This is 0025. ADR 0005 was never created — the sequence jumped 0004 → 0006 and then ran consecutively to 0024 — so this continues from the end of the real range rather than implying eleven unrecorded decisions between 0025 and 0036.