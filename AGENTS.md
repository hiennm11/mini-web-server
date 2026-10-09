## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `hiennm11/mini-web-server`. See `docs/agents/issue-tracker.md`.

### Triage labels

This repo uses the default mattpocock/skills triage labels. See `docs/agents/triage-labels.md`.

### Domain docs

This repo uses a single-context domain-doc layout. See `docs/agents/domain.md`.

### Verifying before you commit

```powershell
dotnet run --project tests/MiniWebServer.Host.Tests   # 124 assertions
pwsh tools/check-ostep-citations.ps1                  # every §N.M must resolve
```

Both run in CI (`.github/workflows/checks.yml`) on every push and PR to `main`, so a
failure is not something you have to remember to look for. Run them locally before
pushing anyway — CI is the backstop, not the feedback loop.

The citation check is not optional when a diff touches `.md` or a `.cs` comment.
This repo cites ~3,000 OSTEP section references, and ADR 0030 records dozens that
were wrong. It runs two passes: every `§N.M` must name a section that exists, and
every citation that quotes a title must quote the title of *that* section or of a
sub-heading inside it. That second pass caught a run of off-by-one errors where the
quoted title was right and the number was off by one — a defect shape no amount of
reading catches, because the quote reads like a citation.

`tools/ostep-sections.json` is transcribed from the chapter PDFs; note that the free
`toc.pdf` covers Chapters 2–51 only, and the security chapters 52–57 are absent from it.
Sub-headings are indexed separately and must never be cited as `§N.M` of their own.

It does not check prose quotations, and it cannot tell whether a section holds the
specific content attributed to it. A correct title on the wrong section still passes.
For that, open the chapter PDF.

### Skills

This repo uses Matt Pocock's engineering skills. The main flow is **idea → ship**:

| Stage | Skill |
|---|---|
| Sharpen the idea | `/grill-with-docs` (in this repo; stateless `/grill-me` if no repo context) |
| Settle a hard design question | `/prototype` (+ `/handoff` to keep the throwaway on a side branch) |
| Multi-session build | `/wayfinder` → `/to-spec` → `/to-tickets` → `/implement` |
| Single-shot build | `/implement` (drives `/tdd` internally) |
| Review the diff | `/code-review` (Standards + Spec axes) |
| Domain vocabulary drift | `/domain-modeling` |
| Hard bug, no obvious cause | `/diagnosing-bugs` |
| Pile of incoming issues / PRs | `/triage` |
| Codebase health sweep | `/improve-codebase-architecture` |

For everything else (resolving merge conflicts, writing docs for agents, setting up pre-commit, research delegation, etc.), use `/find-skills` to discover — the skill list lives in `skill://` and grows over time, so don't pin it here.

**Context hygiene**: keep grilling → spec → tickets in one unbroken window. `/compact` at phase boundaries, not mid-phase. Each `/implement` starts fresh off its ticket.
