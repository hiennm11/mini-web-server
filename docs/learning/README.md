# `docs/learning/` — read order

This directory holds the per-milestone and per-slice learning notes. Each note is a self-contained document: what was built, what was observed, what OSEP says, what's deferred.

## File order (as listed in the directory)

```
m1-raw-socket-server.md          ← milestone 1 overview
m2-http-request.md               ← milestone 2
m3-static-file-server.md          ← milestone 3
m4-thread-per-connection.md      ← milestone 4
m5-race-lab.md                   ← milestone 5
m6-bounded-worker-pool.md        ← milestone 6
m7-async-event-based.md          ← milestone 7
m12-mini-file-system.md          ← milestone 12 (FS lab)
s1.1-raw-socket-server.md        ← slice 1.1
s1.2-http-request.md             ← slice 1.2
s1.3-static-file-server.md        ← slice 1.3
s1.4-robust-request-receive.md   ← slice 1.4
s4.1-single-thread-blocking.md   ← slice 4.1
s4.2-thread-per-connection.md    ← slice 4.2
s4.3-scheduling-non-determinism.md ← slice 4.3
s4.4-shared-address-space.md     ← slice 4.4
s4.5-race-condition-prep.md       ← slice 4.5
s4.6-thread-per-connection-limits.md ← slice 4.6
s6.3-bounded-queue.md            ← slice 6.3 (M8 / bounded queue)
s9-reader-writer-lock.md         ← slice 9 (M9)
s10-threadpool-cap.md             ← slice 10 (M10)
s11-raw-syscall-demo.md          ← slice 11 (M11)
s12.1-fs-core.md                 ← slice 12.1
s12.2-inode-table.md             ← slice 12.2
s12.3-directory.md               ← slice 12.3
s12.4-http-routes.md             ← slice 12.4
s12.5-journal-single-block.md    ← slice 12.5
s12.6-journal-multi-block.md     ← slice 12.6
s12.7-rmdir.md                   ← slice 12.7
s13-mlfq.md                      ← slice 13 (M13 MLFQ)
s14-pager.md                     ← slice 14 (M14 linear paging)
s15-arraypool.md                 ← slice 15 (M15 ArrayPool)
```

## Naming convention

| Prefix | Meaning |
|---|---|
| `m{N}-*.md` | Milestone overview (umbrella doc) — covers an entire milestone |
| `s{M}.{S}-*.md` | Slice doc — a sub-deliverable within a multi-slice milestone (e.g., M1, M4, M6, M12) |
| `s{N}-*.md` | Slice doc — the sole slice in a single-slice milestone (M9, M10, M11, M13, M14, M15) |

Why two slice-naming styles?
- Multi-slice milestones need sub-numbers to distinguish slices.
- Single-slice milestones don't — the slice number is redundant, just `s{N}`.

## Recommended read order

**Foundation (concurrent raw HTTP server)**:

1. `m1-raw-socket-server.md` → `s1.1-raw-socket-server.md`
2. `m2-http-request.md` → `s1.2-http-request.md`
3. `m3-static-file-server.md` → `s1.3-static-file-server.md`
4. `s1.4-robust-request-receive.md`

**Concurrency (threads, locks, pools, async)**:

5. `m4-thread-per-connection.md` → `s4.1-…` through `s4.6-…`
6. `m5-race-lab.md`
7. `m6-bounded-worker-pool.md` → `s6.3-bounded-queue.md`
8. `m7-async-event-based.md`
9. `s9-reader-writer-lock.md` (M9)
10. `s10-threadpool-cap.md` (M10)
11. `s11-raw-syscall-demo.md` (M11)

**Persistence (Mini FS journal)**:

12. `m12-mini-file-system.md` → `s12.1-fs-core.md` through `s12.7-rmdir.md`

**Scheduling (MLFQ)**:

13. `s13-mlfq.md` (M13)

**Paging (linear page table)**:

14. `s14-pager.md` (M14)

**Performance**:

15. `s15-arraypool.md` (M15)

## Cross-references

- **OSEP chapter attribution** is noted in each slice's "OSEP concept" section.
- **Deviations from OSEP** are noted explicitly in each slice's "Implementation deviations" section (added after the OSEP alignment pass).
- **Deferred items** are listed at the end of each slice.

## Status snapshot

`docs/learning/` is the in-repo learning lab. For a higher-level overview (active work, latest commit, OSEP coverage table), see `../../CONTEXT.md`.
