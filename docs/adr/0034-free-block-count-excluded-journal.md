# ADR 0034: The Free-Block Count Excludes the Journal, Because the Bitmap Already Does

## Status

Accepted

## Date

2026-10-09

## Context

The suite had never executed `MiniFs` — the largest untested module in the repo at 1294 lines. Nineteen tests were added covering allocation accounting, the journal's commit rule, crash recovery, and directory operations. One failed on first run, and it was a real defect rather than a wrong expectation.

`Format()` marks the journal region as in-use:

```csharp
for (int i = 0; i < JOURNAL_BLOCKS; i++)
    SetBit(DATA_BITMAP_BLOCK, i);
```

but initialises the free count from the full region, without subtracting those 64 blocks:

```csharp
FreeDataBlocks = NUM_DATA_BLOCKS,   // 185
```

So the bitmap and the superblock disagreed by exactly `JOURNAL_BLOCKS`. `Balloc()` skips the journal by index (`if (i < JOURNAL_BLOCKS) continue;`) and decrements the counter per allocation, so the two views never converged.

## The defect

With `NUM_DATA_BLOCKS = 185` and `JOURNAL_BLOCKS = 64`, a fresh filesystem claimed 185 free blocks while its bitmap marked 64 as in-use.

Consequences, in order of severity:

- **The disk reports itself full early.** The allocator hands out 121 blocks and then `FreeDataBlocks` hits 0, so `Balloc()` returns -1 — while 64 allocatable slots are still untouched in the bitmap. A caller sees a full disk that has room.
- **`DataBlocksInUse()` and `FreeDataBlocks` disagree**, so no single number describes the disk's state. Anything summing them gets a wrong total.
- **The error is silent.** Nothing logs, nothing throws. A workload that allocates until -1 cannot distinguish this from genuine exhaustion.

`Format()` is also the recovery path: `MountFromFile` falls back to it whenever the magic is wrong or the size mismatches, so the bad state was reachable on every fresh mount, not just a first-time format.

## Why it survived

No test executed the module. The bug is only observable by allocating to exhaustion and comparing what was handed out against what the superblock claimed — a test that reads the field and compares it to a constant would not catch it, because the wrong value is self-consistent within the file.

The same shape appears in ADR 0030 and ADR 0029: the claim was locally plausible and nothing connected it to a check. Here the claim was a number in a struct.

## Decision

**The free count is derived from the bitmap, not from the region size.** `Format()` subtracts the journal reservation:

```csharp
_sb.FreeDataBlocks = NUM_DATA_BLOCKS - JOURNAL_BLOCKS;
```

**The invariant is pinned by a test that allocates to exhaustion**, not one that reads the counter. `fs: the free-block count matches what the allocator can actually hand out` loops `Balloc()` until it returns -1 and asserts the count matches, which is the only form that fails when the two drift apart.

## Consequences

### Positive

- A full disk is now actually full. 121 allocatable blocks is the real capacity, and -1 means so.
- The superblock and the bitmap agree, so `FreeDataBlocks + DataBlocksInUse() == NUM_DATA_BLOCKS` holds at every point.

### Negative

- 24 fewer allocatable blocks than the previous code reported. Nothing depended on the larger number — nothing tested it, and the extra 64 were never handable — but the figure was visible in `/fs-stats`.

### Neutral

- One line of production code changed. `MiniFsScope` was added to the test file to give each test an isolated mount; `MiniFs` is static, so unmounting is the only way to reset it.
- 19 MiniFs assertions, 169 overall, up from 124.

## References

- `docs/learning/m12-mini-file-system/overview.md` — slices 12.1 and 12.6, the allocation and journal-reservation design
- ADR 0029 — a claim that nothing checks becomes false without failing visibly
- ADR 0030 — the same failure shape in citation claims rather than counters