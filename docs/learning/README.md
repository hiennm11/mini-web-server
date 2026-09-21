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
├── m13-mlfq/                        ← scheduling (Ch. 8)
├── m13.2-stride-lottery/            ← scheduling (Ch. 9)
├── m13.3-multicpu-scheduling/       ← scheduling (Ch. 10)
├── m14-pager/                       ← paging (Ch. 18)
├── m16-tlb/                         ← paging (Ch. 19)
├── m17-multi-level-pt/              ← paging (Ch. 20)
├── m18-replacement/                 ← paging (Ch. 21 + Ch. 22)
├── m19-complete-vm/                 ← paging (Ch. 23)
├── m20-dining-philosophers/         ← concurrency (Ch. 31.6)
├── m21-ffs/                         ← persistence (Ch. 41)
├── m22-lock-free/                   ← concurrency (Ch. 32.3)
├── m23-totp/                        ← security (Ch. 54.5)
├── m24-raid/                        ← persistence (Ch. 38)
├── m25-lfs/                         ← persistence (Ch. 43)
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

3. **Persistence** (Mini FS journal → FFS block-group placement → RAID → LFS):
   - `m12-mini-file-system/` (overview + 7 slices)
   - `m21-ffs/`
   - `m24-raid/`
   - `m25-lfs/`

4. **Scheduling** (MLFQ → proportional-share → multi-CPU):
   - `m13-mlfq/`
   - `m13.2-stride-lottery/`
   - `m13.3-multicpu-scheduling/`

5. **Paging** (linear PT → TLB → multi-level → replacement → complete VM):
   - `m14-pager/`
   - `m16-tlb/`
   - `m17-multi-level-pt/`
   - `m18-replacement/`
   - `m19-complete-vm/`

6. **Concurrency deep-dive** (dining philosophers → lock-free CAS):
   - `m20-dining-philosophers/`
   - `m22-lock-free/`

7. **Performance**:
   - `m15-arraypool/`

## Cross-references

- **OSEP chapter attribution** is noted in each `overview.md` (per-milestone) and each `sN-*.md` (per-slice).
- **Deviations from OSEP** are noted explicitly in each slice's "Implementation deviations" section (added after the OSEP alignment pass).
- **Deferred items** are listed at the end of each slice.

## Status snapshot

`docs/learning/` is the in-repo learning lab. For a higher-level overview (active work, latest commit, OSEP coverage table), see `../../CONTEXT.md`.
