# Slice 12.4 — HTTP routes + smoke

## What it does

Exposes the Mini FS over HTTP. `path` is a slash-separated absolute string (e.g., `/docs/readme.txt`). The server walks the path from the root inode, calling `Lookup` at each step.

Routes added:

| Route | Method | Body | Returns |
|---|---|---|---|
| `/fs/list?path=/` | GET | — | entries: (name, ino, type, size) |
| `/fs/stat?path=/foo` | GET | — | inode metadata (size, type, nlink, direct_blocks) |
| `/fs/create?path=/foo` | POST | — | `created ino=N` (HTTP 500 on failure) |
| `/fs/write?path=/foo` | POST | request body = content | `wrote N bytes` |
| `/fs/read?path=/foo` | GET | — | raw file content (text/plain or octet-stream) |
| `/fs/unlink?path=/foo` | POST | — | `unlinked` or 404 |

## Files added/changed

- `src/MiniWebServer.Host/Program.cs` — six new routes plus `path` query-string parsing.

## OSEP concept

This slice is the I/O interface for the FS. OSEP §39 covers the file API (open / read / write / close / seek), but our routes are HTTP-level equivalents rather than POSIX syscalls. The semantic mapping:

- HTTP `POST /fs/create?path=X` ↔ OSEP §40.7 `create(path)` + `open(path, O_CREAT)`
- HTTP `POST /fs/write?path=X` (body) ↔ OSEP §40.6 `write(fd, buf, len)` from offset 0
- HTTP `GET /fs/read?path=X` ↔ OSEP §40.6 `read(fd, buf, len)` from offset 0
- HTTP `POST /fs/unlink?path=X` ↔ OSEP §40.7 `unlink(path)`

The slice does not implement `open` / `close` / `seek`; everything is stateless.

## Smoke evidence

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

## Bugs hit during implementation

**`/fs/write` misinterpreted the `offset` parameter.** The Writei signature is `Writei(int ino, byte[] buf, int fileOffset, int len)` — `offset` means file position, not buffer position. The route was passing the request buffer offset (76 = position after `\r\n\r\n`) as the file offset, so a 20-byte write at "file offset 76" set `inode.Size = 76 + 20 = 96`. Fix: extract the body into its own buffer (`Array.Copy(requestBytes, bodyOff, body, 0, bodyLen)`) and call `Writei(ino, body, 0, bodyLen)`.

## Deferred

- **Open + close + seek**: stateless HTTP routes; no fd table. Real OSEP §39 file API requires per-connection fd state.
- **Append mode**: `/fs/write` always writes at offset 0. Real `open(path, O_APPEND)` would write at end-of-file.
- **Truncate**: not implemented.
- **fsync**: not implemented — comes for free with the journal in slice 12.5.
