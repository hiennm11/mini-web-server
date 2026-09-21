# Slice 25.1: LFS Segments, Inode Map, and the Cleaner

## What it does

Implements OSEP Ch. 43's core write path as a single in-memory simulator:

- **Segments** (OSEP §43.2): in-memory buffer accumulates `LfsBlock`s; when full, flushes to the next free disk segment.
- **Inode map (imap)** (§43.5): `inodeNum → diskAddress` indirection. Updated on every inode write.
- **Checkpoint region (CR)** (§43.6): fixed at segment 0, slot 0. Points to the latest imap piece + log head.
- **Segment summary block** (§43.10): for each flushed block, records `(inodeNum, offset)` so the cleaner can tell live from dead.
- **Liveness check** (§43.10): `IsLive(seg, slot) = imap[inode].dataAddresses[offset] == (seg, slot)`.
- **Segment cleaner** (§43.9): picks the coldest segment (fewest live blocks), compacts its live blocks into a new segment, frees the old one.
- **Read path** (§43.7): `name → inodeNum → imap → diskAddr → inode → data block`.

The simulator exposes:

- `CreateFile(name)` / `WriteData(name, offset, value)` / `Read(name, offset)` — file API.
- `Flush()` — force the in-memory segment to disk.
- `Clean()` — run the segment cleaner; returns `LfsCleanReport` with the cleaned segment.
- `IsLive(seg, slot)` — the §43.10 pseudocode in one call.
- `LiveBlockCount` / `DeadBlockCount` / `FreeSegmentCount` — observability counters.
- `FormatLayout()` — disk grid + imap + file table.

Exposed via `/lfs/run?scenario=create|rewrite|clean&segments=N&blocks=M&files=K` HTTP route.

## Files added/changed

- `src/MiniWebServer.Host/MiniScheduler/Lfs.cs` (new, ~480 lines):
  - `LfsBlockKind` enum: `Free, Data, Inode, Imap, Summary, Checkpoint`.
  - `LfsBlock` (kind, inodeNum, offset, value).
  - `LfsInode` (inodeNum, name, size, data addresses).
  - `LfsCleanReport` record.
  - `Lfs` simulator with the full write/read/clean path.
- `src/MiniWebServer.Host/Program.cs` — `/lfs/run?scenario=...&segments=N&blocks=M&files=K` route (~80 lines).
- `tests/MiniWebServer.Host.Tests/Program.cs` — 6 new tests.

## OSEP alignment

Implements OSEP §43.2 (segments), §43.5 (imap indirection), §43.6 (checkpoint region), §43.7 (read path), §43.9 (garbage collection), §43.10 (segment summary + liveness check), §43.11 (cleaning policy — coldest-first simplification).

## Smoke evidence

### `?scenario=create&segments=8&blocks=6&files=3`

```
=== LFS Layout (M25 / OSEP Ch. 43) ===
disk: 8 segments x 6 blocks/seg
free segments: 5   live blocks: 6   dead blocks: 2
log head segment: 2   latest imap addr: 12

imap (inode -> seg/slot):
  inode 1 -> seg 1 slot 1
  inode 2 -> seg 1 slot 3
  inode 3 -> seg 2 slot 1

files:
  /f0 (inode 1, size=1)
    offset 0 -> seg 1 slot 2 = 'A'
  /f1 (inode 2, size=1)
    offset 0 -> seg 1 slot 4 = 'B'
  /f2 (inode 3, size=1)
    offset 0 -> seg 2 slot 2 = 'C'

segment grid (D=data I=inode P=imap S=summary C=CR . = free):
seg  | blocks
-----|--------------------------------------------------
  0  | [C . . . . .] (CR slot)
  1  | [S I D I D .]
  2  | [S I D . . .]
  3 | [. . . . . .] [FREE]
  ...

trace:
create: wrote 3 files, live=6 dead=2
read OK: /f0=A /f1=B /f2=C
```

OSEP §43.2 "Before writing to the disk, LFS keeps track of updates in memory; when it has received a sufficient number of updates, it writes them to disk all at once." — segment 1 holds inodes + data for /f0 + /f1 (the buffer hit the half-full threshold on the 4th item), segment 2 holds /f2's inode + data.

The imap (OSEP §43.5) points each inode number to its physical segment + slot, which is the indirection layer that lets us move inodes around freely.

### `?scenario=rewrite&segments=8&blocks=6&files=3`

```
free segments: 4   live blocks: 6   dead blocks: 5
log head segment: 3

imap (inode -> seg/slot):
  inode 1 -> seg 1 slot 1
  inode 2 -> seg 1 slot 3
  inode 3 -> seg 2 slot 1

files:
  /f0 (inode 1, size=1)
    offset 0 -> seg 3 slot 1 = 'a'   ← new value
  /f1 (inode 2, size=1)
    offset 0 -> seg 3 slot 2 = 'b'
  /f2 (inode 3, size=1)
    offset 0 -> seg 2 slot 2 = 'C'   ← unchanged

segment grid:
  1  | [S I D* I D* .]    ← the two old data blocks are now DEAD
  2  | [S I D . . .]
  3  | [S D D . . .]       ← new data blocks for /f0, /f1

trace:
rewrite: live=6 dead=5
read OK: /f0=a /f1=b /f2=C
```

OSEP §43.9: rewriting /f0 + /f1 generated 3 new blocks (seg 3) and made the old data blocks dead (marked `D*` in the grid). The dead-block count grew from 2 → 5. Reads still work because the imap + inode pointers redirect to the new locations.

### `?scenario=clean&segments=8&blocks=6&files=3`

```
free segments: 4   live blocks: 6   dead blocks: 3
log head segment: 1

segment grid:
  1  | [S I I . . .]       ← cleaner compacted: kept only the live inode blocks
  2  | [S I D . . .]
  3  | [S D D . . .]

trace:
clean: ran 5 times, free segments=4
read OK: /f0=a /f1=b /f2=C
```

OSEP §43.9 + §43.10: the cleaner ran 5 times, picked the coldest segments (those with the most dead blocks), compacted their live blocks into new segments, and freed the originals. Reads still work — every file's data block lives at the same address as before.

## OSEP concept

> "The crux: HOW TO MAKE ALL WRITES SEQUENTIAL WRITES? How can a file system transform all writes into sequential writes?" (OSEP §43.0)

> "Before writing to the disk, LFS keeps track of updates in memory; when it has received a sufficient number of updates, it writes them to disk all at once." (OSEP §43.2)

> "Use a level of indirection. People often say that the solution to all problems in Computer Science is simply a level of indirection." (OSEP §43.5 TIP)

> "LFS adds a little extra information to each segment that describes each block. Specifically, LFS includes, for each data block D, its inode number (which file it belongs to) and its offset (which block of the file this is)." (OSEP §43.10)

> "LFS must periodically find these old dead versions of file data, inodes, and other structures, and **clean** them." (OSEP §43.9)

## .NET mechanism

- `LfsBlock[,] _disk` — `[segment, slot]` 2-D array.
- `Dictionary<int, int> _imap` — inode → linear disk address.
- `Dictionary<int, LfsInode> _inodes` — in-memory inode cache (the working set).
- `Dictionary<(int seg, int slot), (int inode, int offset)> _summary` — segment summary entries per OSEP §43.10.
- Liveness check: `expectedAddr = inode.DataAddresses[so.offset]` vs `actualAddr = seg * BlocksPerSegment + slot`.
- Cleaner: pick segment with min `CountLive(seg)`, copy live blocks to a new segment via the normal buffer + flush path, free the old segment.

## What this slice does NOT do

- Real I/O latency — disk access is in-memory array indexing.
- Hot/cold segregation (OSEP §43.11, [RO91]).
- Multi-piece imap chunks — we use one imap piece per segment flush.
- Two-CR alternating writes (§43.12) — single CR slot suffices for the simulator.
- Snapshot + versioning (WAFL, ZFS, btrfs §43.13).
- fsync() / write barriers — the simulator crashes only when told.

## Deferred (other LFS extensions)

- **Hot/cold cleaning** (§43.11) — segregate hot (frequently-overwritten) vs cold (stable) segments; clean cold sooner.
- **Two-CR alternating writes** (§43.12) — header + body + trailer protocol; pick the CR with consistent timestamps.
- **Roll-forward crash recovery** (§43.12) — replay segments referenced by the CR's log-head pointer.
- **Size-aware segment sizing** (§43.3) — D = (F / (1-F)) × R_peak × T_position.
- **Multi-piece imap** — split the imap across multiple chunks to reduce per-write cost.
