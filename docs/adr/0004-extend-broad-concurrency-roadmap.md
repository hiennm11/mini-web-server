# ADR 0004: Extend Concurrency Roadmap Beyond M7

## Status

Accepted

## Date

2026-09-17

## Context

ADR 0003 (Four-Phase OSEP Learning Roadmap) closes the original four-phase plan at milestone M7. The repo is a complete OS concepts lab for the core three pieces of OSTEP for the planned chapters, but `CONTEXT.md` "OSEP Coverage" lists several natural extensions still relevant to those chapters (Ch. 30 bounded buffer, Ch. 36 I/O devices deeper, Ch. 33 refinements) and beyond (Ch. 8-10 scheduling, Ch. 14-23 paging, Ch. 40-45 file system).

This ADR records the next concrete milestones beyond M7, in the order the repo will likely tackle them. It does not expand into the OSTEP chapters not yet on the roadmap (scheduling, paging, security). They have their own future ADRs if and when work on them starts.

## Decision

We will extend the concurrency / OSEP-chapter-30+ roadmap with the following milestones. Each milestone is sized as one or more lesson slices (30-90 min each). Status column reflects current reality.

| ID | Name | OSEP chapter(s) | Lesson slices | Status |
|---|---|---|---|---|
| **M8** | Bounded queue + backpressure in worker pool | Ch. 30, 31 | s6.3-bounded-queue (planned) | Future |
| **M9** | Reader-writer lock + shared cache demo | Ch. 30 | 2-3 slices (rwlock primitive, /stats cache, smoke) | Future |
| **M10** | Async mode overload + ThreadPool cap | Ch. 33 | 1-2 slices (cap observation, stress under cap) | Future |
| **M11** | Real `open`/`read`/`write`/`close` syscall demo | Ch. 39 | 2 slices (replace `File.ReadAllBytes` with raw `FileStream`; observable syscalls) | Future |
| **M12** | Mini file system (inode + bitmap + journal) | Ch. 40-45 | 4-6 slices (superblock, dir, create, delete, journal) | Future |

Ordering rationale:

- **M8** is a near-trivial extension of M6.1/6.2 with clear code locality. It closes the largest visible gap in the current code: the queue is unbounded.
- **M9** requires a shared resource to motivate (a cache). The `/stats` endpoint is a natural one.
- **M10** parallels M8 for the async mode. Together M8 and M10 give the repo two working models of backpressure.
- **M11** is the persistence counterpart of slice 1.3 — replacing the high-level `File.ReadAllBytes` with the lower-level syscall narrative.
- **M12** is the biggest piece; the slices deliberately mirror OSEP Ch. 40-42 chapter ordering.

These milestones do **not** change the project's status as an OS concepts lab. Each one adds observable behavior, has a smoke test, and lands as its own commit(s) following `docs/learning/README.md`.

## Out of scope for this ADR

- **Scheduling (Ch. 7-10)**: an MLFQ or lottery scheduler in user space is a natural future slice (it would be a /work route that runs a synthetic workload). It belongs in its own ADR.
- **Paging (Ch. 14-23)**: a mini paging system in user space is feasible (memory map + page table + simple TLB simulator) but is a much larger project. Defer.
- **Security (Ch. 53-57)**: out of scope for the concurrency roadmap. Belongs to its own ADR.
- **Asynchronous file I/O (Ch. 36-38)**: combined with M11.

## Consequences

Good:

- The repo has a clear path forward without changing the existing milestones' status.
- Each future milestone is scoped small enough to land as a single commit per slice.
- The `CONTEXT.md` "OSEP Coverage" table can stay as the high-level map; this ADR is the lower-level sequencing for the parts that are queued next.

Tradeoffs:

- A roadmap this far ahead of the work is a guess. The actual order may shift once each milestone reveals its next lesson.
- "Future" milestones that depend on a future milestone (e.g., M9 needing a cache that M11 should provide) introduce ordering risk.

## Verification

- A new slice doc under `docs/learning/` is the unit of work. Each is independently testable per `README.md`.
- `dotnet build MiniWebServer.sln` and the existing test project remain the green bar.
- The roadmap is complete when the Status column here is all Done. That will likely be months away; updating this ADR is a per-milestone event.

## Next Steps

1. Land M8 (bounded queue + 503) first. The detailed plan lives at `docs/learning/s6.3-bounded-queue.md`.
2. After M8, evaluate whether M10 (async-mode overload) should run before M9 (reader-writer lock + cache). Both are reasonable.
3. After the concurrency additions land, consider an ADR for Part III extension (Ch. 39-45) and a separate one for Part I extension (Ch. 7-10 scheduling).