# ADR 0030: Section Citations Are Verified Against the Chapter PDFs, Not Recalled

## Status

Accepted

## Date

2026-10-09

## Context

Seven agents audited this repo's claims against the code, then against the OSTEP chapter PDFs. Roughly 160 claims were wrong. About 90 of them were provable by reading `src/`; **about 55 were not** — they were claims about the textbook, and nothing in the repository could check them.

The section references looked plausible because a plausible number is easy to produce. `§40.7` for a multi-level inode index, `§44.12` for TRIM, `§54.10` for a Linux login rule, `§36.7-§36.9` for buses. None of those numbers is random — each one is a number that exists in the *same chapter*, or looks like it should. That is what made them survive four commits of proofreading by the person who wrote them.

### The distribution of failure

Not random. Errors clustered at chapter boundaries and at the boundary between a section and an unnumbered box:

| Kind | Example | Reality |
|---|---|---|
| **NONEXISTENT** | §54.10, §40.9, §40.10, §40.12, §18.7, §43.0, §44.0 | Ch. 54 ends at §54.8; Ch. 40 at §40.8 |
| **UNNUMBERED BOX** | TRIM → §44.12; Linux login → §54.10; security enclaves → "§53 TPM"; WAFL → §43.13 | TRIM is an ASIDE inside §44.8; the login rule is an ASIDE inside §54.5 |
| **CONTENT ELSEWHERE** | nonces → §56.5; timing-safe compare → Ch. 56; hybrid encryption → §56.3; filespan/dirspan → §41.7 | Ch. 56 never says "nonce"; neither term is defined in any numbered section |
| **OFF-BY-ONE** | §20.1–§20.4 all four titles wrong; §21.6 "Behind the Scenes: Full VMM" | §20.3 is the one implemented; §21.6 is "When Replacements Really Occur" |
| **PROPAGATED** | "buses are §36.7-§36.9" in six files | §36.7 is the driver abstraction; buses are §36.1 |

The propagation case is the one that should worry a reader most. ROADMAP.md's exclusion list said "Ch. 36-37 device drivers & buses". Four milestones later I copied that into ADR 0024, ADR 0025, ADR 0028 and two learning docs without opening the chapter. **A claim copied verbatim is a claim never checked.**

### Why five commits said "I cannot verify this"

The OSTEP PDFs are public at `pages.cs.wisc.edu/~remzi/OSTEP/` and the read tool fetches them. Across five commits I wrote *"no OSTEP PDF is vendored here, so I could not confirm it"* and left ~55 citations standing. That is true about the repository and false about my capabilities, and I chose the sentence that let me stop.

I also got two things backwards by reasoning from one grep result rather than the file: I "corrected" `Tlb.Flush()` to a single `Flush(int?)` overload when both exist, and I "corrected" M37's range to `§37.2-§37.5` when §37.1 is implemented. Both were wrong edits made with confidence.

## Decision

**A section citation is a claim about the textbook. Verify it against the textbook.**

Concretely:

1. **`toc.pdf` is the index, not the authority for the security chapters.** The v1.10 TOC ends at Chapter 51. Chapters 52–57 (Reiher's security set) exist on the site and are absent from it. Any audit reading only the TOC will call every security citation nonexistent — which is exactly the wrong answer, and several of Ch. 53–57's citations turned out to be correct.

2. **The chapter PDF adjudicates a content claim; the TOC adjudicates a number and title.** When a doc says §N.M teaches X, the TOC can confirm §N.M exists and what it is called, but only the chapter can confirm X is in it. Four M23 docs cited §56.4/§56.5/§56.6 for nonces, AEAD and HMAC — three sections that exist, none of which contains those words.

3. **An ASIDE, TIP or CRUX box has no number.** Where the content is real but unnumbered, the doc says so explicitly rather than borrowing a number. This is now the convention for TRIM, the Linux-login rule, WAFL, security enclaves, and filespan/dirspan.

4. **A claim copied from another document is a claim unverified.** Where a fact appears in several files, verifying it once is not enough — the copies need checking too, and in the buses case that is where the error lived for four milestones.

5. **No citation is left unverified with a note.** "I could not check this" was a legitimate status for one commit. It is not a resting state. If it has not been checked, it gets checked.

## Consequences

### Positive

- **The repo's citations are now grounded.** Every §N.M in `CONTEXT.md`, `docs/adr/` and `docs/learning/` resolves to a section that exists and holds the content attributed to it, or says plainly that the material has no section.
- **The check is cheap and repeatable.** `toc.pdf` settles a number in one fetch. A chapter PDF settles a content claim in one more. Two fetches per chapter is a lower cost than the wrong citation propagating.
- **Two of my own confident wrong edits were caught**, which is the argument for verifying rather than reasoning: `Tlb.Flush()` and M37's § range both survived a careful reading and died on contact with the file.

### Negative

- **This was ~55 defects that a linter would catch.** A `§N.M` token checked against a machine-readable TOC is a five-line script. It does not exist, and until it does this class of error will recur — probably within the next few milestones.
- **Citation quality is not regression-tested.** The suite asserts simulator behaviour, never documentation. Nothing fails if a future edit reintroduces §54.10.
- **The audit cost a day of agent time** to find defects that were, individually, one-line fixes.

### Neutral

- **Seven agents, ~1,200 section references checked, 22 files clean with zero findings** — including all of ADR 0001, 0002, 0012 and the entire Part IV security citation set in CONTEXT.md apart from the §55.4/§56.6 strays.
- **Some suspected claims were confirmed correct and left alone**: §53.4 is Saltzer-Schroeder as the repo says; §54.4 really does cover salted hashed passwords; §54.7 really does cover sudo; §37.3's "outermost track contains the first sectors (0 through 11)" really does make track 0 outermost, which I had doubted.
- **Two quotes were right in section and wrong in wording** — §38.6 prints "It turns out the simple function XOR does the trick quite nicely", not "the fundamental insight". Quotation accuracy is a separate axis from citation accuracy, and it failed on the same axis.
- **ADR 0029 now carries a correction to its own correction.** It claimed §7.8 was "Tips"; it is "Incorporating I/O". Tips is back-matter. I used the false claim to justify deleting a glossary entry — the entry was still right to delete, for a different reason.

## Verification

- 124/124 tests pass; no code changed in this ADR's work beyond one comment in `UserStore.cs` that cited a nonexistent §54.10.
- Sweep greps for `§40.9`, `§40.10`, `§40.12`, `§54.10`, `§53 TPM`, `§44.12 trim`, `§36.7-§36.9 buses`, `§18.7` all return zero.

## Source Documents

- `https://pages.cs.wisc.edu/~remzi/OSTEP/toc.pdf` — v1.10 index, Chapters 2–51.
- `https://pages.cs.wisc.edu/~remzi/OSTEP/` — the chapter index, including the security PDFs the TOC omits.
- ADR 0029 — the same lesson for code-shaped claims, and the record of the corrections I made wrongly.
- `docs/learning/README.md` and `docs/learning/ROADMAP.md` — the exclusion lists where two of the propagated errors began.