# Milestone 12: Mini File System (inode + bitmap + journal)

> **Overview** — what this milestone covers and where to start. Slices live in this folder.

## Question

How do real file systems lay out their data on disk? What does it take to implement a working FS in user space — superblock, bitmaps, inodes, directories, and crash-safe journal?

## Scope

A complete toy file system: disk layout (superblock + bitmaps + inode table + data blocks), inode allocation + read/write, directory operations (Lookup/DirLink/DirUnlink/WalkPath), HTTP routes for create/write/read/unlink, write-ahead journal for crash consistency, multi-block transactions for atomic operations, atomic `rmdir`.

## Slices (in order)

1. **[s1-fs-core.md](./s1-fs-core.md)** — disk layout + superblock + bitmaps + `Ialloc`/`Balloc`/`Ifree`/`Bfree` primitives.
2. **[s2-inode-table.md](./s2-inode-table.md)** — fixed 64-byte inodes + `Iget`/`Iput`/`Readi`/`Writei` access path.
3. **[s3-directory.md](./s3-directory.md)** — directory as a file of `(name, ino)` records; `.`/`..`, `Lookup`, `DirLink`, `DirUnlink`, `WalkPath`.
4. **[s4-http-routes.md](./s4-http-routes.md)** — HTTP surface (`/fs/list`, `/fs/create`, `/fs/write`, `/fs/read`, `/fs/unlink`, `/fs/stat`).
5. **[s5-journal-single-block.md](./s5-journal-single-block.md)** — write-ahead log for crash consistency (single-block transactions, backing file, recovery on replay).
6. **[s6-journal-multi-block.md](./s6-journal-multi-block.md)** — multi-block transactions (`Begin`/`Append`/`Commit`), `Tail` pointer for circular-log safety, in-tx read consistency.
7. **[s7-rmdir.md](./s7-rmdir.md)** — atomic `rmdir` (POSIX empty-only check + `Begin`/`Commit` around `DirUnlink` + `Idestroy`).

## OSTEP coverage

This milestone covers the canonical Part III chapters end-to-end.

- **Ch. 39 Interlude: Files and Directories** (§39.1 files = linear arrays of bytes; §39.4 `read`/`write`/`open`/`close`; §39.7 `fsync`; §39.8 `rename`; §39.10 `unlink`; §39.11 `mkdir`; §39.13 `rmdir` — must be empty).
- **Ch. 40 File System Implementation** (§40.2 vsfs layout: superblock + inode bitmap + data bitmap + inode table + data region; §40.3 inode (type/size/blocks/protection); §40.4 directory organization with `.`/`..`; §40.5 free-space management via bitmaps; §40.6 access path — reading needs inode + data block, writing needs 5 I/Os; §40.7 caching/buffering).
- **Ch. 42 Crash Consistency: FSCK and Journaling** (§42.1 the crash-consistency problem; §42.2 `fsck`; §42.3 journaling = write-ahead log; §42.3 "Recovery" — replay committed transactions; §42.3 "Batching Log Updates" — buffer multiple writes into one transaction; §42.3 "Making the Log Finite" — circular log; §42.3 "Tricky Case: Block Reuse" — revoke records; §42.3 data vs metadata journaling; §42.3 "Wrapping Up Journaling" — write-ordering timelines).

## OSEP §-specific deviations

### vsfs layout simplifications

OSEP §40.2 uses 4 KB blocks, 5 blocks of inode table = 80 inodes, 56 data blocks. Our layout follows the same structure but with different numbers:
- 256 inodes (vs 80) — 1 inode block × 16 inodes/block × 16 blocks = 256.
- 185 data blocks (vs 56) — after reserving 8 blocks (slice 5) then 64 blocks (slice 6) for the journal.
- We don't implement the multi-level index (OSEP §40.3 indirect / double-indirect pointers) — files are capped at 12 direct pointers = 48 KB.
- We don't implement extent-based allocation (OSEP §40.3 TIP) — just fixed direct pointers.

### PTE / directory entry simplifications

OSEP §40.3 lists the full inode fields: mode, uid, size, atime/ctime/mtime/dtime, gid, links count, blocks count, flags, OS-specific, 60 bytes for block pointers (15 total), generation, ACLs. Our `Inode` struct is just: `Type`, `Nlink`, `Size`, `DirectBlocks[12]` (56 bytes used; 8 bytes padding).

OSEP §40.4 directory entry: `inum | reclen | strlen | name` (4 + 2 + 2 + name = variable length). Our `DirEntry` is fixed-size `(ushort ino + 30-byte name)` (32 bytes total).

### PTE / inode field simplifications (extending slice 2)

OSEP §40.3 describes the full inode; we use a subset.

### Journaling

OSEP §42.3 "Data Journaling" describes the simple form: write TxB + DATA + TxE, then checkpoint. Our slice 5 implements exactly this.

OSEP §42.3 "Batching Log Updates" describes the optimization: buffer multiple writes into one transaction. Our slice 6 implements this with `Begin`/`Append`/`Commit`.

OSEP §42.3 "Making the Log Finite" describes the circular log. Our slice 6 implements this with the `Tail` pointer.

OSEP §42.3 "Tricky Case: Block Reuse" describes revoke records. We DEFER this — our simulator never frees a block during a transaction, so the scenario cannot occur.

OSEP §42.3 "Metadata Journaling" / "Ordered Journaling" describes the optimization where user data is written only once (not journaled). Our slice 5 does **data journaling** (everything journaled) — the simpler of the two forms.

### POSIX rmdir semantics

OSEP §40.4 doesn't explicitly cover `rmdir`, but OSEP §39.13 does: "rmdir() has the requirement that the directory be empty". Our slice 7 implements this.

### Other simplifications

- We don't implement `rename` (OSEP §40.8).
- We don't implement hard links + `link` (OSEP §40.9) + unlink-naming-mystery.
- We don't implement `fsync` (OSEP §39.7) — would require forcing journal commit + checkpoint before responding to the HTTP write.

## Key OSEP quotes

> "We thus have arrived at a basic protocol for updating file-system on-disk structures... first carefully writes out the details of the transaction to the journal... after the transaction is complete, the file system checkpoints those blocks to their final locations." (OSEP §42.3 "Making the Log Finite")

## .NET mechanism

- The "disk" is a `byte[]` of size `NUM_BLOCKS * BLOCK_SIZE` (1 MB). Every operation translates logical concepts (inodes, data blocks) into byte offsets in this array.
- All FS operations are static methods on `MiniFs` — no instance state, no async.
- `File.WriteAllBytes` / `File.ReadAllBytes` for the backing-image persistence (slice 5).

## Deferred (cross-slice)

- **fsync-style grouping**: each request is currently its own transaction.
- **Block reuse revoke records** (OSEP §42.3 "Tricky Case: Block Reuse"): not needed yet.
- **Hard links + `rename`** (OSEP §40.8-§40.9): would require `Nlink > 1` accounting.
- **Indirect / doubly-indirect blocks** (OSEP §40.3): today a file is capped at ~48 KB (12 direct pointers).
- **Extent-based allocation** (OSEP §40.3 TIP).
- **`fsync` semantics** (OSEP §39.7).
