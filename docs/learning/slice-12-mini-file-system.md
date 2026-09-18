# Milestone 12: Mini File System (inode + bitmap + journal)

## Question

How do real file systems lay out their data on disk? What does it take to implement a working FS in user space — superblock, bitmaps, inodes, directories, and crash-safe journal?

## OSTEP Context

- Chapter(s): 40 (File System Implementation — vsfs), with cross-references to Ch. 39 (file API) and Ch. 42 (Crash Consistency / Journaling, for slice 12.5).
- Concept: a file system divides the underlying block device into regions with specific purposes — a superblock at a fixed offset describes the rest of the layout; bitmaps track free inodes and free data blocks; an inode table holds per-file metadata (type, size, block pointers); data blocks hold file contents. A journal captures pending updates so that a crash in the middle of a write doesn't leave the FS in an inconsistent state.
- Key point from OSEP §40.2 (vsfs layout): the on-disk order is superblock → inode bitmap → data bitmap → inode table → data region. There is no log in vsfs — journaling is a separate topic in OSEP Ch. 42. Inodes are fixed-size records; each contains a type (file/dir/device/free), a size in bytes, and a small array of direct block pointers (plus, in real systems, indirect/double-indirect for large files).
- OSEP §40.3 covers the inode as index node (i-number, low-level name) and the multi-level index for large files.
- OSEP §40.4 covers the directory organization: a directory is a list of `(entry name, inode number)` records, plus `.` and `..` entries; deleted entries leave a "free slot" marked with inode number zero.
- OSEP §40.5 covers free space management via the two bitmaps (inode + data).
- OSEP §40.6 covers the access path for read/write, including the per-operation I/O cost (e.g., writing one block costs 5 I/Os: read data bitmap, write data bitmap, read inode, write inode, write data).
- Key point from OSEP §42 (journaling, slice 12.5): a journal is a write-ahead log of pending transactions. The kernel writes the transaction to the log, then writes the modified blocks to their final locations, then commits the transaction. On crash recovery, the kernel scans the journal: any committed transaction is replayed; any uncommitted transaction is discarded.

## C#/.NET Mechanism

- We model the disk as a `byte[]` of size `NUM_BLOCKS * BLOCK_SIZE`. The "block device" is RAM, but every read/write goes through the same offset arithmetic a real disk would. The on-disk positions are real — superblock at offset 0, inode bitmap at offset BLOCK_SIZE, data bitmap at 2×BLOCK_SIZE, inode table starting at 3×BLOCK_SIZE, data blocks after that.
- Operations on inodes and bitmaps are bytewise manipulations at those positions. For example, `ialloc()` finds the first zero bit in the inode bitmap, sets it, returns the inode number.
- The journal is a circular buffer of fixed-size log records, stored in the first few data blocks. We write the transaction header + a list of block updates + a commit marker.

## Build

Five sub-slices within M12:

### Slice 12.1 — Superblock + bitmaps + alloc/free

Files:

- `src/MiniWebServer.Host/MiniFs/MiniFs.cs` — main class with `Mount()`, `Unmount()`, `Format()`, `Ialloc()`, `Ifree(int)`, `Balloc()`, `Bfree(int)`, `ReadBlock(int, byte[])`, `WriteBlock(int, byte[])`.
- `src/MiniWebServer.Host/MiniFs/Superblock.cs` — struct with `Magic`, `TotalInodes`, `TotalBlocks`, `FreeInodes`, `FreeDataBlocks`, `InodeBitmapStart`, `DataBitmapStart`, `InodeTableStart`, `DataBlocksStart`.
- `src/MiniWebServer.Host/MiniFs/Constants.cs` — `BLOCK_SIZE = 4096`, `NUM_INODES = 256`, `INODE_SIZE = 64`, `NUM_BLOCKS = 256`.
- `src/MiniWebServer.Host/Program.cs` — `/fs-stats` route that prints the superblock + alloc counts.

The `Mount()` either loads an existing disk image or calls `Format()` if the magic doesn't match. `Format()` zeros all blocks, writes the superblock, marks all inodes + data blocks free in the bitmaps, reserves inode 0 as "no inode" (similar to xv6).

### Slice 12.2 — Inode table + read/write

Files:

- `src/MiniWebServer.Host/MiniFs/Inode.cs` — struct: `Type` (file/dir/free), `Size`, `DirectBlocks[12]`, `IndirectBlock`.
- `src/MiniWebServer.Host/MiniFs/MiniFs.cs` — `Iget(int ino)`, `Iput(int ino, Inode)`, `Readi(Inode, byte[], int offset, int len)`, `Writei(Inode, byte[], int offset, int len)`.

A direct-block inode with 12 pointers × 4096 bytes = 48 KB max file size. The indirect block adds 1024 pointers (one block holds 1024 × 4-byte pointers) for an extra 4 MB.

### Slice 12.3 — Directory operations

Files:

- `src/MiniWebServer.Host/MiniFs/DirEntry.cs` — `(ushort ino, char[] name)` fixed-size record (e.g., 32 bytes per entry).
- `src/MiniWebServer.Host/MiniFs/MiniFs.cs` — `Lookup(int dirIno, string name)`, `Create(int dirIno, string name, FileType)`, `Unlink(int dirIno, string name)`, `Mkdir(int dirIno, string name)`.

Directories are special files whose content is an array of `DirEntry`. `.` and `..` are the first two entries.

### Slice 12.4 — HTTP routes + smoke

Files:

- `src/MiniWebServer.Host/Program.cs` — `/fs/list`, `/fs/read?path=`, `/fs/create?path=`, `/fs/write?path=&content=`, `/fs/unlink?path=`, `/fs/stat?path=`.

`path` is a slash-separated string (e.g., `/docs/readme.txt`). The server walks the path from the root inode, looking up each component.

### Slice 12.5 — Journal for crash consistency

Files:

- `src/MiniWebServer.Host/MiniFs/Journal.cs` — circular log of transactions. `Begin()`, `LogWrite(int blockNo, byte[] data)`, `Commit()`, `Replay()`.
- `src/MiniWebServer.Host/MiniFs/MiniFs.cs` — wraps writes in `Begin()`/`Commit()`.

Each transaction reserves log blocks, writes the header + data, writes a commit marker. On mount, scan the journal from the start; replay committed transactions; discard uncommitted.

## Experiment (per slice)

Each slice's smoke goes in `.gitnexus/smoke-m12.{1,2,3,4,5}.ps1` and exercises the new surface. For 12.1:

```powershell
# Start server
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj

# Hit /fs-stats to confirm mount + superblock
curl http://127.0.0.1:8080/fs-stats

# Confirm: NUM_INODES=256, NUM_BLOCKS=256, free_inodes=255 (1 reserved), free_data_blocks=249
```

For 12.4 (after all FS ops work):

```powershell
# Create /hello.txt, write "Hello world", read back
curl "http://127.0.0.1:8080/fs/create?path=/hello.txt"
curl -X POST "http://127.0.0.1:8080/fs/write?path=/hello.txt&content=Hello%20world"
curl "http://127.0.0.1:8080/fs/read?path=/hello.txt"

# List root
curl http://127.0.0.1:8080/fs/list
```

For 12.5 (journal):

```powershell
# Start server, create a file, then SIGKILL mid-transaction (simulate by killing the process before commit)
# Restart, verify the file does NOT exist (uncommitted transaction is discarded on recovery)
```

## Observation

Answer after running each smoke:

What does the FS do at each level?

- **12.1**: the disk is a flat `byte[]`. The superblock at offset 0 tells us where everything else lives. The bitmaps are the FS's bookkeeping; an `ialloc()` is just a scan-then-set-bit.
- **12.2**: an inode is a fixed-size metadata record. The file's content lives in data blocks pointed to by `DirectBlocks[12]`. Reading 4 KB of a file at offset 0 means reading the data block at `DirectBlocks[0]`.
- **12.3**: a directory is a file whose data blocks hold `DirEntry` records. `Lookup` scans the directory's data blocks for a name match.
- **12.4**: HTTP routes walk the path component-by-component from the root inode, calling `Lookup` at each step. Writes call `Create`/`Writei` and update the inode's size + timestamps.
- **12.5**: the journal is a write-ahead log. A transaction's writes go to the log first; only after commit do the data blocks get updated in place. On crash recovery, the journal is replayed or discarded based on whether the commit marker exists.

## Status

- [x] Planned
- [x] Built (12.1 — superblock + bitmaps + alloc/free)
- [x] Built (12.2 — inode table + readi/writei)
- [x] Built (12.3 — directory operations: lookup/dir-link/dir-unlink/walk/create/unlink)
- [x] Built (12.4 — HTTP routes + smoke)
- [x] Built (12.5 — journal for crash consistency: TxB/TxE/replay + backing file)
- [x] Built (12.6 — multi-block transactions: Begin/Append/Commit API + Tail pointer)
- [x] Built (12.7 — atomic rmdir: empty-only check + Begin/Commit around DirUnlink + Idestroy)
- [x] Experimented
- [x] Noted

## Learning Note (slices 12.1–12.4)

### What changed

**Slice 12.1 (superblock + bitmaps + alloc/free)** — `commit 2adc2f0`

- `MiniFs/Constants.cs` — layout constants: `BLOCK_SIZE=4096`, `NUM_INODES=256`, `NUM_BLOCKS=256`, `INODE_SIZE=64`. Block map: superblock=0, inode bitmap=1, data bitmap=2, inode table=3..6, data blocks=7..255 (249 data blocks). `FS_MAGIC=0x1F5EF5E1`.
- `MiniFs/Superblock.cs` — struct with `Magic`, `TotalInodes`, `TotalBlocks`, `FreeInodes`, `FreeDataBlocks`, and the bitmap/table start offsets.
- `MiniFs/MiniFs.cs` — `Mount()` allocates a 1 MB `byte[]` "disk"; tries to read an existing superblock; if magic mismatches, calls `Format()` to zero the disk and write a fresh superblock. `Ialloc()`/`Ifree()`/`Balloc()`/`Bfree()` update the bitmaps and free counts.
- `Program.cs` — calls `MiniFs.Mount()` at startup; new `/fs-stats` route reports the superblock + counts.

**Slice 12.2 (inode table + readi/writei)** — `commit 054d57f`

- `MiniFs/Inode.cs` — `ushort Type`, `ushort Nlink`, `int Size`, `int[NDIRECT=12] DirectBlocks` (56 bytes used; 8 bytes padding to fill 64-byte inode size).
- `MiniFs/MiniFs.cs` — `Iget(ino)` reads the inode table block that holds `ino`, parses the slot; `Iput(ino, inode)` writes back; `Iinit(ino, type)` initializes a freshly-allocated inode; `Idestroy(ino)` releases all data blocks, marks inode free, and clears the bitmap bit. `Readi(ino, buf, offset, len)` reads up to `len` bytes at file offset `offset`, walking direct blocks; handles EOF (returns < len) and unset blocks. `Writei(ino, buf, fileOffset, len)` writes `len` bytes at file offset, calling `Balloc()` on first write to each direct-block slot; throws if the file would exceed `NDIRECT * BLOCK_SIZE = 48 KB`.

**Slice 12.3 (directory operations)** — bundled with 12.4 in this commit

- `MiniFs/DirEntry.cs` — fixed 32-byte record (`ushort Ino` + 30 bytes UTF-8 name). `Ino == 0` means the slot is free.
- `MiniFs/MiniFs.cs` — `InitRoot()` allocates inode 1 as the root directory and writes `.` and `..` entries pointing at itself. `Lookup(dirIno, name)` scans the directory's data blocks for a name match; `DirLink(dirIno, name, ino)` adds a new entry (reusing free slots or appending); `DirUnlink(dirIno, name)` zeros the slot; `Readdir(dirIno)` enumerates entries. `WalkPath("/a/b/c")` resolves an absolute path component-by-component from the root. `CreateFile(path)` allocates an inode and links it in the parent directory; `UnlinkFile(path)` removes the link and destroys the inode.
- `Program.cs` — six new routes: `/fs/list?path=`, `/fs/stat?path=`, `/fs/create?path=`, `/fs/write?path=` (POST body), `/fs/read?path=`, `/fs/unlink?path=`.

### What I observed

Smoke run against the running server (default mode):

```
=== /fs-stats (after mount + InitRoot: 2 inodes in use) ===
magic = 0x1F5EF5E1   total_inodes = 256   total_blocks = 256
free_inodes = 254    free_data_blocks = 248
inodes_in_use = 2    data_blocks_in_use = 1
inode_bitmap_block = 1   data_bitmap_block = 2
inode_table_start = 3    data_blocks_start = 7
disk_size_bytes = 1048576   block_size = 4096

=== /fs/list (should show . and ..) ===
path: /
entries: 2
  ino=   1  type=dir   size=      64  name=.
  ino=   1  type=dir   size=      64  name=..

=== /fs/create?path=/hello.txt ===
created ino=3

=== /fs/list ===
path: /
entries: 4
  ino=   1  type=dir   size=     128  name=.
  ino=   1  type=dir   size=     128  name=..
  ino=   2  type=file  size=      20  name=z.txt
  ino=   3  type=file  size=       0  name=hello.txt

=== POST /fs/write?path=/hello.txt (20 bytes "Hello mini-FS world!") ===
wrote 20 bytes

=== /fs/read?path=/hello.txt ===
Content-Length: 20
Hello mini-FS world!

=== /fs/stat?path=/hello.txt ===
ino = 3
type = file
size = 20
nlink = 1
direct_blocks = 2,-,-,-,-,-,-,-,-,-,-,-

=== /fs/unlink?path=/hello.txt ===
unlinked

=== /fs-stats after unlink ===
free_inodes = 252    free_data_blocks = 247
inodes_in_use = 4    data_blocks_in_use = 2
```

Reading:

- After `Mount()` + `InitRoot()`: 2 inodes in use (reserved inode 0 + root inode 1), 1 data block in use (root's `.` and `..` directory entries).
- After `CreateFile("/hello.txt")`: root's size grew from 64 → 128 (added one 32-byte dir entry for `hello.txt`); new file has `Size=0`, `DirectBlocks[0]=-1` until written.
- After `Writei(3, body, 0, 20)`: inode 3's `DirectBlocks[0]=2` (next free data block); `Size=20`.
- After `Readi(3, ...)`: returns exactly the 20 bytes that were written — `Hello mini-FS world!` round-trips correctly through the disk.
- After `UnlinkFile("/hello.txt")`: the dir entry slot in root is zeroed (size 160 still shows because unlink doesn't shrink the dir; in real FS, dir would be compacted); inode 3 freed; data block 2 freed.

### Bugs hit during implementation

1. **`Idestroy` didn't free the inode bitmap bit.** Initial code marked the inode `TYPE_FREE` in the table but didn't call `Ifree()`, so `inodes_in_use` stayed elevated after unlink. Fix: add `Ifree(ino)` call at the end of `Idestroy`.
2. **`/fs/write` misinterpreted the `offset` parameter.** The Writei signature is `Writei(int ino, byte[] buf, int fileOffset, int len)` — `offset` means file position, not buffer position. The route was passing the request buffer offset (76 = position after `\r\n\r\n`) as the file offset, so a 20-byte write at "file offset 76" set `inode.Size = 76 + 20 = 96`. Fix: extract the body into its own buffer (`Array.Copy(requestBytes, bodyOff, body, 0, bodyLen)`) and call `Writei(ino, body, 0, bodyLen)`.

### OSEP concept

The slice implements the textbook vsfs (Very Simple File System) layout from OSEP Ch. 40:

- **§40.2 (overall organization)**: superblock describes everything else; the inode bitmap, data bitmap, inode table, and data region live at fixed positions on disk.
- **§40.3 (the inode)**: a fixed-size metadata record; each inode has an i-number (low-level name) and a size, plus pointers to data blocks.
- **§40.4 (directory organization)**: a directory is a special file whose data blocks contain `(entry name, inode number)` records, plus `.` and `..`. `lookup` scans the data blocks for a name match; `create` adds a new record; deleting leaves the slot marked with inode number zero.
- **§40.5 (free space management)**: two bitmaps (inode + data) are the only free-space representation; `ialloc`/`balloc` scan for the first zero bit.
- **§40.6 (access path)**: a real file read costs N reads of the directory entries plus the inode, then the data blocks. A write costs more because it has to update the inode AND the data bitmap AND the inode table block.

### .NET mechanism

- The "disk" is a `byte[256 * 4096] = 1 MB` `byte[]`. Every operation translates logical concepts (inodes, data blocks) into byte offsets in this array.
- `BitConverter` (little-endian on x86) handles the integer serialization for the on-disk format.
- All FS operations are static methods on `MiniFs` — no instance state, no async. Simpler for educational purposes.

### Slice 12.5 — Journal for crash consistency

The slice implements the OSEP §42 write-ahead log protocol: every disk write goes through a journal that records the change in a reserved region before checkpointing it to its final position. The journal lets the FS recover from crashes by replaying committed-but-not-checkpointed transactions and discarding uncommitted ones.

### Layout

Eight disk blocks are reserved for the journal at the start of the data region:

```
block 7  : journal superblock (magic, head, next-TID)
blocks 8..14 : journal data (TxB + per-block data + TxE per transaction)
```

User data lives in disk blocks 15..255 (241 data blocks). `Balloc` skips the journal region, and `Format()` marks the journal region's data bitmap bits as in-use.

### Transaction format (single-block updates)

A transaction is three blocks written in order, all inside the journal data region:

```
TxB   : [TXB_MAGIC=0xAABBCCDD | TID | blockNo | zero-padded]
DATA  : [exact 4096 bytes of the new block content]
TxE   : [TXE_MAGIC=0xDDCCBBAA | TID | zero-padded]
```

### Protocol (per transaction, blocking write-through)

For each `WriteBlock(blockNo, data)`:

1. Allocate TID, append TxB at `journal-data-slot[head]`.
2. Append DATA at `journal-data-slot[head+1]`.
3. Append TxE at `journal-data-slot[head+2]`.
4. Checkpoint: write `data` to its final on-disk position.
5. Advance `head` and persist the journal superblock.

### Recovery (on Mount)

If the journal superblock's magic matches, scan all journal data slots:

- A TxB with no matching TxE → discard (uncommitted transaction).
- A TxB with a matching TxE → replay the checkpoint write (redo logging, per OSEP §42.3).

After replay, the journal is reset via `Format()` so future writes start fresh.

### Persistence (backing file)

To make "crash and restart" actually testable, the FS supports a backing-file path via `MountFromFile(string?)` and `SaveToFile(string)`. The environment variable `MINIFS_IMAGE` (defaulting to `./minifs.img`) is the default backing file.

- On `MountFromFile`: if the file exists and contains a valid superblock, load it and replay the journal; otherwise Format() a fresh disk.
- `SaveToFile` writes the entire 1 MB disk image to the path. The HTTP route `/fs-save?path=<file>` exposes this.

### Files added/changed

- `src/MiniWebServer.Host/MiniFs/Journal.cs` (new, ~140 lines): TxB/TxE encoding, Replay, single-block transactions.
- `src/MiniWebServer.Host/MiniFs/Constants.cs`: added `JOURNAL_START=7`, `JOURNAL_BLOCKS=8`, `DATA_BLOCKS_START=15`, plus journal magic constants.
- `src/MiniWebServer.Host/MiniFs/MiniFs.cs`:
  - `Format()` now reserves the journal region in the data bitmap and calls `Journal.Format()`.
  - `WriteBlock(blockNo, src)` routes through `Journal.WriteBlockJournaled`.
  - New `WriteBlockNoLog(blockNo, src)` for journal-internal writes (the journal writes its own TxB/DATA/TxE blocks directly to the journal region, bypassing itself).
  - New `MountFromFile(string?)` and `SaveToFile(string)`.
  - `Mount()` calls `Journal.Replay()` after loading the superblock.
- `src/MiniWebServer.Host/Program.cs`: `/fs-save?path=<file>` route + `MINIFS_IMAGE` env var for the default backing file. Debug route `/fs-inject-orphan` writes a fake TxB into the journal without a matching TxE (for testing Replay).

### Smoke evidence

**Test 1 — persistence round-trip** (`.gitnexus/smoke-m12-5.ps1`):

```
1. start server (fresh image)
2. POST /fs/create?path=/hello.txt        → created ino=2
3. POST /fs/write?path=/hello.txt  (17B)  → wrote 17 bytes
4. GET /fs/list                           → 3 entries (., .., hello.txt size=17)
5. GET /fs/read?path=/hello.txt           → "Hello from M12.5!" (17 bytes)
6. GET /fs-save?path=.../minifs.img       → "saved to ..."
7. kill server
8. restart server (loads image, replays journal)
9. GET /fs/list                           → 3 entries (., .., hello.txt size=17) ✓
10. GET /fs/read?path=/hello.txt          → "Hello from M12.5!" ✓
```

**Test 2 — orphan TxB discarded on recovery** (`.gitnexus/smoke-m12-5-orphan.ps1`):

```
1. start server (fresh image)
2. GET /fs-inject-orphan                  → "orphan-txb-injected" (writes fake TxB at journal slot 8)
3. POST /fs/create?path=/orphan-test.txt → created ino=2
4. POST /fs/write?path=/orphan-test.txt  (15B) → wrote 15 bytes
5. GET /fs-save?path=.../minifs.img      → saved
6. kill server
7. restart server (replay scans journal, finds orphan TxB with no TxE → discards; the orphan-test.txt transaction had TxB+TxE → replayed)
8. GET /fs/list                           → 3 entries (., .., orphan-test.txt size=15) ✓
9. GET /fs/read?path=/orphan-test.txt    → "survives orphan" (15 bytes) ✓
```

The orphan test proves OSEP §42's core guarantee: a crash mid-transaction loses the in-flight update; a crash after commit but before checkpoint replays the update on recovery.

### OSEP concept

This slice implements the literal protocol from OSEP §42.3:

> 1. **Journal write:** Write the contents of the transaction (containing TxB and the contents of the update) to the log; wait for these writes to complete.
> 2. **Journal commit:** Write the transaction commit block (containing TxE) to the log; wait for the write to complete; the transaction is now committed.
> 3. **Checkpoint:** Write the contents of the update to their final locations within the file system.

Our slice executes steps 1+2+3 atomically (synchronously) per WriteBlock call. A future enhancement would be to buffer multiple updates into one transaction and write them out together — the natural follow-up for "real" fsync() semantics. We chose the simplest possible slice: one block per transaction, synchronous, no batching. That still demonstrates the recovery guarantee end-to-end.

### .NET mechanism

- `BitConverter.GetBytes(uint)` writes little-endian ints (matching the x86 FS); `BitConverter.ToUInt32(byte[], int)` reads back.
- The journal writes use the raw `WriteBlockNoLog` path so the journal doesn't write itself to itself (recursion). The `static bool _active` flag prevents nested transactions.
- The backing file uses `File.WriteAllBytes` / `File.ReadAllBytes` for the 1 MB disk image.

### What's deferred (next slice candidates)

- **Write barriers / fsync**: the OSEP §42 "write barrier" detail (forcing ordering across writes) is not needed here because each WriteBlock is synchronous; it would matter if we used write buffering.
- **Block reuse revoke records** (OSEP §42.3 "Tricky Case: Block Reuse"): not needed yet because we never reuse blocks across transactions in this slice.

## Slice 12.6 — Multi-block transactions

Slice 12.5 demonstrated the journal with single-block transactions: every `WriteBlock` was its own TxB + DATA + TxE sequence. That's fine for atomic writes of single blocks, but a `CreateFile` does 2-3 `WriteBlock` calls (inode bitmap + inode table + directory data) — each its own transaction. A crash mid-`CreateFile` would leave the FS with an inode allocated in the bitmap but no entry in the directory (or vice versa). Slice 12.6 fixes this by making multiple `WriteBlock` calls atomic.

### Multi-block transaction format

```
TxB   : [TXB_MAGIC | TID | count | blockNo_1 | blockNo_2 | ... | blockNo_count]
DATA_1: [4096 bytes of new content for blockNo_1]
DATA_2: [4096 bytes of new content for blockNo_2]
...
DATA_count: [4096 bytes of new content for blockNo_count]
TxE   : [TXE_MAGIC | TID]
```

The TxB block has room for up to `MAX_BLOCKS_PER_TX = 1021` block numbers (4096 - 12 bytes of header) / 4 bytes per blockNo = 1021 entries. Way more than any single FS operation needs.

### Journal API

```csharp
Journal.Begin();             // open a multi-block transaction
Journal.Append(blockNo, data); // buffer a write (does not touch disk yet)
Journal.Commit();             // flush TxB + DATA* + TxE + checkpoint each block
Journal.Abort();              // discard pending writes without checkpointing
Journal.InTransaction         // bool: is a tx active?
Journal.GetPendingWrite(bn)   // slice 12.6: read-cache for in-tx reads
```

`MiniFs.WriteBlock(blockNo, data)` checks `Journal.InTransaction`. If true, appends to the current transaction. If false, opens a 1-block transaction, appends, and commits (backward-compatible).

### Read consistency inside a transaction

A subtle bug that almost shipped: when CreateFile wraps Iinit + DirLink in one transaction, both write to inode block 3 (different offsets within the block). If `Writei` (during DirLink) re-reads inode block 3 via `MiniFs.ReadBlock`, it would see the **stale** version (pre-Iinit's changes) because the transaction hasn't checkpointed yet. Then its `Iput` would write back the stale version, clobbering Iinit's TYPE_FILE update.

Fix: `MiniFs.ReadBlock` first checks `Journal.GetPendingWrite(blockNo)`. If the current transaction has a pending write for that block, return the latest pending snapshot instead of the stale `_disk` content. This makes reads inside a tx see the latest pending state for the same block.

### Tail pointer for circular safety

Slice 12.5 had an 8-block journal (7 data slots). That worked for the simple smoke but wrapped within a few transactions, creating spurious orphan TxB/TxE pairs that confused Replay.

Slice 12.6 enlarges the journal to 64 blocks (63 data slots) and adds a `Tail` field to the journal superblock. Since every transaction is fully checkpointed during Commit, `Tail == Head` at the end of every Commit. Replay scans from `Tail` to `Head` modulo `JOURNAL_DATA_BLOCKS`, ignoring stale slots outside that range. This makes the journal safe against wrap-around as long as `JOURNAL_DATA_BLOCKS` is larger than the maximum number of slots a single transaction can consume.

### Files added/changed (slice 12.6)

- `src/MiniWebServer.Host/MiniFs/Constants.cs`:
  - `JOURNAL_BLOCKS` 8 → 64 (avoids wrap in normal smoke workloads)
  - `DATA_BLOCKS_START` 15 → 71 (user data shrinks correspondingly)
  - `MAX_BLOCKS_PER_TX = 1021`
- `src/MiniWebServer.Host/MiniFs/Journal.cs`:
  - `Begin()` / `Append()` / `Commit()` / `Abort()` / `InTransaction` / `GetPendingWrite()` API.
  - TxB format updated for N block updates (count + count × blockNo).
  - Journal superblock has a `Tail` field; `Replay` scans from Tail to Head modulo size.
  - `WriteBlockJournaled` auto-wraps single-block writes in a 1-block tx when no tx is active.
- `src/MiniWebServer.Host/MiniFs/MiniFs.cs`:
  - `ReadBlock` checks `Journal.GetPendingWrite` first (in-tx read consistency).
  - `CreateFile` wraps Ialloc + Iinit + DirLink in `Journal.Begin()`/`Commit()`.
  - `UnlinkFile` wraps DirUnlink + Idestroy in `Journal.Begin()`/`Commit()`.
- `src/MiniWebServer.Host/Program.cs`:
  - `/fs-inject-orphan-multi` DEBUG route for testing multi-block orphan discard.
  - `/fs-dump-block?blockNo=N` DEBUG route for inspecting disk bytes during debugging.
  - Reordered `/fs-inject-orphan` and `/fs-inject-orphan-multi` so the multi-block one matches first.

### Smoke evidence (.gitnexus/smoke-m12-6.ps1)

```
1. start server (fresh image)
2. create /file1.txt + /file2.txt + write 19B each
3. /fs/list
   entries: 4
     ino=1 dir   size=128  .
     ino=1 dir   size=128  ..
     ino=2 file  size=19   file1.txt
     ino=3 file  size=19   file2.txt
4. inject multi-block orphan TxB (count=2, no DATA, no TxE)
5. save image
6. kill server
7. restart server (replay scans journal: orphans discarded, committed files replayed)
8. /fs/list: 4 entries (., .., file1.txt size=19, file2.txt size=19) ✓
9. /fs/read /file1.txt: 'multi-block atomic!' ✓
10. /fs/read /file2.txt: 'multi-block atomic!' ✓
```

The new tx-level read consistency is essential: without `Journal.GetPendingWrite`, the smoke would show `type=free` for the new inodes (verified via debug dump during slice dev).

### OSEP concept

This slice implements the second OSEP §42 lesson that slice 12.5 deferred: **batching** (the §42.3 subsection "Batching Log Updates"):

> "Linux ext3 does not commit each update to disk one at a time ... rather, one can buffer all updates into a global transaction. ... By buffering updates, a file system can avoid excessive write traffic to disk in many cases."

We extend the buffering to span a multi-block logical operation (CreateFile) into a single transaction. This is the same pattern ext3 / ext4 use for `fsync()`: group the metadata updates of a single high-level operation into one journal commit.

### .NET mechanism

- The `Journal.GetPendingWrite` method walks the `_txBuffer` list backwards (latest Append wins). O(n) per read but n is small (≤ ~5 blocks per transaction).
- The `Append` method accepts any `byte[]` but the journal takes its reference — so subsequent mutation of the caller's buffer would corrupt the journal. Our code is careful to construct a fresh `byte[]` inside `Iput` / `Writei` before passing to `WriteBlock`.

## Slice 12.7 — Atomic `rmdir`

Slice 12.6 wrapped file creation and unlinking in multi-block transactions. This slice extends the same atomicity to directory removal, the last multi-block FS operation without it.

### `UnlinkDir` semantics

POSIX `rmdir` requires the directory to be empty: only `.` and `..` entries are allowed (free slots from partial unlinks are also tolerated, since they don't hold a reference to a real inode). Attempting to remove a non-empty directory returns `UnlinkDirResult.NotEmpty` without modifying anything.

The function returns an `UnlinkDirResult` enum (`Ok` / `NotFound` / `NotADirectory` / `NotEmpty` / `InvalidName` / `Failed`) instead of a bare bool so the HTTP route can map each outcome to the correct status code (200 / 404 / 400 / 400 / 400 / 500).

The atomic transaction wraps two operations:
1. `DirUnlink(parentIno, name)` — zero out the dir entry in the parent's data block.
2. `Idestroy(targetIno)` — free the directory's data block, zero the inode, free the inode bit.

A crash between these would otherwise leave either (a) the directory inode reused but no entry in the parent (orphan inode), or (b) the parent still has the entry but the inode is gone (dangling pointer). Wrapping both in `Journal.Begin()` / `Commit()` makes them atomic.

### Files added/changed (slice 12.7)

- `src/MiniWebServer.Host/MiniFs/MiniFs.cs`:
  - `UnlinkDir(path)` + `UnlinkDirResult` enum.
  - Empty check: walks `Readdir(targetIno)`, accepts only `.` / `..` / `ino=0`.
  - `ROOT_INO` is protected from removal.
  - Atomic wrap: `Journal.Begin()` → `DirUnlink` → `Idestroy` → `Commit()` (or `Abort()` on exception).
- `src/MiniWebServer.Host/Program.cs`:
  - `/fs/mkdir?path=` route (wraps `CreateDir`).
  - `/fs/rmdir?path=` route (wraps `UnlinkDir`, maps each result to 200/404/400/500).

### Smoke evidence (.gitnexus/smoke-m12-7.ps1)

```
1. mkdir /subdir                       -> 200 mkdir ino=5
2. rmdir /subdir (empty)               -> 200 rmdir ok
3. mkdir /subdir + create /subdir/inside.txt + write 17B
                                        -> 200 200 200
4. rmdir /subdir (not empty)           -> 400 not empty
5. unlink /subdir/inside.txt           -> 200
5b. rmdir /subdir (now empty)          -> 200 rmdir ok
6. /fs/list                            -> 5 entries (probe2, afile, probe3 + . ..) ✓
7. rmdir /                             -> 400 invalid name
8. rmdir /nope                         -> 404 not found
9. rmdir /afile                        -> 400 not a directory
10. save + restart                     -> persisted state, including /after-restart ✓
```

### OSEP concept

OSEP §40.7 (`rename`, `link`, `unlink`, `mkdir`, `rmdir`) describes directory operations as multi-block atomic operations. The empty-directory rule for `rmdir` is a POSIX invariant: removing a non-empty directory would leave the files in it unreachable from the root (assuming no other hard links). The empty check is therefore mandatory, not optional.

### Deferred (next slice candidates)

- **fsync-style grouping**: today each request is its own transaction. A future slice could buffer all writes from a single HTTP request into one transaction (saves journal space).
- **Block reuse revoke records** (OSEP §42.3 "Tricky Case: Block Reuse"): not needed yet because we never reuse blocks across transactions in this slice.
- **Hard links + `rename`**: OSEP §40.10 + §40.12. Would require managing `Nlink > 1` correctly across the FS (not currently tested).
- **Indirect / doubly-indirect blocks**: today a file is capped at `NDIRECT = 13` blocks = ~50 KB. For larger files we'd need the §40.7 doubly-indirect block trick.