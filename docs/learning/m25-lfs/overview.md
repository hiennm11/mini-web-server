# Milestone 25: Log-Structured File System (LFS)

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does a filesystem turn the huge gap between random and sequential I/O into a win? How do you find an inode when it's scattered all over the disk? How do you garbage-collect old versions of files without leaving holes?

## Scope

A standalone LFS simulator demonstrating OSEP Ch. 43's core ideas:

- **Segments** (OSEP §43.2): in-memory buffer that accumulates {data blocks, inodes, imap chunks}; on flush, writes them all to disk sequentially.
- **Inode map (imap)** (§43.5): indirection layer `inodeNum → diskAddress`. Pieces of the imap ride along in each segment write.
- **Checkpoint region (CR)** (§43.6): fixed location at disk address 0 pointing to the latest imap piece + log head/tail.
- **Segment summary block** (§43.10): for each block in a segment, records `(inodeNum, offset)` so the cleaner can tell live vs. dead.
- **Garbage / liveness** (§43.9 + §43.10): when an inode is rewritten, the old inode + old data block become "garbage". They're reclaimed by the segment cleaner.
- **Segment cleaner** (§43.9): picks cold segments, compacts live blocks into new segments, frees the old ones.
- **Read path** (§43.7): CR → imap → inode → data block. Three indirections, all but the last cached.

Exposed via `/lfs/run?scenario=create|rewrite|clean&segments=N&blocks=M` HTTP route.

## Slice

- **[s1-lfs-segments.md](./s1-lfs-segments.md)** — `Lfs` simulator with segments + imap + CR + cleaner.

## OSEP coverage

- **Ch. 43 Log-structured File Systems** (§43.1 writing sequentially; §43.2 segments; §43.3 how much to buffer; §43.4 finding inodes; §43.5 inode map; §43.6 checkpoint region; §43.7 reading a file; §43.8 directories; §43.9 garbage collection; §43.10 block liveness; §43.11 cleaning policy; §43.12 crash recovery).
- §43.3 segment-size math (amortization of positioning cost) — modeled as a configurable segment size; no closed-form solver.
- §43.11 hot/cold cleaning policy — implemented as "pick the segment with fewest live blocks first" (the simplest defensible policy). Hot/cold segregation is a future improvement.
- §43.12 crash recovery — modeled as "roll forward from the last CR through segments referenced by the CR's head pointer" (the simplest valid recovery). The two-CR alternating-write protocol (§43.12) is implemented as one CR slot (we don't crash, so a single CR is enough).

OSEP §43.1:
> "An ideal file system would thus focus on write performance, and try to make use of the sequential bandwidth of the disk."

OSEP §43.5:
> "The imap is a structure that takes an inode number as input and produces the disk address of the most recent version of the inode."

OSEP §43.6:
> "LFS has just such a fixed place on disk for this, known as the **checkpoint region (CR)**. The checkpoint region contains pointers to (i.e., addresses of) the latest pieces of the inode map, and thus the inode map pieces can be found by reading the CR first."

OSEP §43.9:
> "So what should we do with these older versions of inodes, data blocks, and so forth? ... LFS instead keeps only the latest live version of a file; thus (in the background), LFS must periodically find these old dead versions of file data, inodes, and other structures, and **clean** them; cleaning should thus make blocks on disk free again for use in subsequent writes."

OSEP §43.10:
> "LFS includes, for each data block D, its inode number (which file it belongs to) and its offset (which block of the file this is). This information is recorded in a structure at the head of the segment known as the **segment summary block**."

## OSEP §-specific deviations

| OSEP § | We do | We defer |
|---|---|---|
| §43.2 segments | In-memory `Segment` accumulates `LfsBlock`s until full, then flushes to next free segment on disk. | Variable segment size; real LFS uses one large segment (MB-scale). |
| §43.4 finding inodes | Fixed inode table → scatter. Resolved by the imap indirection. | Real disk seeks. |
| §43.5 inode map | Imap is a `Dictionary<int, int>` (inode → disk address). Pieces of it ride along in each segment flush. | Multi-piece imap with CR pointing to head + tail. We use one piece per flush for the simulator. |
| §43.6 checkpoint region | Fixed slot at segment 0 of the disk. Contains the latest imap piece + log head pointer. | Two alternating CRs (§43.12). |
| §43.7 read path | `name → inodeNum → imap → diskAddr → inode → data block`. Three indirections, imap cached in memory. | Real I/O cost. |
| §43.8 directories | A directory is a list of `(name, inodeNum)` pairs; same write path as files. | Multi-block directories + indirect pointers. |
| §43.9 garbage | Old inodes + old data blocks become "dead" the moment a rewrite appends new versions. Tracked via segment summary. | None (this is the headline feature). |
| §43.10 block liveness | `SegmentSummary[(inode, offset)]` per block. `IsLive(addr)` compares summary's `(inode, offset)` against the current inode's offset-N pointer. | Version-number short-circuit (§43.10 TIP). |
| §43.11 cleaning policy | "Coldest segment first" (fewest live blocks). | Hot/cold segregation [RO91]. |
| §43.12 crash recovery | Roll-forward: read CR, then segments referenced by the CR's head pointer. | Two-CR alternating-write protocol. |
| §43.13 WAFL/ZFS/btrfs | Not modeled. | Snapshot + tree-structured FS. |

## Key OSEP quotes

> "The crux: HOW TO MAKE ALL WRITES SEQUENTIAL WRITES? How can a file system transform all writes into sequential writes?" (OSEP §43.0)

> "Before writing to the disk, LFS keeps track of updates in memory; when it has received a sufficient number of updates, it writes them to disk all at once." (OSEP §43.2)

> "Use a level of indirection. People often say that the solution to all problems in Computer Science is simply a level of indirection." (OSEP §43.5 TIP)

> "LFS adds a little extra information to each segment that describes each block. Specifically, LFS includes, for each data block D, its inode number (which file it belongs to) and its offset (which block of the file this is). This information is recorded in a structure at the head of the segment known as the **segment summary block**." (OSEP §43.10)

## .NET mechanism

- `LfsBlock` = one block on disk. Records `kind` (Data / Inode / Imap / Summary / CR), `inodeNum`, `offset`, `value`.
- `Segment` = fixed-size array of `LfsBlock`s. When full, the simulator flushes it to the next free segment slot on disk.
- `Lfs.disk` = `LfsBlock[Segments, BlocksPerSegment]`.
- `Lfs.imap` = `Dictionary<int, int>` (inode → disk address).
- `Lfs.cr` = latest checkpoint region contents.
- `Lfs.clean()` = find coldest segment, copy live blocks to a new segment, mark old blocks dead, free the old segment.

## Files

- `src/MiniWebServer.Host/MiniScheduler/Lfs.cs` (new, ~300 lines):
  - `LfsBlockKind` enum: `Data, Inode, Imap, Summary, Checkpoint`.
  - `LfsBlock` (kind, inodeNum, offset, value).
  - `LfsInode` (inodeNum, size, data addresses).
  - `Lfs` simulator: `CreateFile`, `WriteData`, `Read`, `Flush`, `Clean`, `FormatLayout`, `SimulateCrash` (roll-forward).
- `src/MiniWebServer.Host/Program.cs` — `/lfs/run?scenario=...` route.

## What this slice does NOT do

- Real I/O latency — disk access is in-memory array indexing.
- Hot/cold segregation (OSEP §43.11, [RO91]).
- Multi-piece imap chunks — we use one imap piece per segment flush.
- Two-CR alternating writes (§43.12) — single CR slot suffices for the simulator.
- Snapshot + versioning (WAFL, ZFS, btrfs §43.13).
- fsync() / write barriers — the simulator crashes only when told.

## Where this leads

After M25 the roadmap continues with:

- **M26 Flash-based SSDs** (Ch. 44)
- **M27 Data integrity** (Ch. 45)

These round out Part III Persistence. Once M24–M27 are done, the persistence thread is closed.
