# Milestone 12: Mini File System (inode + bitmap + journal)

## Question

How do real file systems lay out their data on disk? What does it take to implement a working FS in user space — superblock, bitmaps, inodes, directories, and crash-safe journal?

## Slice index

Read in order — each builds on the previous:

1. **s12.1-fs-core.md** — disk layout, superblock, bitmaps, alloc/free primitives.
2. **s12.2-inode-table.md** — fixed-size inode records + `Readi`/`Writei` access path.
3. **s12.3-directory.md** — directory as a file of `(name, ino)` entries; `.`/`..`, `Lookup`, `DirLink`, `DirUnlink`, `WalkPath`.
4. **s12.4-http-routes.md** — HTTP surface (`/fs/list`, `/fs/create`, `/fs/write`, `/fs/read`, `/fs/unlink`, `/fs/stat`).
5. **s12.5-journal-single-block.md** — write-ahead log for crash consistency (single-block transactions, backing file, recovery on replay).
6. **s12.6-journal-multi-block.md** — multi-block transactions (`Begin`/`Append`/`Commit`), `Tail` pointer for circular-log safety, in-tx read consistency.
7. **s12.7-rmdir.md** — atomic `rmdir` (POSIX empty-only check + `Begin`/`Commit` around `DirUnlink` + `Idestroy`).

## OSTEP coverage

- **OSEP Ch. 40** (File System Implementation — vsfs) — slices 12.1–12.4.
- **OSEP §40.7** (mkdir / rmdir / rename / link semantics) — slices 12.3 + 12.7.
- **OSEP Ch. 42** (Crash Consistency: FSCK and Journaling) — slices 12.5–12.7, especially §42.3 "Batching Log Updates" + "Making the Log Finite" + "Tricky Case: Block Reuse" (the last is deferred — see slice 12.6 doc).

## C#/.NET Mechanism

- The "disk" is a `byte[]` of size `NUM_BLOCKS * BLOCK_SIZE` (1 MB). Every operation translates logical concepts (inodes, data blocks) into byte offsets in this array.
- Inodes and bitmaps are bytewise manipulations at those positions. `ialloc()` finds the first zero bit in the inode bitmap, sets it, returns the inode number.
- The journal is a circular buffer of fixed-size log records, stored in the first few data blocks. We write the transaction header + a list of block updates + a commit marker.
- All FS operations are static methods on `MiniFs` — no instance state, no async. Simpler for educational purposes.

## Deferred (cross-slice)

- **fsync-style grouping**: today each request is its own transaction. A future slice could buffer all writes from a single HTTP request into one transaction (saves journal space).
- **Block reuse revoke records** (OSEP §42.3 "Tricky Case: Block Reuse"): the journal never frees a block during a transaction, so the scenario cannot occur; deferred to a future slice.
- **Hard links + `rename`**: OSEP §40.10 + §40.12. Would require managing `Nlink > 1` correctly across the FS.
- **Indirect / doubly-indirect blocks**: today a file is capped at `NDIRECT = 12` blocks = ~48 KB. For larger files we'd need the §40.7 doubly-indirect block trick.
