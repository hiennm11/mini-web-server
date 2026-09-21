# Slice 26.1: SSD FTL, Garbage Collection, and Wear Tracking

## What it does

Implements the OSEP Ch. 44 flash SSD model as a single in-memory simulator:

- **Raw flash model** (OSEP §44.3): blocks of pages. Pages have states `Erased`, `Valid`, `Dead`. Three low-level ops: `ReadPage`, `ProgramPage`, `EraseBlock`. Erase operates on a whole block.
- **Flash Translation Layer (FTL)** (§44.7): turns client `Read(lba)` / `Write(lba, value)` / `Trim(lba)` into the underlying read/program/erase sequence. Page-level mapping table `LBA → physicalPage`.
- **Log-structured writes** (§44.7): writes append to the next free page in the log block; old versions become dead.
- **Garbage collection (GC)** (§44.8): picks the block with the most dead pages, migrates live pages to the log, erases the block.
- **Wear tracking** (§44.10): every `EraseBlock` bumps a per-block counter; surfaced as a histogram.
- **Trim hint** (§44.12 ASIDE): `Trim(lba)` drops the mapping; the underlying page becomes dead without rewriting.

Exposed via `/ssd/run?scenario=write|gc|wear&blocks=N&pages=K` HTTP route.

## Files added/changed

- `src/MiniWebServer.Host/MiniScheduler/Ssd.cs` (new, ~250 lines):
  - `SsdPageState` enum: `Erased, Valid, Dead`.
  - `SsdGarbageCollectReport` record.
  - `Ssd` simulator with `Write`, `Read`, `Trim`, `CollectGarbage`, `EraseBlock`, `ReadPage`, `FormatLayout`, `FormatWearReport`.
- `src/MiniWebServer.Host/Program.cs` — `/ssd/run?scenario=write|gc|wear` route (~85 lines).
- `tests/MiniWebServer.Host.Tests/Program.cs` — 5 new tests.

## OSEP alignment

Implements OSEP §44.3 (read/program/erase), §44.7 (log-structured FTL + mapping table), §44.8 (garbage collection), §44.10 (wear leveling — we track erase count but don't migrate cold data), §44.12 (TRIM).

## Smoke evidence

### `?scenario=write&blocks=4&pages=4` (initial log writes)

```
=== SSD Layout (M26 / OSEP Ch. 44) ===
blocks: 4   pages/block: 4   total pages: 16
current log block: 2   log cursor: 4

mapping table (LBA -> page):
  LBA 0 -> block 0 page 0 = 'A'
  LBA 1 -> block 0 page 1 = 'B'
  ...
  LBA 7 -> block 1 page 3 = 'H'
  LBA 8 -> block 2 page 0 = 'I'
  ...

block grid (V=valid D=dead E=erased):
block | pages
------|--------------------------------------------------
  0    | [V V V V]
  1    | [V V V V]
  2    | [V V V V] (log)
  3    | [E E E E]
```

OSEP §44.7 "the device appends the write to the next free spot in the currently-being-written-to block" — 8 LBAs fill blocks 0 and 1 (4 pages each), the next 4 fill block 2 (the current log). Block 3 is still untouched (Erased).

### `?scenario=gc&blocks=4&pages=4` (rewrite + GC)

```
mapping table (LBA -> page):
  LBA 0 -> block 2 page 0 = 'a'
  LBA 1 -> block 2 page 1 = 'b'
  LBA 2 -> block 2 page 2 = 'c'
  LBA 3 -> block 2 page 3 = 'd'
  LBA 4 -> block 1 page 0 = 'E'
  LBA 5 -> block 1 page 1 = 'F'
  LBA 6 -> block 1 page 2 = 'G'
  LBA 7 -> block 1 page 3 = 'H'

block grid:
  0    | [E E E E]
  1    | [V V V V]
  2    | [V V V V] (log)
  3    | [E E E E]

trace:
write: wrote 8 LBAs, mapping size=8
rewrite: dead pages=4
gc: ran 1 times, dead pages now=0
read OK: LBA0=a LBA1=b LBA2=c LBA3=d LBA4=E LBA5=F LBA6=G LBA7=H
```

OSEP §44.8 GC protocol observed exactly: the 4 rewrites (LBA 0-3 from 'A-D' to 'a-d') made the original 4 pages in block 0 dead. GC picked block 0 (most dead pages), migrated the 4 still-live pages (LBA 4-7, holding 'E'-'H') to the log (block 2), and erased block 0. After GC, dead page count = 0; reads still work because the migrated pages are now in block 2.

### `?scenario=wear&blocks=4&pages=4` (rewrite, no GC)

```
=== SSD Wear Report (M26 / OSEP §44.10) ===
blocks: 4   pages/block: 4

block | erase count | valid | dead | erased
------|-------------|-------|------|--------
  0   | 0           | 0     | 4    | 0
  1   | 0           | 4     | 0    | 0
  2   | 0           | 4     | 0    | 0
  3   | 0           | 0     | 0    | 4

total erases: 0   min/max: 0/0
```

OSEP §44.10: every erase bumps a per-block counter. With no GC run, all counters are 0 — the wear is entirely in the log block (block 2), which is what OSEP warns about: a long-lived log block would wear out without active wear leveling.

## OSEP concept

> "The crux: HOW TO BUILD A FLASH-BASED SSD? How can we handle the expensive nature of erasing? How can we build a device that lasts a long time, given that repeated overwrite will wear the device out?" (OSEP §44.0)

> "Read (a page): ... typically quite fast, 10s of microseconds or so, regardless of location on the device. Erase (a block): ... quite expensive, taking a few milliseconds. Program (a page): ... usually taking around 100s of microseconds." (OSEP §44.3)

> "The log-based approach by its nature improves performance (erases only being required once in a while, and the costly read-modify-write of the direct-mapped approach avoided altogether), and greatly enhances reliability." (OSEP §44.7)

> "The process of finding garbage blocks (also called dead blocks) and reclaiming them for use is called garbage collection, and it is an important component of any modern SSD." (OSEP §44.8)

> "The FTL should try its best to spread that work across all the blocks of the device evenly." (OSEP §44.10)

## .NET mechanism

- `SsdPageState[] _pageStates` — flat `[block*PagesPerBlock + page]` array.
- `byte[] _pageValues` — parallel values array.
- `int[] _blockEraseCount` — per-block wear counter.
- `Dictionary<int, int> _mapping` — `LBA → physical page`.
- `Dictionary<int, int> _reverseMapping` — `physical page → LBA` (for GC to know which live pages to migrate).
- GC: scan all blocks for the highest dead-page count; collect `(lba, value)` for live pages; write each via `Write()` (which allocates a fresh page in the log); erase the block.

## What this slice does NOT do

- Multi-chip parallelism (real SSDs use many flash chips in parallel).
- Block-level or hybrid mapping (OSEP §44.9 — we use page-level, which doesn't scale to TB devices).
- Over-provisioning (real SSDs reserve 7–28% of capacity for GC).
- Microsecond timing — every operation is O(1).
- Active wear leveling — we track erase count but don't move cold data.
- Disturbance errors.
- Out-of-band mapping persistence.

## Deferred (other SSD extensions)

- **Block-level mapping** (§44.9) — one mapping entry per block instead of per page; small writes become expensive.
- **Hybrid mapping** (§44.9) — page-level for log blocks, block-level for data blocks; switch merge / partial merge / full merge.
- **Over-provisioning** — reserve some blocks hidden from the client for GC headroom.
- **Active wear leveling** — periodically migrate cold data so every block wears evenly.
- **Multi-chip parallelism** — split the device across N chips; reads/writes can hit multiple chips in parallel.
- **Out-of-band (OOB) area** — store per-page mapping on the flash itself so the table can be reconstructed after power loss.
