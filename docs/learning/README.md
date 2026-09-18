# `docs/learning/` — read order

This directory holds the per-milestone learning lab. Each milestone has its own folder containing:

- `overview.md` — the milestone's question, scope, OSEP concept mapping, files changed, deferred items.
- `s{N}-*.md` — slice docs (one per sub-deliverable). Numbering restarts at 1 inside each milestone folder because the milestone context is in the folder name.

## Folder structure

```
docs/learning/
├── README.md                        ← this file
├── m1-raw-socket-server/            ← foundation
├── m2-http-request/
├── m3-static-file-server/
├── m4-thread-per-connection/        ← concurrency
├── m5-race-lab/
├── m6-bounded-worker-pool/
├── m7-async-event-based/
├── m8-bounded-queue/
├── m9-reader-writer-lock/
├── m10-threadpool-cap/
├── m11-raw-syscall-demo/
├── m12-mini-file-system/            ← persistence
├── m13-mlfq/                        ← scheduling
├── m14-pager/                       ← paging
└── m15-arraypool/                   ← perf
```

## Naming convention

- Folder name: `m{N}-{short-name}/` — milestone number, kebab-case short name.
- File name: `overview.md` — the milestone overview (one per folder).
- File name: `s{N}-{short-name}.md` — slice within the milestone. `N` is the slice number within the milestone, starting at 1.

Within a folder, slices are numbered sequentially (`s1`, `s2`, ...). The milestone number is implicit from the folder.

## Recommended read order

1. **Foundation** (concurrent raw HTTP server):
   - `m1-raw-socket-server/` (overview + `s1`, `s2`)
   - `m2-http-request/`
   - `m3-static-file-server/`

2. **Concurrency** (threads, locks, pools, async):
   - `m4-thread-per-connection/` (6 slices)
   - `m5-race-lab/`
   - `m6-bounded-worker-pool/`
   - `m7-async-event-based/`
   - `m8-bounded-queue/`
   - `m9-reader-writer-lock/`
   - `m10-threadpool-cap/`
   - `m11-raw-syscall-demo/`

3. **Persistence** (Mini FS journal):
   - `m12-mini-file-system/` (overview + 7 slices)

4. **Scheduling** (MLFQ):
   - `m13-mlfq/`

5. **Paging** (linear page table):
   - `m14-pager/`

6. **Performance**:
   - `m15-arraypool/`

## Cross-references

- **OSEP chapter attribution** is noted in each `overview.md` (per-milestone) and each `sN-*.md` (per-slice).
- **Deviations from OSEP** are noted explicitly in each slice's "Implementation deviations" section (added after the OSEP alignment pass).
- **Deferred items** are listed at the end of each slice.

## Status snapshot

`docs/learning/` is the in-repo learning lab. For a higher-level overview (active work, latest commit, OSEP coverage table), see `../../CONTEXT.md`.
