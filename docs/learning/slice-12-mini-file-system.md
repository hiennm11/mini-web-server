# Milestone 12: Mini File System (inode + bitmap + journal)

## Question

How do real file systems lay out their data on disk? What does it take to implement a working FS in user space — superblock, bitmaps, inodes, directories, and crash-safe journal?

## OSTEP Context

- Chapter(s): 40 (File System Implementation), 42 (Crash Consistency: FSCK and Journaling), with cross-references to Ch. 39 (file API) and Ch. 36 (I/O devices).
- Concept: a file system divides the underlying block device into regions with specific purposes — a superblock at a fixed offset describes the rest of the layout; bitmaps track free inodes and free data blocks; an inode table holds per-file metadata (type, size, block pointers); data blocks hold file contents. A journal captures pending updates so that a crash in the middle of a write doesn't leave the FS in an inconsistent state.
- Key point from OSEP §40.3 (xv6 layout): the on-disk order is superblock → log → inode bitmap → data bitmap → inodes → data blocks. Inodes are fixed-size records; each contains a type (file/dir/device/free), a size in bytes, and a small array of direct block pointers (plus, in real systems, indirect/double-indirect for large files).
- Key point from OSEP §42.5 (journaling): a journal is a circular log of pending transactions. Each transaction lists the blocks it intends to modify. The kernel writes the transaction to the log, then writes the modified blocks to their final locations, then commits the transaction by writing a "done" record. On crash recovery, the kernel scans the journal: any committed transaction is replayed; any uncommitted transaction is discarded.

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
- [ ] Built (12.1)
- [ ] Built (12.2)
- [ ] Built (12.3)
- [ ] Built (12.4)
- [ ] Built (12.5)
- [ ] Experimented
- [ ] Noted