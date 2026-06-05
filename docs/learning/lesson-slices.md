# Lesson Slice Model

## Purpose

A lesson slice is the smallest learning-and-building unit in this repo. It maps one OS concept to one small server behavior change, sized for one focused session (30–90 minutes).

Lesson slices exist because milestones are too large for daily learning work. A milestone says where the server is going; a lesson slice says what to build and observe today.

## Core Philosophy

**Don't study chapters. Study phenomena visible in the server.**

OSTEP is the explanation source. `mini-web-server` is the lab. The correct flow:

```text
Observe pain → Learn OS concept → Build tiny change → Run experiment → Write note
```

Not:

```text
Read 50 pages → try to remember → code blindly
```

Every slice must answer three questions:

1. **What is the OS doing?** — the observable operating-system behavior.
2. **Which .NET API exposes it?** — the concrete C# mechanism.
3. **Where does it break at scale?** — the limit that motivates the next slice.

If all three can be answered, the slice succeeded.

## Standard Session Cadence

One slice, one session, 60–90 minutes:

```text
10m  Review previous note
10m  OSTEP concept via NotebookLM query
15m  Design experiment (what to observe, which commands)
25m  Build the small change (no refactoring)
15m  Run experiment + observe
10m  Write learning note
```

## The Learning Cycle

### Step 1: Start with a question

Every slice begins with a concrete, observable question. Example:

> Why does a request to `/slow` block a request to `/` in the current server?

The question must be specific and verifiable with commands.

### Step 2: Query NotebookLM

Query the OSTEP NotebookLM notebook (`74bcbca0-6161-48cd-92bb-9dd39032794e`):

> Explain process states Running/Ready/Blocked for a single-threaded blocking web server.

Extract 3–5 key points. Do not read chapters end-to-end.

### Step 3: Design the experiment before writing code

Define:

- What command(s) to run.
- What output to expect.
- What question the observation answers.

If you don't know what to observe, do not code yet.

### Step 4: Build a minimal change

One slice = one behavior. Do not:

- Refactor unrelated code.
- Add "nice-to-have" improvements.
- Optimize for performance or abstraction.

### Step 5: Run the experiment

Execute the commands. Compare actual output against expected. Note any surprise.

### Step 6: Write the learning note

Short. Structured:

```markdown
## What changed
## What I observed
## OSTEP concept
## .NET mechanism
## Next question
```

Commit the note with the code change.

## Role Split

### Plan Agent (orchestrator)

- Queries NotebookLM for concept mapping.
- Designs experiment.
- Writes ADR and slice plans.
- Writes learning notes after observation.
- Does not code.

### Build Agent

- Implements the slice code.
- Runs build and tests.
- Fixes compilation errors.
- Commits the change.

### Learner (you)

Three responsibilities only:

1. Run the experiment.
2. Say what you observed.
3. Ask follow-up questions when the concept hasn't landed.

The observation step is where real learning happens. Do not outsource it.

## Rule Checklist — Every Slice Must Pass

Before a slice is considered complete, verify all rules:

- [ ] Rule 1: One slice, one concept. Two concepts = two slices.
- [ ] Rule 2: One commit per slice. No bundling.
- [ ] Rule 3: No refactoring unless it enables observation.
- [ ] Rule 4: No early optimization. Solve today's problem, not tomorrow's.
- [ ] Rule 5: No abstraction that hides OS behavior. Console output and socket lifecycle must stay visible.
- [ ] Rule 6: Every behavior must be visible in console, log, or command output.
- [ ] Rule 7: Start with a question. Do not build without knowing what to observe.
- [ ] Rule 8: Design the experiment before writing code.
- [ ] Rule 9: Run the experiment and write the note before claiming completion.
- [ ] Rule 10: The three-question test passes (What is the OS doing? Which .NET API? Where does it break?).

## Structure

A lesson slice file name follows this pattern:

```text
docs/learning/slice-M.N-short-name.md
```

Where `M` is the milestone number and `N` is the slice number within that milestone.

## Template

```markdown
# Slice M.N: Name

## Question

The concrete, observable question this slice answers.

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

## Three-Question Test

1. What is the OS doing?
2. Which .NET API exposes it?
3. Where does it break at scale?

## Learning Note

After observing, write a short note. Include:

- What was built.
- What the experiment showed.
- How it connects to OSTEP.
- Next question (motivates the following slice).

## Status

- [ ] Planned
- [ ] Built
- [ ] Experimented
- [ ] Noted
```

## Relationship to Milestones

Milestones are coarse-grained capability checkpoints. They answer: where is the server evolving?

Lesson slices are the execution units inside a milestone. They answer: what to build and observe today.

A milestone is complete when:

- All its slices are built, experimented, and noted.
- Learning notes are aggregated or cross-referenced.
- `CONTEXT.md` is updated if runtime behavior changed.
- ADR is updated if a design decision changed.

## OSTEP Connection

The OSTEP NotebookLM notebook (`74bcbca0-6161-48cd-92bb-9dd39032794e`, 69 sources) is the concept source.

Before building any slice, query it for the relevant chapter context. Use the output to fill the OSTEP Context section of the template.

Do not read OSTEP chapters end-to-end before building. Let the code pull concepts from the book, not the other way around.
