# Slice 12.1 — Superblock + bitmaps + alloc/free

## What it does

The disk is a `byte[]` of size `NUM_BLOCKS * BLOCK_SIZE` (1 MB). The first few blocks hold the FS metadata:

- Block 0: superblock (magic, totals, free counts, layout offsets)
- Block 1: inode bitmap (1 bit per inode)
- Block 2: data bitmap (1 bit per data block)
- Blocks 3..6: inode table (256 inodes × 64 bytes)
- Blocks 7..255: data blocks (249 blocks)

`Mount()` either loads an existing image or calls `Format()` (writes the superblock, zeros bitmaps). `Ialloc()`/`Ifree()`/`Balloc()`/`Bfree()` scan-and-set/clear bits in the bitmaps and update the free counts.

## Files added/changed

- `src/MiniWebServer.Host/MiniFs/Constants.cs` — `BLOCK_SIZE = 4096`, `NUM_INODES = 256`, `INODE_SIZE = 64`, `NUM_BLOCKS = 256`, `FS_MAGIC = 0x1F5EF5E1`.
- `src/MiniWebServer.Host/MiniFs/Superblock.cs` — `Magic`, `TotalInodes`, `TotalBlocks`, `FreeInodes`, `FreeDataBlocks`, `InodeBitmapStart`, `DataBitmapStart`, `InodeTableStart`, `DataBlocksStart`.
- `src/MiniWebServer.Host/MiniFs/MiniFs.cs` — `Mount()`, `Unmount()`, `Format()`, `Ialloc()`, `Ifree(int)`, `Balloc()`, `Bfree(int)`, `ReadBlock(int, byte[])`, `WriteBlock(int, byte[])`.
- `src/MiniWebServer.Host/Program.cs` — calls `MiniFs.Mount()` at startup; new `/fs-stats` route reports the superblock + counts.

## OSEP concept

This slice implements OSEP §40.2 (overall organization) and §40.5 (free-space management via two bitmaps). The layout is the textbook vsfs (Very Simple File System) from OSEP Ch. 40:

- superblock describes everything else (OSEP §40.2)
- two bitmaps for free-space accounting (OSEP §40.5)
- `ialloc` / `balloc` are simple scan-then-set operations

Inode + directory data structures come in slices 12.2 + 12.3.

## Smoke evidence

```
=== /fs-stats (after mount + InitRoot: 2 inodes in use) ===
magic = 0x1F5EF5E1   total_inodes = 256   total_blocks = 256
free_inodes = 254    free_data_blocks = 248
inodes_in_use = 2    data_blocks_in_use = 1
inode_bitmap_block = 1   data_bitmap_block = 2
inode_table_start = 3    data_blocks_start = 7
disk_size_bytes = 1048576   block_size = 4096
```

## .NET mechanism

- The "disk" is a `byte[256 * 4096] = 1 MB` `byte[]`. All FS operations translate logical concepts into byte offsets in this array.
- `BitConverter` (little-endian on x86) handles the integer serialization for the on-disk format.
- All FS operations are static methods on `MiniFs` — no instance state, no async.

## Deferred

- None — slice 12.1 is the foundation; subsequent slices build on it.
