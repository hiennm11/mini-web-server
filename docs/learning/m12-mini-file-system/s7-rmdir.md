# Slice 12.7 — Atomic `rmdir`

## What it does

Slice 12.6 wrapped file creation and unlinking in multi-block transactions. This slice extends the same atomicity to directory removal, the last multi-block FS operation without it.

### `UnlinkDir` semantics

POSIX `rmdir` requires the directory to be empty: only `.` and `..` entries are allowed (free slots from partial unlinks are also tolerated, since they don't hold a reference to a real inode). Attempting to remove a non-empty directory returns `UnlinkDirResult.NotEmpty` without modifying anything.

The function returns an `UnlinkDirResult` enum (`Ok` / `NotFound` / `NotADirectory` / `NotEmpty` / `InvalidName` / `Failed`) instead of a bare bool so the HTTP route can map each outcome to the correct status code (200 / 404 / 400 / 400 / 400 / 500).

The atomic transaction wraps two operations:
1. `DirUnlink(parentIno, name)` — zero out the dir entry in the parent's data block.
2. `Idestroy(targetIno)` — free the directory's data block, zero the inode, free the inode bit.

A crash between these would otherwise leave either (a) the directory inode reused but no entry in the parent (orphan inode), or (b) the parent still has the entry but the inode is gone (dangling pointer). Wrapping both in `Journal.Begin()` / `Commit()` makes them atomic.

## Files added/changed

- `src/MiniWebServer.Host/MiniFs/MiniFs.cs`:
  - `UnlinkDir(path)` + `UnlinkDirResult` enum.
  - Empty check: walks `Readdir(targetIno)`, accepts only `.` / `..` / `ino=0`.
  - `ROOT_INO` is protected from removal.
  - Atomic wrap: `Journal.Begin()` → `DirUnlink` → `Idestroy` → `Commit()` (or `Abort()` on exception).
- `src/MiniWebServer.Host/Program.cs`:
  - `/fs/mkdir?path=` route (wraps `CreateDir`).
  - `/fs/rmdir?path=` route (wraps `UnlinkDir`, maps each result to 200/404/400/500).

## Smoke evidence (`.gitnexus/smoke-m12-7.ps1`)

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

## OSEP concept

OSEP §40.7 (`rename`, `link`, `unlink`, `mkdir`, `rmdir`) describes directory operations as multi-block atomic operations. The empty-directory rule for `rmdir` is a POSIX invariant: removing a non-empty directory would leave the files in it unreachable from the root (assuming no other hard links). The empty check is therefore mandatory, not optional.

## Deferred

- **fsync-style grouping**: today each request is its own transaction. A future slice could buffer all writes from a single HTTP request into one transaction (saves journal space).
- **Hard links + `rename`**: OSEP §40.10 + §40.12. Would require managing `Nlink > 1` correctly across the FS (not currently tested).
- **Indirect / doubly-indirect blocks**: today a file is capped at `NDIRECT = 13` blocks = ~50 KB. For larger files we'd need the §40.7 doubly-indirect block trick.
