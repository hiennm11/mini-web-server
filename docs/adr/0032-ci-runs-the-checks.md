# ADR 0032: CI Runs the Citation Check, Not Just the Build

## Status

Accepted

## Date

2026-10-09

## Context

ADRs 0030 and 0031 added `tools/check-ostep-citations.ps1`, which found sixteen real defects across `.md` and `.cs` files. The roadmap has been fully implemented since M37: 37 milestones, 124 assertions, clean build. There was no remaining feature work, so the question was what was actually left.

The answer was measurable rather than a matter of taste. Before this ADR:

```
no .github directory
no pre-commit wiring
.git/hooks → sample files only
grep "check-ostep-citations" → the script itself, and AGENTS.md
```

The checker existed as an instruction to an agent. It was a convention, not a control. Nothing in the repository caused it to run, and nothing would have failed if it did not.

This is not hypothetical. Writing ADR 0031 made the checker fail on ADR 0031 eleven times, and that was caught only because the author happened to re-run the checker afterwards. The next agent would not have.

## Decision

Add `.github/workflows/checks.yml`, running on push and pull request to `main`:

| Job | What it proves |
|---|---|
| `citations` | Every `§N.M` resolves and every quoted title belongs to the section cited |
| `build` | Release build with `-warnaserror`, then all 124 assertions |

The citation job runs first and separately. It is the only check whose failure mode is invisible to review: a wrong section number reads as perfectly plausible prose, and the file compiles and passes every test while being wrong. It also takes four seconds, so it never becomes a reason to skip.

## What had to be verified before trusting the workflow

A workflow file is a claim about an environment this repo has never run in. Each assumption was checked rather than assumed:

**The suite can fail CI.** `tests/MiniWebServer.Host.Tests` is a console application, not xunit — it prints `PASS`/`FAIL` per assertion and calls `Environment.Exit(1)` from the catch block in `Run()`. Confirmed by injecting a deliberate failure: exit code `1`, `FAIL parses request line: CI PROBE`. Reverted, exit code `0`. So a bare `run:` step is sufficient; no result parser is needed.

**The smoke test can actually serve.** The first draft used `--help`, which does not exist — `Program.cs` only reads `--async`, `--max-threads` and `--min-threads`. The server binds a fixed port 8080 and blocks. Running the real server and requesting `/index.html` returned HTTP 200 with a 225-byte body.

**`--warnaserror` costs nothing.** Release build under that rule: 0 warnings, 0 errors. Free now; catches the first regression later.

**`wwwroot` reaches the Release output.** The smoke test runs `--no-build` after the Release build, so it needs `wwwroot/index.html` present there. Confirmed, and `index.html` is tracked in git rather than ignored.

## The bug this ADR found in its own tooling

Writing the workflow exposed a defect in the checker that only CI would ever have hit:

```powershell
$rel = $file.FullName.Substring($Path.Length).TrimStart('\').Replace('\', '/')
```

`TrimStart('\')` removes backslashes only. On Linux the separator is `/`, so every relative path kept a leading slash, no `$rel` matched the `$exempt` list, and both audit ADRs — the two files that are *supposed* to contain wrong citations — would have been flagged. The job would have failed on its first run, on the very repository it was written for.

Fixed to `TrimStart([char[]]'\/')`, verified by running that normalisation against both a Windows-style and a Linux-style path.

The mode bit was also `100644`. On a Linux runner, `./tools/check-ostep-citations.ps1` needs `100755`, so the file mode is set in the index.

## What CI deliberately does not do

- **It does not lint the prose.** The 73 `DEFER` notes across `docs/learning` are recorded decisions, not debt; a check that flagged them would be flagging intent.
- **It does not verify quoted wording.** The citation check proves a section exists and a quoted title belongs to it. It cannot tell whether the section holds the specific claim, and it ignores prose quotations entirely. Both limits are stated in the script's PASS output and in ADR 0031. That gap needs the chapter PDF and a reader.
- **It does not use a second test runner.** The suite is a console application and stays one. A migration to xunit would be a different ADR.

## Consequences

A wrong citation now fails the build instead of surviving to be discovered by an audit that may never run. That is the entire point: ADR 0030 found ~200 of them after they had accumulated across the repo's history, and nothing would have found the next twenty.

The cost is that CI is now a thing that can be red. That is the intended trade.

## References

- ADR 0030 — citations are verified against the PDFs, not recalled
- ADR 0031 — the citation checker verifies titles, not just section numbers
- `.github/workflows/checks.yml`
- `tools/check-ostep-citations.ps1`