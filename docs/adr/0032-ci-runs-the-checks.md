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

## The gap: a workflow that reports is not a rule that holds

The first run of this workflow on GitHub failed in 0 seconds with no log — the signature of a file that does not parse, not a step that fails.

```
X This run likely failed because of a workflow file issue.
yaml.scanner.ScannerError: mapping values are not allowed here
  in .github/workflows/checks.yml, line 66, column 20
```

Line 66 was `- name: Smoke: app starts and serves`. An unquoted YAML scalar containing `": "` is read as a nested mapping key, so `name` received a mapping instead of a string. Quoting it fixed the parse; a scan of every `name:` and `run:` line found no other occurrence.

This is worth recording for what it says about verification. The workflow had already been checked — every CI assumption verified by running it locally — and it was still wrong. What was missing was parsing the file with a YAML parser rather than reading it.

## Testing the pull request path found something worse

A workflow that reports a failure does not prevent anything. `main` had no branch protection:

```
gh api repos/hiennm11/mini-web-server/branches/main/protection
Branch not protected (HTTP 404)
```

So the sequence was tested end to end: open a PR, plant a nonexistent section, watch the check fail, then try to merge.

The merge **succeeded**. The PR reported `mergeStateStatus: UNSTABLE`, and `gh pr merge` merged it anyway. The bad citation landed on `main`, and only then did the push run turn red — which is the checker telling you about a problem that already shipped, not stopping it.

A check that fires after merge is a report. A check that blocks merge is a control.

## Protection, and how it was verified

`main` now requires both status checks and forbids force pushes. Neither was assumed to work:

**The merge is blocked.** A PR with `citations` failing reports `mergeStateStatus: BLOCKED`, and:

```
X Pull request #2 is not mergeable: the base branch policy prohibits the merge.
```

**The force-push is rejected.**

```
remote: ! [remote rejected] main -> main (protected branch hook declined)
remote: - 2 of 2 required status checks are expected.
```

**Green PRs still merge.** The `pull_request` trigger fires correctly — `event: pull_request`, one run per push, both jobs passing — and protection adds no friction to a clean change.

## The verification that failed the first time

The first planted error was a section that exists, so the check passed and proved nothing. The probe had been wrong, not the checker. The negative test only became real with a number past the end of a chapter — and then CI went red and named the file and line.

This ADR would fail its own checker if it quoted either number literally, so it does not. That is the third time this repo has caught the same mistake: an audit record that names a wrong citation is not exempt from being one. The two audit ADRs are exempt in `check-ostep-citations.ps1`; this one is written to avoid needing the exemption.

A test that passes for the wrong reason is worse than no test, because it reports confidence. It is worth being deliberate about which failure is being demonstrated.

## What CI deliberately does not do

- **It does not lint the prose.** The 73 `DEFER` notes across `docs/learning` are recorded decisions, not debt; a check that flagged them would be flagging intent.
- **It does not verify quoted wording.** The citation check proves a section exists and a quoted title belongs to it. It cannot tell whether the section holds the specific claim, and it ignores prose quotations entirely. Both limits are stated in the script's PASS output and in ADR 0031. That gap needs the chapter PDF and a reader.
- **It does not use a second test runner.** The suite is a console application and stays one. A migration to xunit would be a different ADR.

## Consequences

A wrong citation now fails the build instead of surviving to be discovered by an audit that may never run. With branch protection, it cannot be merged either: `main` requires both checks to pass and refuses force pushes.

That closes the loop ADR 0030 could not. It found ~200 wrong citations after they had accumulated across the repo's history, and nothing would have found the next twenty. Now the twenty-first is stopped at the merge.

The cost is that `main` can go red and merges require the checks to have run. That is the intended trade: the alternative was demonstrated to permit merging a failing change.

## References

- ADR 0030 — citations are verified against the PDFs, not recalled
- ADR 0031 — the citation checker verifies titles, not just section numbers
- `.github/workflows/checks.yml`
- `tools/check-ostep-citations.ps1`