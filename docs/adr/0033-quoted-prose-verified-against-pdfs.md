# ADR 0033: Quoted Prose Is Verified Against the PDFs, Not Paraphrased Into Quotes

## Status

Accepted

## Date

2026-10-09

## Context

ADR 0030 verified that every `§N.M` resolves and that every quoted *title* belongs to the section cited (ADR 0031 added the title check). Neither touches quoted **prose**.

The limitation was stated in ADR 0032 and left there as a known gap: *"It does not verify quoted wording... That gap needs the chapter PDF and a reader."* This ADR closes it.

The claim shape is the same one that ADR 0030 was written for. A citation's number and title being right says nothing about whether the passage in quotation marks is what the book actually says — and a paraphrased sentence placed inside quotation marks is indistinguishable from a verbatim one to every reader and to every existing check. The file compiles, the citation checker passes, and the prose reads as authoritative.

## Measurement

`grep -rio defer`-style counting was used to size the work before doing it:

| Figure | Value |
|---|---|
| Quoted strings in `docs/**.md` | 204 |
| Code strings (no citation nearby, no prose shape) | 96 |
| **Prose quotes attributed to OSTEP** | **108** |
| Distinct chapters involved | 24 |

108 quotes is not a manual task; it is a fan-out. Five read-only agents each fetched the chapter PDFs named in their bundle, grouped the quotes by chapter so each PDF was fetched once, and returned a verdict per quote with evidence.

## Verdict vocabulary

| Verdict | Meaning |
|---|---|
| `VERIFIED` | Text is in the chapter. Section number recorded. |
| `MISQUOTED` | Real OSTEP content, wrong wording. The actual wording is recorded. |
| `NOT-IN-CHAPTER` | Not in that chapter. The other chapter it belongs to is named where known. |
| `NOT-OSTEP` | Repo prose, a code string, or an external source. |
| `UNVERIFIED` | Could not be checked; the blocker is named. |

Close is not verified. Swapping *we* for *you*, dropping `on disk`, or reassigning a parenthetical example to the grammatical subject are all `MISQUOTED`, because the words on the page are what a reader is checking. Typographic normalisation — ligatures, hyphenation across a line break, curly versus straight quotes — is not a mismatch and was not flagged.

## What the audit found

Every verdict was re-checked against PDF text before any edit was made. The agents were given the instruction to report `UNVERIFIED` rather than fall back to recollection, because recollection is what produced the original defects.

Eight quotes were wrong as quotations:

| File | Defect |
|---|---|
| `m6-bounded-worker-pool/overview.md` | "If we are going to add bounded buffers to a multi-threaded program..." — **not in OSTEP at all.** §30.2 says "Because the bounded buffer is a shared resource, we must of course require synchronized access to it, lest a race condition arise." |
| `m5-race-lab/overview.md` | "A lock is just a variable, plus lock and unlock semantics." — a blend of two §28.1 sentences, presented as one quotation. |
| `m16-tlb/overview.md` | "When **you** want to make things fast" — OSTEP reads "When **we** want to make things fast". |
| `m16-tlb/overview.md` | "Thus, paging logically requires an extra memory reference per instruction fetch or data access." — a fusion of Ch. 19's lead-in and §18.4's sentence; present verbatim in neither. |
| `m12/overview.md` | Journal protocol quote ended "…to their final locations." OSTEP ends "…to their final locations **on disk**", and the ellipsis hid an intervening sentence. |
| `s6-journal-multi-block.md` | "**Linux ext3 does not** commit each update to disk one at a time" — OSTEP's subject is "some file systems", with ext3 as a parenthetical example. |
| `m23-pkcrypto/s1-pkcrypto.md` | "If someone hands you..." — OSTEP reads "**What if** someone hands you...". |
| `m23-at-rest-encryption/s1-at-rest.md` | Quotation closed early, dropping "before it runs the password through the one-way cryptographic hashing algorithm". |

Four section attributions were wrong — the quote was real, the pointer was not. `m25-lfs` attributed its lead-in sentence to §43.1 (it precedes that heading); `m23-pkcrypto` attributed a §56.2 TIP to §56.3 in its own lead-in line; `m16-tlb` attributed two chapter lead-in sentences to §19.1; `m26-ssd` attributed a §44.7 sentence to §44.8. These are corrected in place and named in the text as lead-ins, which is what they are.

## The near-misses that were left alone

The audit also produced verdicts that looked like findings and were not:

- `m24-raid` defers I/O scheduling and background scrubbing. M33 and M37 shipped both capabilities — and `Raid.cs` references neither. **A capability existing is not a deferral closing.**
- `m23-totp` quotes a passage attributed to Ch. 56. It is verbatim in Ch. 54 §54.5. The bundle's chapter assignment was wrong; the repo was right.
- `m23-rbac` quotes a least-privilege line under §53.4. The bundle sent it to Ch. 54; the repo's own attribution was correct.

These are recorded because an audit that only reports confirmations is not reporting. The measure of the pass is that a wrong verdict would have been as damaging as a missed one.

## Decision

**A quoted passage is either what the book says or it is not quoted.** Where the repo's wording is a paraphrase, the fix is the book's wording — not removing the quotation marks and hoping. Where a passage is genuinely the repo's own prose, it is stated as prose with no quote marks and no OSTEP section.

**A quote that sits in the chapter lead-in is labelled as a lead-in**, not with the section number of the section that follows it. The distinction matters because the lead-in is not part of any `§N.M`, and a citation checker that resolves `§43.1` cannot tell the reader that the sentence is above the heading.

**Verbatim means verbatim.** The audit flagged single-word substitutions and dropped trailing clauses. It did not flag ligatures, hyphenation, or quote-style variation — a pass rate of 100% on those would mean the check was measuring typography, not truth.

## Consequences

### Positive

- Eight quotations now say what OSTEP says, and four point at the right place in the book.
- The audit is repeatable. The method is: enumerate quoted strings, drop the ones with no citation nearby and no prose shape, group by chapter, fetch each chapter PDF once, compare.

### Negative

- 96 of the 204 quoted strings are code strings and were classified heuristically. The heuristic is a keyword list, and it could misfile a prose quote that happens to contain a code-ish token. Every `MISQUOTED` and `NOT-IN-CHAPTER` verdict was therefore confirmed by hand against PDF text before editing.
- This cannot run in CI. It needs network access and a PDF text extractor, and a CI failure caused by the upstream site being unreachable is worse than no check. The checker remains section-and-title only, and its own output still says so.
- Fetching 24 chapter PDFs per pass is slow — roughly four minutes per agent. Cheap enough to run before a release, not cheap enough to run on every commit.

### Neutral

- No code changed. 124/124 tests unaffected.
- Nine files edited: `m6-bounded-worker-pool/overview.md`, `m5-race-lab/overview.md`, `m16-tlb/overview.md`, `m12-mini-file-system/overview.md`, `m12-mini-file-system/s6-journal-multi-block.md`, `m23-pkcrypto/s1-pkcrypto.md`, `m23-at-rest-encryption/s1-at-rest.md`, `m25-lfs/overview.md`, `m26-ssd/overview.md`, plus this ADR. Two drafts of this line said "ten files" and then named fewer; `git diff --stat` is the reliable count, and it was run before this line was written.

## References

- ADR 0030 — section citations are verified against the chapter PDFs, not recalled
- ADR 0031 — a quoted title must belong to the section cited
- ADR 0032 — CI runs the checks; the stated gap this ADR closes
- `https://pages.cs.wisc.edu/~remzi/OSTEP/` — v1.10 chapter PDFs