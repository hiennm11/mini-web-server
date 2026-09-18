# Slice 12.3 — Directory operations

## What it does

A directory is a file whose data blocks hold an array of `DirEntry` records:

```
struct DirEntry {
    ushort Ino;        // 0 means the slot is free
    char[30] Name;     // UTF-8 filename (up to 30 bytes)
}
```

Each entry is 32 bytes (the fixed record size). Every directory has `.` and `..` as its first two entries (pointing at itself and at the parent, respectively).

Operations:

- `Lookup(dirIno, name)` — scan the directory's data blocks for a name match; return the inode number or 0.
- `DirLink(dirIno, name, ino)` — append a new entry, or reuse a free slot (one whose `Ino == 0`).
- `DirUnlink(dirIno, name)` — zero the matching entry's `Ino` field (slot becomes free).
- `Readdir(dirIno)` — enumerate all non-free entries.
- `WalkPath("/a/b/c")` — resolve an absolute path component-by-component from the root inode.

## Files added/changed

- `src/MiniWebServer.Host/MiniFs/DirEntry.cs` — fixed 32-byte record. `Ino == 0` means the slot is free.
- `src/MiniWebServer.Host/MiniFs/MiniFs.cs`:
  - `InitRoot()` — allocates inode 1 as the root directory and writes `.` and `..` entries pointing at itself.
  - `Lookup`, `DirLink`, `DirUnlink`, `Readdir`, `WalkPath`.
  - `CreateFile(path)` — allocates an inode and links it in the parent directory.
  - `UnlinkFile(path)` — removes the link and destroys the inode.

## OSEP concept

This slice implements OSEP §40.4 (directory organization):

> "a directory is a special file whose data blocks contain `(entry name, inode number)` records, plus `.` and `..`. `lookup` scans the data blocks for a name match; `create` adds a new record; deleting leaves the slot marked with inode number zero."

The free-slot pattern (zeroing `Ino` rather than compacting) matches OSEP's description: "deleted entries leave a 'free slot' marked with inode number zero".

## Smoke evidence

The slice ships a `Lookup` + `DirLink` + `DirUnlink` API, but the end-to-end smoke runs in slice 12.4 via HTTP routes. The relevant trace:

```
=== /fs/list (after CreateFile("/hello.txt")) ===
path: /
entries: 4
  ino=   1  type=dir   size=     128  name=.
  ino=   1  type=dir   size=     128  name=..
  ino=   2  type=file  size=      20  name=z.txt
  ino=   3  type=file  size=       0  name=hello.txt
```

Root's size grew from 64 → 128 after one dir entry was added (32-byte slot + 32-byte padding for `.` and `..`).

## Deferred

- **Directory compaction**: real FSes periodically compact a directory by removing free slots. Our `Unlink` leaves the slot intact; subsequent `DirLink` will reuse the slot if found.
- **Long directory names**: 30-byte cap is short. Real ext4 uses variable-length entries (up to 255 bytes per filename).
- **`rename`** (OSEP §40.10): not implemented in this slice. Could be added in a later slice.
