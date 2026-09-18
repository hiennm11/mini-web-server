# Slice 12.5 — Journal for crash consistency (single-block)

## What it does

Every disk write goes through a journal that records the change in a reserved region before checkpointing it to its final position. The journal lets the FS recover from crashes by replaying committed-but-not-checkpointed transactions and discarding uncommitted ones.

A region of the disk is reserved for the journal:

```
block 7   : journal superblock (magic, head, next-TID)
blocks 8..14 : journal data (TxB + per-block data + TxE per transaction)
```

User data lives in disk blocks 15..255 (241 data blocks). `Balloc` skips the journal region, and `Format()` marks the journal region's data bitmap bits as in-use.

### Single-block transaction format

```
TxB   : [TXB_MAGIC=0xAABBCCDD | TID | blockNo | zero-padded]
DATA  : [exact 4096 bytes of the new block content]
TxE   : [TXE_MAGIC=0xDDCCBBAA | TID | zero-padded]
```

### Protocol (per transaction, blocking write-through)

For each `WriteBlock(blockNo, data)`:

1. Allocate TID, append TxB at `journal-data-slot[head]`.
2. Append DATA at `journal-data-slot[head+1]`.
3. Append TxE at `journal-data-slot[head+2]`.
4. Checkpoint: write `data` to its final on-disk position.
5. Advance `head` and persist the journal superblock.

### Recovery (on Mount)

If the journal superblock's magic matches, scan all journal data slots:

- A TxB with no matching TxE → discard (uncommitted transaction).
- A TxB with a matching TxE → replay the checkpoint write (redo logging, per OSEP §42.3).

After replay, the journal is reset via `Format()` so future writes start fresh.

### Persistence (backing file)

To make "crash and restart" actually testable, the FS supports a backing-file path via `MountFromFile(string?)` and `SaveToFile(string)`. The environment variable `MINIFS_IMAGE` (defaulting to `./minifs.img`) is the default backing file.

- On `MountFromFile`: if the file exists and contains a valid superblock, load it and replay the journal; otherwise Format() a fresh disk.
- `SaveToFile` writes the entire 1 MB disk image to the path. The HTTP route `/fs-save?path=<file>` exposes this.

## Alignment with OSEP §42.3

OSEP §42.3 introduces **data journaling** (write everything — including user data — to the journal first) as opposed to **metadata journaling** / **ordered journaling** (only metadata goes to the journal; user data is written once to its final location). Our `Journal` does **data journaling** because every `WriteBlock` goes through it, regardless of whether the block holds user data or metadata. This matches the simpler of the two journaling modes OSEP describes.

OSEP §42.3 "Recovery" describes the replay algorithm: scan the log for committed transactions (TxB followed by TxE with matching TIDs) and replay them by re-issuing the writes to their final disk locations. Our `Journal.Replay()` does exactly this.

OSEP §42.3 "Batching Log Updates" describes the optimization of buffering multiple writes into one transaction instead of issuing one TxB+DATA+TxE per write. This is **slice 12.6**.

OSEP §42.3 "Making the Log Finite" introduces the **circular log** concept — after a transaction is checkpointed, its space can be reused. Our Tail pointer (slice 12.6) implements this.

OSEP §42.3 "Tricky Case: Block Reuse" introduces **revoke records** to handle the case where a block is freed and reallocated while its old contents are still in the journal. We defer this — our simulator never frees a block during a transaction, so the scenario cannot occur. Real Linux ext3 handles this with revoke records per §42.3.

## Files added/changed

- `src/MiniWebServer.Host/MiniFs/Journal.cs` (new, ~140 lines): TxB/TxE encoding, Replay, single-block transactions.
- `src/MiniWebServer.Host/MiniFs/Constants.cs`: added `JOURNAL_START=7`, `JOURNAL_BLOCKS=8`, `DATA_BLOCKS_START=15`, plus journal magic constants.
- `src/MiniWebServer.Host/MiniFs/MiniFs.cs`:
  - `Format()` now reserves the journal region in the data bitmap and calls `Journal.Format()`.
  - `WriteBlock(blockNo, src)` routes through `Journal.WriteBlockJournaled`.
  - New `WriteBlockNoLog(blockNo, src)` for journal-internal writes (the journal writes its own TxB/DATA/TxE blocks directly to the journal region, bypassing itself).
  - New `MountFromFile(string?)` and `SaveToFile(string)`.
  - `Mount()` calls `Journal.Replay()` after loading the superblock.
- `src/MiniWebServer.Host/Program.cs`: `/fs-save?path=<file>` route + `MINIFS_IMAGE` env var for the default backing file. Debug route `/fs-inject-orphan` writes a fake TxB into the journal without a matching TxE (for testing Replay).

## Smoke evidence

**Test 1 — persistence round-trip** (`.gitnexus/smoke-m12-5.ps1`):

```
1. start server (fresh image)
2. POST /fs/create?path=/hello.txt        → created ino=2
3. POST /fs/write?path=/hello.txt  (17B)  → wrote 17 bytes
4. GET /fs/list                           → 3 entries (., .., hello.txt size=17)
5. GET /fs/read?path=/hello.txt           → "Hello from M12.5!" (17 bytes)
6. GET /fs-save?path=.../minifs.img       → "saved to ..."
7. kill server
8. restart server (loads image, replays journal)
9. GET /fs/list                           → 3 entries (., .., hello.txt size=17) ✓
10. GET /fs/read?path=/hello.txt          → "Hello from M12.5!" ✓
```

**Test 2 — orphan TxB discarded on recovery** (`.gitnexus/smoke-m12-5-orphan.ps1`):

```
1. start server (fresh image)
2. GET /fs-inject-orphan                  → "orphan-txb-injected" (writes fake TxB at journal slot 8)
3. POST /fs/create?path=/orphan-test.txt → created ino=2
4. POST /fs/write?path=/orphan-test.txt  (15B) → wrote 15 bytes
5. GET /fs-save?path=.../minifs.img      → saved
6. kill server
7. restart server (replay scans journal, finds orphan TxB with no TxE → discards; the orphan-test.txt transaction had TxB+TxE → replayed)
8. GET /fs/list                           → 3 entries (., .., orphan-test.txt size=15) ✓
9. GET /fs/read?path=/orphan-test.txt    → "survives orphan" (15 bytes) ✓
```

The orphan test proves OSEP §42's core guarantee: a crash mid-transaction loses the in-flight update; a crash after commit but before checkpoint replays the update on recovery.

## OSEP concept

This slice implements the literal protocol from OSEP §42.3:

> 1. **Journal write:** Write the contents of the transaction (containing TxB and the contents of the update) to the log; wait for these writes to complete.
> 2. **Journal commit:** Write the transaction commit block (containing TxE) to the log; wait for the write to complete; the transaction is now committed.
> 3. **Checkpoint:** Write the contents of the update to their final locations within the file system.

Our slice executes steps 1+2+3 atomically (synchronously) per WriteBlock call. A future enhancement would be to buffer multiple updates into one transaction and write them out together — the natural follow-up for "real" fsync() semantics. We chose the simplest possible slice: one block per transaction, synchronous, no batching. That still demonstrates the recovery guarantee end-to-end.

## .NET mechanism

- `BitConverter.GetBytes(uint)` writes little-endian ints (matching the x86 FS); `BitConverter.ToUInt32(byte[], int)` reads back.
- The journal writes use the raw `WriteBlockNoLog` path so the journal doesn't write itself to itself (recursion). The `static bool _active` flag prevents nested transactions.
- The backing file uses `File.WriteAllBytes` / `File.ReadAllBytes` for the 1 MB disk image.

## Deferred

- **Write barriers / fsync**: the OSEP §42 "write barrier" detail (forcing ordering across writes) is not needed here because each WriteBlock is synchronous; it would matter if we used write buffering.
- **Block reuse revoke records** (OSEP §42.3 "Tricky Case: Block Reuse"): not needed yet because we never reuse blocks across transactions in this slice.
- **Multi-block transactions** (covered by slice 12.6).
