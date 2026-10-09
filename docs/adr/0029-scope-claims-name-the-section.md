# ADR 0029: Scope Claims Name the Section, Not the Chapter

## Status

Accepted

## Date

2026-10-08

## Context

A domain-modeling pass over the M35–M37 vocabulary turned up a repeated shape of defect, in four different documents, all of it the same shape:

**A document claims a chapter, where the code implements a section of it.**

- `CONTEXT.md` had `### Scheduling baselines (Ch. 7 — planned, M35)` opening with *"Not yet implemented"* — `BaselineScheduler.cs` had shipped with ADR 0026.
- `CONTEXT.md` had `**Free-space allocation** (OSEP Ch. 17, planned M36)` calling the allocation policy *"the gap"* — `HeapAllocator.cs` and `BuddyAllocator.cs` had shipped with ADR 0027.
- The glossary defined **Disk scheduling** as *"Ch. 7 §7.8 and Ch. 37 §37.5"*. Ch. 7 §7.8 is *"Incorporating I/O"* and Ch. 7 never names disk scheduling at all — Ch. 37 §37.5 does, alone. The phrase pointed at two chapters where only one has the concept. Dropped rather than renumbered, because the Ch. 37 section already owns the vocabulary and a cross-reference would have been a second site for the same drift.
- `CONTEXT.md`'s coverage table listed **M36 | Free-space management | Ch. 17 | ✅** and **M37 | Disk geometry | Ch. 37 | ✅**, which reads as *"the chapter is done"*. M37 implements §37.1–§37.5; the chapter's §37.6 (the summary) is prose rather than content.

And on §43.12, three documents implied M31 covered the whole section when it covers the first half:

- `CONTEXT.md`'s coverage table: **Ch. 43 (§43.3, §43.12) | ✅**
- `CONTEXT.md`'s attribution line: **§43.3 segment-size cost model + §43.12 two-CR alternating writes — M31**
- `docs/learning/README.md`: **persistence (Ch. 43 §43.3 + §43.12)**

Roll-forward — the second half of §43.12 — is deferred everywhere it is mentioned, but never at the site of the ✅. A reader checking the table would conclude the section is closed.

Meanwhile `m25-lfs/s1-lfs-segments.md` still listed **two-CR alternating writes** and **size-aware segment sizing** under "Deferred", both of which M31 delivered.

## Decision

**A coverage claim names the section that was built, not the chapter that contains it.**

Where a milestone implements part of a section, the claim says which part and says what remains. Concretely:

- **M35** → `Ch. 7 §7.3-§7.7`, not "Ch. 7".
- **M36** → `Ch. 17 §17.1-§17.4` — whole chapter, so the chapter number is fine and stays.
- **M37** → `Ch. 37 §37.1-§37.5`. The table row said bare `Ch. 37`, which reads as the whole chapter.
- **M31** → `§43.3 + §43.12 first half`, with roll-forward named as still open.

**Deferral lists are annotated when a later milestone closes an entry**, rather than left as written. A deferral list is a snapshot; if it is not marked, it becomes a false claim the moment the deferred work lands. M25's list now carries a header naming M31 and the two entries it closed.

**ADR 0015's "single CR slot" decision is marked superseded in part.** The single CR is still what `Lfs` uses, so the decision was not reversed — but a reader finding it had no way to know that the deferral it justified was later closed by a different milestone. ADRs are append-only in practice here: the correction lives beside the original claim.

**A deferral list is retired, not left to rot.** This ADR's first draft applied the rule above to two claims and then missed the same defect class seven more times across the repo — deferral lists in `m14-pager`, `m16-tlb`, `m25-lfs/overview.md`, `m26-ssd` and `m27-integrity` still listing work that M16–M19, M28, M31, M32 and M33 had delivered. The reason the earlier passes missed them is worth recording: each was **true when written**, so no single reading contradicts itself. Only a change elsewhere in the repo invalidates them, and nothing connects a deferral list to the milestone that later closes it.

**No claim is left standing because it records what someone once thought.** The second draft of this ADR left ADR 0025's `§37.1-§37.6` in place as "planning-time scope, not a completion claim", and left M37's own range wrong. Neither distinction survived contact with the reader: the first reads as a claim about what was built, and the second was simply an error in an ADR written minutes earlier. Append-only applies to *decisions*; a *scope claim* is either true of the code or it is corrected.

## Consequences

### Positive

- **The ✅ in the coverage table is now trustworthy at the section level.** That is the table's only job, and it was overstating.
- **Deferral lists can be read as current.** The failure mode was invisible: a deferred item and a done item look identical in the file, and only the ADR numbering revealed the difference.
- **The glossary's Disk scheduling entry was deleted rather than renumbered.** Ch. 7 introduces I/O interleaving and hands disk scheduling to Ch. 37; there is no Ch. 7 half of the term. A cross-reference would have been a second site for the same drift.

### Negative

- **The coverage table gets longer and harder to scan.** `§37.2-§37.5` carries less information at a glance than `Ch. 37`, and this is the cost of not lying. Accepted.
- **This is a documentation convention with no enforcement.** Nothing checks that a ✅ row's sections match the code. It is fixed by reading, which is how the previous three drifted.
- **ADR 0021's `Lfs`-uses-one-CR point is now stated twice** — once in ADR 0015 as superseded, once in ADR 0021 as a consequence. Deliberate: ADR 0015 is what a reader searching "single CR" finds first, and without the note there it reads as current.

### Neutral

- No code changed. 124/124 tests unaffected.
- Eleven sites edited across six files: `CONTEXT.md` (six — the four table rows plus the glossary entry and its `Ch. 37` cross-reference), `docs/learning/README.md`, `docs/learning/ROADMAP.md`, `docs/learning/m25-lfs/s1-lfs-segments.md`, `docs/adr/0015-m25-lfs.md`, `docs/adr/0021-m31-lfs-extensions.md`. An earlier draft of this line said "ten sites across five files" and then named six; both numbers are now reconciled against the enumeration above.
- Vocabulary added for terms the chapters use and the glossary lacked: Round Robin, time slice / quantum, convoy effect, baseline job, splitting, coalescing, allocation header, embedded free list, and the four fit strategies.
- M36's terms moved out of `Storage / Mini FS (M11, M12, M21)` into their own `Heap / free-space management (M36)` section. The old section is about on-disk filesystem structures; these are about a linear address space, and Ch. 17 splits on exactly that line.

## Source Documents

- `docs/learning/m37-disk-geometry/overview.md` — the `What this slice does NOT do` list that shows the convention already in use per-milestone, and which `CONTEXT.md` did not follow.
- ADR 0025, 0026, 0027, 0028 — the M35–M37 decisions whose scope claims this corrects.
- `docs/agents/domain.md` — the repo's single-context convention: `CONTEXT.md` at the root, not a separate `GLOSSARY.md`.