# Lesson Slice Model

## Purpose

A lesson slice is the smallest learning-and-building unit in this repo. It maps one OS concept to one small server behavior change, sized for one focused session (30–90 minutes).

Lesson slices exist because milestones are too large for daily learning work. A milestone says where the server is going; a lesson slice says what to build and observe today.

## Structure

A lesson slice file name follows this pattern:

```text
docs/learning/slice-M.N-short-name.md
```

Where `M` is the milestone number and `N` is the slice number within that milestone.

## Template

```markdown
# Slice M.N: Name

## OSTEP Context

- Chapter(s): ...
- Concept: one sentence summary.
- Key quote (from NotebookLM query): ...

## C#/.NET Mechanism

- Type or API: ...
- Why this, not another abstraction: ...

## Build

Code behavior to add or change. Keep it small — one observable change.

File(s) affected:

- `src/MiniWebServer.Host/...`

Before/after code sketch or exact change description.

## Experiment

Command(s) to run. Two terminals often needed (one for server, one for client).

Expected console output and what to notice.

## Observation

Question to answer after running the experiment. Designed so the learner must interpret the output, not just read it.

## Learning Note

After observing, write a short note in the corresponding milestone learning doc or a standalone slice note. Include:

- What was built.
- What the experiment showed.
- How it connects to OSTEP.

## Status

- [ ] Planned
- [ ] Built
- [ ] Experimented
- [ ] Noted
```

## Workflow

1. Query OSTEP NotebookLM for the concept.
2. Fill the template (planning phase — read-only, no code).
3. Switch to build mode.
4. Implement the code behavior.
5. Run the experiment.
6. Write the learning note.
7. Mark the checkbox.

## Conventions

- Code changes should be minimal. One behavior per slice.
- Console output is the primary observation tool. Do not hide important state behind abstraction layers.
- Socket lifecycle must remain visible.
- After a slice is completed, commit it before starting the next slice.
