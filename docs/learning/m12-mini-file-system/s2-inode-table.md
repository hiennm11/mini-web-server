# Slice 12.2 — Inode table + read/write

## What it does

An inode is a fixed-size metadata record (64 bytes). Each has:

- `Type` (file / dir / free)
- `Size` (bytes)
- `DirectBlocks[12]` — pointers to data blocks (12 × 4 KB = 48 KB max file size)

The FS implements `Iget(ino)` / `Iput(ino, inode)` for inode-table access, and `Readi()` / `Writei()` for the access path (compute logical block, look up `DirectBlocks[logicalBlock]`, read/write the data block).

## Files added/changed

- `src/MiniWebServer.Host/MiniFs/Inode.cs` — `ushort Type`, `ushort Nlink`, `int Size`, `int[NDIRECT=12] DirectBlocks` (56 bytes used; 8 bytes padding to fill 64-byte inode size).
- `src/MiniWebServer.Host/MiniFs/MiniFs.cs`:
  - `Iget(int ino)` reads the inode-table block that holds `ino`, parses the slot.
  - `Iput(int ino, Inode)` writes back.
  - `Iinit(int ino, ushort type)` initializes a freshly-allocated inode.
  - `Idestroy(int ino)` releases all data blocks, marks inode free, and clears the bitmap bit.
  - `Readi(int ino, byte[] buf, int offset, int len)` reads up to `len` bytes at file offset `offset`, walking direct blocks; handles EOF (returns < len) and unset blocks.
  - `Writei(int ino, byte[] buf, int fileOffset, int len)` writes `len` bytes at file offset, calling `Balloc()` on first write to each direct-block slot; throws if the file would exceed `NDIRECT * BLOCK_SIZE = 48 KB`.

## OSEP concept

This slice implements OSEP §40.3 (the inode as index node) and the access path from §40.6.

> "a fixed-size metadata record; each inode has an i-number (low-level name) and a size, plus pointers to data blocks"

> "a real file read costs N reads of the directory entries plus the inode, then the data blocks"

The 12-pointer cap is a deliberate simplification: real OSEP §40.7 describes indirect + double-indirect block pointers for files larger than ~50 KB. Our slice defers that.

## Smoke evidence

The smoke runs in slice 12.4 once the HTTP routes are available. The relevant observation:

```
=== POST /fs/write?path=/hello.txt (20 bytes "Hello mini-FS world!") ===
wrote 20 bytes

=== /fs/stat?path=/hello.txt ===
ino = 3
type = file
size = 20
nlink = 1
direct_blocks = 2,-,-,-,-,-,-,-,-,-,-,-
```

`DirectBlocks[0] = 2` after a 20-byte write to an empty file (block 2 is the first free data block).

## Bugs hit during implementation

**`Idestroy` didn't free the inode bitmap bit.** Initial code marked the inode `TYPE_FREE` in the table but didn't call `Ifree()`, so `inodes_in_use` stayed elevated after unlink. Fix: add `Ifree(ino)` call at the end of `Idestroy`.

## Deferred

- **Indirect / doubly-indirect blocks** (OSEP §40.7): today a file is capped at 48 KB. Would need 1 block for the indirect pointer (1024 × 4-byte entries → +4 MB) and 1 block for the doubly-indirect (+4 GB).
- **Larger inode size**: 64 bytes is enough for 12 direct pointers; real Linux ext4 inodes are 256 bytes to fit extended attributes + 4-level block pointers.
