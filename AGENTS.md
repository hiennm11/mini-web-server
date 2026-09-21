## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `hiennm11/mini-web-server`. See `docs/agents/issue-tracker.md`.

### Triage labels

This repo uses the default mattpocock/skills triage labels. See `docs/agents/triage-labels.md`.

### Domain docs

This repo uses a single-context domain-doc layout. See `docs/agents/domain.md`.

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
