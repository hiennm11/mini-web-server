# Slice 12.6 — Multi-block transactions

## What it does

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
Journal.GetPendingWrite(bn)   // read-cache for in-tx reads
```

`MiniFs.WriteBlock(blockNo, data)` checks `Journal.InTransaction`. If true, appends to the current transaction. If false, opens a 1-block transaction, appends, and commits (backward-compatible).

### Read consistency inside a transaction

A subtle bug that almost shipped: when CreateFile wraps Iinit + DirLink in one transaction, both write to inode block 3 (different offsets within the block). If `Writei` (during DirLink) re-reads inode block 3 via `MiniFs.ReadBlock`, it would see the **stale** version (pre-Iinit's changes) because the transaction hasn't checkpointed yet. Then its `Iput` would write back the stale version, clobbering Iinit's TYPE_FILE update.

Fix: `MiniFs.ReadBlock` first checks `Journal.GetPendingWrite(blockNo)`. If the current transaction has a pending write for that block, return the latest pending snapshot instead of the stale `_disk` content. This makes reads inside a tx see the latest pending state for the same block.

### Tail pointer for circular safety

Slice 12.5 had an 8-block journal (7 data slots). That worked for the simple smoke but wrapped within a few transactions, creating spurious orphan TxB/TxE pairs that confused Replay.

Slice 12.6 enlarges the journal to 64 blocks (63 data slots) and adds a `Tail` field to the journal superblock. Since every transaction is fully checkpointed during Commit, `Tail == Head` at the end of every Commit. Replay scans from `Tail` to `Head` modulo `JOURNAL_DATA_BLOCKS`, ignoring stale slots outside that range. This makes the journal safe against wrap-around as long as `JOURNAL_DATA_BLOCKS` is larger than the maximum number of slots a single transaction can consume.

## Files added/changed

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

## Smoke evidence (`.gitnexus/smoke-m12-6.ps1`)

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

## OSEP concept

This slice implements OSEP §42.3 "Batching Log Updates":

> "Linux ext3 does not commit each update to disk one at a time ... rather, one can buffer all updates into a global transaction. ... By buffering updates, a file system can avoid excessive write traffic to disk in many cases."

We extend the buffering to span a multi-block logical operation (CreateFile) into a single transaction. This is the same pattern ext3 / ext4 use for `fsync()`: group the metadata updates of a single high-level operation into one journal commit.

It also implements §42.3 "Making the Log Finite" — the circular-log concept with a Tail pointer that advances past checkpointed transactions so the journal space can be reused.

### §42.3 sub-sections not yet covered

- "Tricky Case: Block Reuse" — needs **revoke records**. Deferred.
- "Wrapping Up Journaling: A Timeline" — the issue of write ordering within a journal write. Our implementation assumes sequential disk writes (the simulator uses an in-memory byte array; no real disk reordering). In a real disk, the OSEP aside on "Forcing Writes to Disk" applies.

## .NET mechanism

- The `Journal.GetPendingWrite` method walks the `_txBuffer` list backwards (latest Append wins). O(n) per read but n is small (≤ ~5 blocks per transaction).
- The `Append` method accepts any `byte[]` but the journal takes its reference — so subsequent mutation of the caller's buffer would corrupt the journal. Our code is careful to construct a fresh `byte[]` inside `Iput` / `Writei` before passing to `WriteBlock`.

## Deferred

- **Block reuse revoke records** (OSEP §42.3 "Tricky Case: Block Reuse"): not needed yet because we never reuse blocks across transactions in this slice.
- **fsync-style grouping**: today each request is its own transaction. A future slice could buffer all writes from a single HTTP request into one transaction (saves journal space).
- **Atomic `unlink` of directories** (`rmdir`): same wrapping needed for `Idestroy` of a directory. Slice 12.7 does this.
