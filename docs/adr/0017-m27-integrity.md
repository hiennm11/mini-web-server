# ADR 0017: M27 Data Integrity — Checksums, Physical IDs, Write Sequences, and Scrubbing (OSEP Ch. 45)

## Status

Accepted

## Date

2026-09-21

## Context

Part III Persistence has M11 (raw I/O), M12 (vsfs + journaling), M21 (FFS), M24 (RAID), M25 (LFS), M26 (SSD). The persistence thread still misses the most subtle topic: how does a storage system know the bits it just read are actually the bits that were written? Without that guarantee, RAID redundancy can't help (it might reconstruct from a corrupt copy), journaling can't help (the journal itself could be corrupt), and LFS can't help (the segments themselves could be corrupt).

OSEP Ch. 45 is the canonical chapter on this. After M27 the persistence thread is closed.

## Decision

We add `MiniScheduler.Integrity` with three checksum functions + per-block metadata + scrubber + failure injection.

### 1. Three checksum functions, side-by-side

OSEP §45.3 compares XOR, additive, and Fletcher. We implement all three on the same payload so the trade-offs (collision rate vs compute cost) are visible:
- **XOR**: per-byte XOR across the block. Cheap; misses bit-pattern collisions.
- **Additive**: per-byte sum mod 256. Cheap; misses reorderings.
- **Fletcher**: two accumulators s1 (mod 255) and s2 (running sum of s1, mod 255). Catches more errors than XOR/additive at the cost of a few extra instructions per byte.

### 2. Per-block metadata with physical ID + write sequence

Each block carries:
- `PhysicalId` = (disk, block) — detects misdirected writes (§45.5).
- `WriteSequence` — monotonically increasing per write; detects lost writes (§45.6 ZFS-style).
- All three checksums — for trade-off exploration.

### 3. Read verifies; write stores metadata

- `Write(blockId, data)`: bump the write sequence, recompute all three checksums, update physical ID, store data + metadata.
- `Read(blockId)`: read data + metadata, recompute all three checksums, compare against stored values; compare physical ID against expected; compare write sequence against the expected next sequence.

### 4. Scrubber walks every block, reports failures

OSEP §45.7. `Scrub()` reads every block and runs the full verification. Returns a `ScrubReport` listing each block that failed + which checks detected the failure.

### 5. Failure injection

Three fault types, switchable via the route:
- `InjectCorruption(blockId)` — flip a bit in the block's data (silent fault, detected by checksum mismatch).
- `InjectMisdirectedWrite(blockId)` — swap the block's physical ID to a different block's (detected by physical-ID mismatch).
- `InjectLostWrite(blockId)` — decrement the block's write sequence (detected by sequence mismatch on next read).

### 6. Route exposes three scenarios

- `compute` — run all three checksums on a fixed payload + report trade-offs.
- `corrupt` — write a payload + inject one of each fault + show the detection.
- `scrub` — write N blocks + inject faults + run the scrubber + report.

## Consequences

### Positive

- **Closes Ch. 45** — the canonical data-integrity chapter is demonstrated end-to-end.
- **Closes the persistence thread** — M11 + M12 + M21 + M24 + M25 + M26 + M27 now cover every canonical OSEP Ch. 36–45 chapter except device drivers (M11 covers the syscall surface only).
- **No new dependencies** — byte arrays + integer arithmetic.

### Negative

- **No CRC** — we use Fletcher as the strongest; CRC requires polynomial division which is overkill for the simulator.
- **One scrub, not a periodic schedule** — the route exposes one-shot scrubbing.
- **No real failure percentages** — §45.1 figure 45.1 isn't reproduced.
- **No ZFS end-to-end checksum tree** — we model per-block metadata, not the full inode→indirect-block→data checksum chain.

## Verification

- Build clean.
- Existing tests still pass (no test changes).
- New tests cover: all three checksums on a known payload + comparison; write+read round-trip; checksum detects silent corruption; physical ID detects misdirected write; write sequence detects lost write; scrubber reports all failures.
- Smoke: `/integrity/run?scenario=compute` runs all three checksums on a known payload.
- Smoke: `/integrity/run?scenario=corrupt` writes a payload + injects each fault + shows detection.
- Smoke: `/integrity/run?scenario=scrub` writes N blocks + injects faults + runs the scrubber.

## Source Documents

- `docs/learning/m27-integrity/overview.md` — milestone scope + §-coverage.
- `docs/learning/m27-integrity/s1-checksum-scrub.md` — slice doc + smoke evidence.
- OSEP Ch. 45 §45.1–§45.8 (failure modes, checksums, misdirected writes, lost writes, scrubbing, overheads).
- OSEP §45.3 (XOR + additive + Fletcher comparison).
- OSEP §45.5 (misdirected write + physical ID).
- OSEP §45.6 (lost write + write sequence).
- OSEP §45.7 (scrubber protocol).
- ADR 0001 (build-first OSTEP direction) for context on the persistence thread.
