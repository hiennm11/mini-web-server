# Slice 21.1: FFS Block-Group Placement

## What it does

Implements the core FFS placement policy as a standalone simulator:

- **Block groups** (`BlockGroup`): each group has its own inode bitmap + data bitmap.
- **Directory placement** (OSEP §41.4): place directories in the group with the most free inodes (and fewest existing directories).
- **File placement** (OSEP §41.4): place a file's data blocks in the same group as its parent directory.
- **Large-file exception** (OSEP §41.6): for files larger than `N` blocks, allocate the first `N` in the parent's group, then rotate to the next group with free blocks, in `N`-block chunks.
- **Filespan** (OSEP §41.7 question 3): max distance between any two data blocks of a file (or between inode and any data block).
- **Dirspan** (OSEP §41.7 question 5): max distance between inode/data of a directory and inode/data of all its files.

Two demo scenarios:
- **`manyfiles`**: OSEP §41.7 question 4. Root + `/a` + `/b` dirs, files `/a/c`, `/a/d`, `/a/e`, `/b/f` each 2 blocks.
- **`largefile`**: OSEP §41.7 example. Single file `/a` of 30 blocks; demonstrates the large-file exception.

## Files added/changed

- `src/MiniWebServer.Host/MiniScheduler/FFS.cs` (new, ~340 lines):
  - `BlockGroup` (id, inode bitmap, data bitmap, dir count).
  - `FFSFile` (name, inodeId, groupId, inodeIdx, isDir, size, dataBlocks).
  - `FfsBlockRef` (groupId, idx, globalId).
  - `FFS` (the simulator; `CreateDir`, `CreateFile`, `Filespan`, `Dirspan`, `FormatLayout`).
- `src/MiniWebServer.Host/Program.cs` — `/ffs/run` route.

## OSEP alignment

Implements OSEP §41.3 (cylinder groups), §41.4 (placement policies), §41.6 (large-file exception), and §41.7 homework (filespan + dirspan metrics).

## Smoke evidence

### `scenario=manyfiles` (OSEP §41.7 question 4)

```
=== FFS Layout ===
groups: 8  inodes/group: 5  blocks/group: 16  large-file threshold: 12

group | inode-usage | data-usage
  0   | 1/5 (dirs=1)  | 0/16        ← /
  1   | 4/5 (dirs=1)  | 6/16        ← /a + /a/c + /a/d + /a/e
  2   | 2/5 (dirs=1)  | 2/16        ← /b + /b/f
  3-7 | 0/5 (dirs=0)  | 0/16        ← empty

/a/c -> [g1:b0,g1:b1]   filespan = 1
/a/d -> [g1:b2,g1:b3]   filespan = 1
/a/e -> [g1:b4,g1:b5]   filespan = 2
/b/f -> [g2:b0,g2:b1]   filespan = 1

/a dirspan = 5
/b dirspan = 1
```
Matches OSEP §41.4's claim: files in the same directory land in the same group as the directory (`/a/c`, `/a/d`, `/a/e` all in group 1). Empty groups 3-7 are exactly what FFS hopes for: unrelated stuff stays far apart.

### `scenario=largefile&threshold=12` (OSEP §41.7 example)

```
group | data-usage
  0   | 12/16     ← first 12 blocks of /a
  1   | 12/16     ← next 12 blocks (large-file exception kicks in)
  2   |  6/16     ← last 6 blocks
  3-7 |  0/16

/a -> [g0:b0..b11, g1:b0..b11, g2:b0..b5]   filespan = 37
```
First 12 blocks in group 0, then 12 in group 1 (large-file rotation), then 6 in group 2.

### `scenario=largefile&threshold=4` (smaller chunks)

```
/a -> [g0:b0..b3, g1:b0..b3, ..., g7:b0..b1]   filespan = 113
```
Smaller threshold → file spreads across all 8 groups → much bigger filespan (113 vs 37). Demonstrates OSEP §41.7 amortization: larger chunks → smaller filespan → better sequential read performance.

## OSEP concept

> "the main issue was that the old UNIX file system treated the disk like it was a random-access memory; data was spread all over the place without regard to the fact that the medium holding the data was a disk, and thus had real and expensive positioning costs." (OSEP §41.1)

> "The basic mantra is simple: keep related stuff together (and its corollary, keep unrelated stuff far apart)." (OSEP §41.4)

> "After some number of blocks are allocated into the first block group (e.g., 12 blocks, or the number of direct pointers available within an inode), FFS places the next 'large' chunk of the file (e.g., those pointed to by the first indirect block) in another block group ... This process of reducing an overhead by doing more work per overhead paid is called amortization." (OSEP §41.6 + §41.7)

## .NET mechanism

- `bool[]` arrays for in-memory inode + data bitmaps per block group (OSEP §41.3 "per-group inode bitmap and data bitmap").
- Linear scan for `AllocateBlock` / `AllocateInode` — small groups, no need for fancier allocation.
- Linear block ID = `(groupId * BlocksPerGroup) + blockIdx` as a proxy for seek distance.

## What this slice does NOT do

- Real disk geometry (cylinder / track / sector) — we use linear block IDs.
- On-disk persistence — this is a placement simulator only.
- Sub-blocks (OSEP §41.7) — 4 KB blocks only.
- Parameterized placement (OSEP §41.7) — sector-stagger optimization.

## Deferred (other FFS extensions)

- **Long file names** (OSEP §41.7 TIP) — usability improvement.
- **Symbolic links** (OSEP §41.7) — link abstraction.
- **Atomic rename** (OSEP §41.7 TIP) — transactional directory update.
- **Sub-block allocation** (OSEP §41.7) — small-file space efficiency.
