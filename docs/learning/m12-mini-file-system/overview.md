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

## OSEP coverage

- **Ch. 40** (File System Implementation — vsfs) — slices 1-4.
- **§40.7** (mkdir / rmdir / rename / link semantics) — slices 3 + 7.
- **Ch. 42** (Crash Consistency: FSCK and Journaling) — slices 5-7, especially §42.3 "Batching Log Updates" + "Making the Log Finite" + "Tricky Case: Block Reuse" (the last is deferred — see slice 6 doc).

## .NET mechanism

- The "disk" is a `byte[]` of size `NUM_BLOCKS * BLOCK_SIZE` (1 MB). Every operation translates logical concepts (inodes, data blocks) into byte offsets in this array.
- All FS operations are static methods on `MiniFs` — no instance state, no async.

## Deferred (cross-slice)

- **fsync-style grouping**: each request is currently its own transaction.
- **Block reuse revoke records** (OSEP §42.3 "Tricky Case: Block Reuse"): not needed yet.
- **Hard links + `rename`**: would require `Nlink > 1` accounting.
- **Indirect / doubly-indirect blocks**: today a file is capped at ~48 KB (12 direct pointers).
