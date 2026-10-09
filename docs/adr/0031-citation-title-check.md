# ADR 0031: The Citation Checker Verifies Titles, Not Just Section Numbers

## Status

Accepted

## Date

2026-10-09

## Context

`tools/check-ostep-citations.ps1` (ADR 0030's closing recommendation) parses every `§N.M` in the repo and resolves it against `tools/ostep-sections.json`. It caught four real defects on its first run and now runs clean.

That check answers one question: *does this section exist?* A number can exist and still be the wrong number. The repo is full of citations of that shape, and none of them are caught by the existence check, because every one of them names a real section.

## The defect class the first check could not see

A second check now runs over every citation that quotes a title: `§9.6 "Stride Scheduling"`. If the quoted title is the title of a *different* section, the number and the title cannot both be right.

On its first run it found twelve such citations. Every one was off by one, and in every one the **title was correct and the number was wrong**:

| Cited | Actually | File |
|---|---|---|
| §9.3 "Stride Scheduling" | §9.6 | `ProportionalScheduler.cs` |
| §20.4 "A Memory Trace" | §18.5 | `PageDirectory.cs` |
| §21.4 "Page-Fault Control Flow" | §21.5 | `Pager.cs` (×2), `Swap.cs` |
| §18.4 "Where Are Page Tables Stored?" | §18.2 | `VirtualAddress.cs` |
| §38.3 "RAID Level 0: Striping" | §38.4 | `Raid.cs` |
| §38.4 "RAID Level 1: Mirroring" | §38.5 | `Raid.cs` |
| §38.7 "RAID Level 4: Saving Space With Parity" | §38.6 | `Raid.cs` |
| §38.8 "RAID Level 5: Rotating Parity" | §38.7 | `Raid.cs` |

Each was fixed by changing the **number** and keeping the title, because the chapter PDFs confirm the title.

## Why off-by-one is the characteristic error here

Chapters that end on a Summary produce a uniform drag: when a reader cites "the section about RAID 5" and then has to pick a number, the number most often lands one slot early or late relative to the title. `§38.4 "RAID Level 1: Mirroring"` is off by one because §38.3 ("How To Evaluate A RAID") sits between them. Reading the file does not surface this. The quoted title is a *correct* English phrase about RAID 1, so the sentence reads fine; only the number is wrong, and a number alone is exactly the thing the eye slides over.

ADR 0030 already recorded that a plausible number in a plausible chapter is what defeats a careful read. The title check works because the quote acts as an **independent witness**: it was copied from somewhere, and if it was copied from the wrong place, the copy disagrees with the number.

## Three ways a quoted title can legitimately disagree

Distinguishing a defect from correct usage turned out to be the whole difficulty. A quoted title is legal when:

1. **It is the section's own title.** Handled directly.
2. **It is a sub-heading inside the section.** §42.3 "Tricky Case: Block Reuse" is a sub-heading of §42.3 "Solution #2: Journaling (or Write-Ahead Logging)", not a section. Confirmed against `file-journaling.pdf`.
3. **It is prose quoted from the section, not a title at all.** §54.4 "drastically slow down" is a phrase, and reading it as a heading produces nonsense.

Case 3 needs a discriminator, and the obvious one is capitalisation: OSTEP section titles and sub-headings are Title Case, prose quotations generally are not. The check therefore requires at least 60% of the quoted words of length > 2 to begin with a capital, and at least two capitalised words, before it treats the quotation as a heading. Without that gate, `§22.5 "implementation"` is flagged against §9.3 "Implementation" — a false positive caught on the first run.

Two cases are errors:

4. **The title belongs to a different section.** Reported as "number and title disagree". Both cannot be right.
5. **The quoted sub-heading lives in a different section.** Same report shape.

## What the index now carries

`tools/ostep-sections.json` gained a `subheadings` map: 47 verified sub-heading titles, each recorded with the section that contains it, transcribed from the chapter PDFs. Without it, case 2 would be indistinguishable from case 4 and every legitimate sub-heading reference would be flagged.

This is also why the check must never consult a sub-heading as if it were a section. TRIM is the standing example: it is an ASIDE inside §44.8, and citing it as §44.12 (the Summary) was one of ADR 0030's recorded corrections. `Ssd.cs` now cites the ASIDE's owning section and says so in the comment.

## Verification

Both passes were checked by planting known errors and confirming exit 1, then reverting and confirming exit 0:

- Existence pass: §40.9 and §57.9 planted in `CONTEXT.md` — both flagged.
- Title pass: §38.4→§38.3 and §38.6→§38.7 regressions planted in `Raid.cs` — both flagged as "number and title disagree".

Final state: 3,057 section references across 200 files, 209 of them quoting a title, all consistent. 124/124 tests pass, build clean.

## Consequences

The check is wired into `AGENTS.md` as a pre-commit step for any diff touching `.md` or a `.cs` comment. It is a single command and it fails the run, so there is no excuse not to run it.

## What it still cannot do

Stated plainly, because the value of this check is entirely in knowing where its edge is:

- **It does not check prose quotations.** §54.4 "drastically slow down" is never verified against §54.4. Quoting accuracy is a separate axis from citation accuracy, and ADR 0030 found defects on both.
- **It does not check whether a section contains the attributed content.** A correct title on the wrong section passes: `§42.3 "Recovery"` is both a real section and a real sub-heading of it, and nothing here can tell whether the sentence's claim about *recovery* is right.
- **The Title-Case heuristic is a heuristic.** A prose quotation that happens to be Title Case will be read as a heading and may be flagged; a sub-heading written in lower case will be missed. Both failure modes are quiet.

Those three gaps need the chapter PDF and a human. What this check removes is the class of defect that survives reading.

## References

- ADR 0030 — citations are verified against the PDFs, not recalled
- `tools/check-ostep-citations.ps1`
- `tools/ostep-sections.json`