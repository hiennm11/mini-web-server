# Milestone 21: Fast File System (FFS) Locality

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

Why is the original UNIX file system so slow, and what placement policies make file access faster? What does "disk-aware" allocation mean?

## Scope

A standalone FFS (Fast File System) placement simulator that demonstrates OSEP Ch. 41's core ideas:
- **Block groups / cylinder groups** (OSEP §41.3): divide the disk into N groups so that related files can be placed in the same group, reducing seek distance.
- **Locality policies** (OSEP §41.4): directories go to a group with the most free inodes; files go in the same group as their parent directory.
- **Large-file exception** (OSEP §41.6): files larger than `N` blocks (default 12, matching OSEP §41.7's worked example) rotate across groups in `N`-block chunks to avoid filling any one group.
- **Filespan + dirspan metrics** (OSEP §41.7 questions 3 + 5): measure how spread out a file / directory is.

Exposed via `/ffs/run?scenario=manyfiles|largefile&groups=N&inodes=N&blocks=N&threshold=M` HTTP route.

## Slice

- **[s1-ffs-placement.md](./s1-ffs-placement.md)** — `FFS` simulator with block-group allocation, filespan + dirspan metrics, two demo scenarios.

## OSEP coverage

- **Ch. 41 Locality and The Fast File System** (§41.3 Organizing Structure: Cylinder Group; §41.4 Policies; §41.6 Large-File Exception; §41.7 Other innovations — sub-blocks + parameterized placement not modeled).

OSEP §41.1 "The Problem: Poor Performance":
> "the main issue was that the old UNIX file system treated the disk like it was a random-access memory; data was spread all over the place without regard to the fact that the medium holding the data was a disk, and thus had real and expensive positioning costs."

OSEP §41.4 "Policies":
> "The basic mantra is simple: keep related stuff together (and its corollary, keep unrelated stuff far apart). ... For files, FFS does two things. First, it makes sure (in the general case) to allocate the data blocks of a file in the same group as its inode, thus preventing long seeks between inode and data ... Second, it places all files that are in the same directory in the cylinder group of the directory they are in."

OSEP §41.6 "Large-File Exception":
> "After some number of blocks are allocated into the first block group (e.g., 12 blocks, or the number of direct pointers available within an inode), FFS places the next 'large' chunk of the file (e.g., those pointed to by the first indirect block) in another block group."

OSEP §41.7 (homework question 3) "filespan":
> "the max distance between any two data blocks of the file or between the inode and any data block."

OSEP §41.7 (homework question 5) "dirspan":
> "the spread of files within a particular directory, specifically the max distance between the inodes and data blocks of all files in the directory and the inode and data block of the directory itself."

## OSEP §-specific deviations

| OSEP § | We do | We defer |
|---|---|---|
| §41.3 cylinder groups | `BlockGroup` with bitmap for inodes + bitmap for data. | Real cylinder geometry (we use linear block IDs as the seek-distance proxy). |
| §41.4 directory policy | "low number of allocated directories + high number of free inodes" → score = `freeInodes*100 - dirCount*50`. | FFS's actual heuristic (includes free data blocks too). |
| §41.4 file policy | Files land in parent's group. | Per-subdirectory balancing (we don't have a parent-name → inode mapping; `Dirspan` uses `name.StartsWith(prefix)`). |
| §41.6 large-file exception | First 12 blocks in parent's group, then rotate to next group with free blocks. | Per-indirect-block rotation (OSEP §41.7's actual implementation); we use a simple threshold parameter. |
| §41.7 sub-blocks | Not modeled. | 512-byte sub-blocks + copy-on-grow. |
| §41.7 parameterized placement | Not modeled (we don't simulate disk geometry). | Sector-stagger layout to avoid track rotation misses. |
| §41.7 filespan / dirspan | Implemented. | None. |

## Key OSEP quotes

> "The problem: performance was terrible. As measured by Kirk McKusick and his colleagues at Berkeley [MJLF84], performance started off bad and got worse over time, to the point where the file system was delivering only 2% of overall disk bandwidth!" (OSEP §41.1)

> "The basic mantra is simple: keep related stuff together (and its corollary, keep unrelated stuff far apart)." (OSEP §41.4)

> "For files, FFS does two things. First, it makes sure (in the general case) to allocate the data blocks of a file in the same group as its inode, thus preventing long seeks between inode and data ... Second, it places all files that are in the same directory in the cylinder group of the directory they are in." (OSEP §41.4)

> "After some number of blocks are allocated into the first block group (e.g., 12 blocks, or the number of direct pointers available within an inode), FFS places the next 'large' chunk of the file (e.g., those pointed to by the first indirect block) in another block group ... This process of reducing an overhead by doing more work per overhead paid is called amortization." (OSEP §41.6 + §41.7)

## .NET mechanism

- `bool[]` arrays for in-memory inode + data bitmaps per block group (OSEP §41.3 "per-group inode bitmap and data bitmap").
- Linear scan for `AllocateBlock` / `AllocateInode` — small groups, no need for fancier allocation.
- Linear block ID = `(groupId * BlocksPerGroup) + blockIdx` as a proxy for seek distance (real FFS uses cylinder geometry).

## Files

- `src/MiniWebServer.Host/MiniScheduler/FFS.cs` (new, ~340 lines):
  - `BlockGroup` (id, inode bitmap, data bitmap, dir count).
  - `FFSFile` (name, inodeId, groupId, inodeIdx, isDir, size, dataBlocks).
  - `FfsBlockRef` (groupId, idx, globalId).
  - `FFS` simulator (allocate dirs + files, compute filespan + dirspan, format layout).
- `src/MiniWebServer.Host/Program.cs` — `/ffs/run?scenario=manyfiles|largefile&groups=N&inodes=N&blocks=N&threshold=M` route.

## What this slice does NOT do

- Real disk geometry (cylinder / track / sector) — we use linear block IDs as the seek-distance proxy.
- On-disk persistence — this is a placement simulator only; not a real file system you can mount.
- Sub-blocks (OSEP §41.7) — files are allocated in 4 KB blocks only.
- Parameterized placement (OSEP §41.7) — disk-layout optimization for sequential read.
- Symbolic links, long file names, atomic rename (OSEP §41.7 TIP) — those are FFS usability improvements, not placement policies.

## Where this leads

The roadmap continues with **M23 Security** (Ch. 53-57) — completely different topic. After M23 we return to FS work:
- **M24 RAID** (Ch. 38)
- **M25 LFS** (Ch. 43)
- **M26 Flash** (Ch. 44)
- **M27 Data integrity** (Ch. 45)
