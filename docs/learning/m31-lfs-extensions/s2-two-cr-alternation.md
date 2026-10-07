# Slice 31.2: Two-CR Alternating Writes (Ch. 43 §43.12)

> **What it does** — adds a second checkpoint region written alternately with the §43.12 header/body/trailer protocol, and a recovery rule that mounts the most recent CR whose header and trailer timestamps agree. Exposes `/lfs/run?scenario=dual-cr-recovery|cr-alternation|cr-crash-during-write`.

## Question

LFS keeps one checkpoint region pointing at the latest imap pieces. What happens to the file system if the machine crashes while that CR is being written?

## OSTEP coverage

- **§43.12 "Crash Recovery And The Log"**, verbatim: "To ensure that the CR update happens atomically, LFS actually keeps two CRs, one at either end of the disk, and writes to them alternately. LFS also implements a careful protocol when updating the CR with the latest pointers to the inode map and other information; specifically, it first writes out a header (with timestamp), then the body of the CR, and then finally one last block (also with a timestamp). If the system crashes during a CR update, LFS can detect this by seeing an inconsistent pair of timestamps. LFS will always choose to use the most recent CR that has consistent timestamps, and thus consistent update of the CR is achieved."

Two properties come out of that paragraph, and they are different:

- **The timestamps detect the crash.** The trailer is written last, so `header == trailer` is the evidence that the write finished. A mismatch means the crash landed mid-update.
- **The alternation makes recovery possible at all.** Once one CR is detected as torn, the other one is the only remaining anchor — and it is only intact because the writer was not touching it.

## Surface

`DualCheckpointRegion` (new, `MiniScheduler/SegmentSizer.cs`):

- `Write(bodyTimestamp?, trailerTimestamp?)` — stamps header, body, trailer into the active CR, then toggles. The two optional timestamp parameters exist so a test can represent a crash between body and trailer; that is the only reason the method takes arguments.
- `Recover()` — returns `(index, image, reason)`: the most recent CR whose header and trailer agree, plus why the other was rejected.
- `ActiveIndex`, `Cr0`, `Cr1`, `WriteLog`, `WaitingCount`-style observability for the route.
- `CrImage.IsConsistent` — `HeaderTimestamp == TrailerTimestamp`.

Timestamps are a monotonic counter, not wall-clock, so the consistency property is testable without the tests depending on timing.

## Smoke evidence

### `?scenario=dual-cr-recovery`

```
three checkpoint writes, alternating:
  write 1 -> CR0 (ts=1)
  write 2 -> CR1 (ts=2)
  write 3 -> CR0 (ts=3)

CR0: header=3 trailer=3 consistent=True
CR1: header=2 trailer=2 consistent=True

recovered from CR0 (ts=3)  (CR1 also consistent but older, ts=2)
```

### `?scenario=cr-alternation`

```
10 checkpoint writes -> CR0, CR1, CR0, CR1, CR0, CR1, CR0, CR1, CR0, CR1
next write targets CR0

CR0 last ts: 9
CR1 last ts: 10
```

Ten writes is even, so each CR holds five and CR1 is one timestamp newer. A tie is impossible with a monotonic clock — every write stamps a distinct value — so "the most recent consistent CR" is unambiguous.

### `?scenario=cr-crash-during-write`

The case the timestamps exist for:

```
  write 1 -> CR0 (ts=1, consistent)
  write 2 -> CR1 (ts=2, consistent)
  write 3 -> CR0 CRASHED (header=3, body=3, trailer=never written)

CR0: header=3 trailer=-1 consistent=False
CR1: header=2 trailer=2 consistent=True

recovered from CR1 (ts=2)  (CR0 rejected: header ts=3 != trailer ts=-1)
```

With a single CR this is unrecoverable — the one structure the file system uses to find everything else is half-written. Alternating is the whole reason CR1 is still mountable here.

## Tests

- `dual-cr recovery takes the newest consistent CR` — three writes leave CR0 (ts=3) newest and both consistent; two writes leave CR1 (ts=2) newest; a `DualCheckpointRegion` that was never written throws rather than mounting a filesystem with no anchor.
- `dual-cr alternation leaves the other CR intact on a mid-write crash` — the crashed CR is visibly inconsistent (`Cr0.IsConsistent == false`) while the other is not; recovery returns the survivor's index and a reason naming the rejected one; seven writes produce exactly `CR0,CR1,CR0,CR1,CR0,CR1,CR0` and leave `ActiveIndex == 1`; when both CRs are torn, `Recover()` throws.

## Why this is not wired into M25's `Lfs`

M25's `Lfs` updates a single CR block at `segment 0, block 0` and is a running system; `DualCheckpointRegion` is a standalone model of the crash-recovery *protocol*. They do not compose, and pretending they do would mean retrofitting a persistence model onto a simulator that never persists. Slice 31.2 delivers the protocol; wiring a real CR update through M25's flush path is separate work.

## What this slice does NOT do

- **Roll-forward recovery** — §43.12's second half: reading past the last consistent CR through the log to recover writes since it was taken. Deferred.
- **Imap fragments at the disk ends** — some LFSes place imap pieces to reduce CR update frequency; not modelled.
- **Cross-CR validation or a MAC per block.** A CR is trusted purely on its internal timestamp agreement, so anything that can write to disk can forge a consistent-looking CR.

## Source documents

- `docs/learning/m31-lfs-extensions/overview.md` — milestone scope.
- `docs/learning/m25-lfs/overview.md` — predecessor surface (single CR).
- `docs/adr/0021-m31-lfs-extensions.md` — the timestamp and CR-modelling decisions.
- OSTEP Ch. 43 §43.6 (what a CR is), §43.12 (two CRs, alternation, header/body/trailer).