# Slice 32.1: Block-Level and Hybrid FTL (Ch. 44 §44.9)

> **What it does** — adds block-level and hybrid (log-block) mapping to the M26 SSD simulator, and measures page-level, block-level and hybrid against the same workloads.

## Surface

- `FtlMappingCost` — the chapter's table arithmetic, as pure functions over capacity numbers. `TableBytes(capacity, pageBytes, blockBytes, entryBytes, logBlocks)` returns page-level, block-level and hybrid table sizes. No device is allocated, so the 1 TB worked example is checked directly.
- `RawFlash` — page state, program/erase rules and wear counters, shared by all three strategies so they differ only in translation.
- `IFtlStrategy` — `Write` / `Read` / `MappingEntries` / `WriteAmplification` / `MergeLogBlocks`, so the comparison can drive all three identically.
- `PageLevelFtl` — §44.7's baseline. Its collector can only erase a block once every page in it is dead; there is no block pointer to fold a log block into.
- `BlockLevelFtl` — one entry per logical chunk. A small write into a chunk already on the device copies the whole block out. `PhysicalPageOf(lba)` exposes §44.9's "offset added to the physical address of the block".
- `HybridFtl` — per-page log pointers plus per-block data pointers, consulted in that order. `SwitchMerges` / `PartialMerges` / `FullMerges` / `PagesCopiedOnMerge` report what cleaning cost.
- `HybridMergeDemo` — the three merges isolated, so their relative cost can be asserted without a 256-block device.

## Route

`/ssd/run?scenario=ftl-comparison&ftl=all|pagelevel|blocklevel|hybrid&writes=N&workload=seq|random&lbas=N&logblocks=N`
`/ssd/run?scenario=mapping-cost&logblocks=N`
`/ssd/run?scenario=merges`

An unknown `ftl` or `workload` answers 400 rather than running the default workload.

## Measured results

`scenario=ftl-comparison&writes=200`, 64 pages per block, 4 log blocks. Mapping entries are measured before the merge, so the hybrid row's total is exactly its log + data split.

| workload | strategy | map entries | data bytes | write amp |
|---|---|---|---|---|
| random (1024 logical) | page-level | 184 | 200 | **1.00×** |
| random | block-level | 16 | 12672 | **63.36×** |
| random | hybrid | 147 (log 132 + data 15) | 641 | **3.21×** |
| sequential | page-level | 200 | 200 | **1.00×** |
| sequential | block-level | 3 | 256 | **1.28×** |
| sequential | hybrid | 137 (log 136 + data 1) | 208 | **1.04×** |

The two workloads are the whole point. Block-level mapping is not "slow" — it is slow precisely when writes are smaller than a block and scattered, which is the condition §44.9 names.

Note the hybrid's entry count on the sequential workload: 137, of which 136 are per-page log entries. The count is taken *before* the merge, and no merge has run — the log table is at its full budget because the workload of 200 writes never outruns 4 log blocks × 64 pages. Those 136 pointers are exactly what a later merge collapses into one block pointer each. The hybrid row therefore proves the *data* cost (208 bytes against block-level's 256 and page-level's 200), not that its mapping table is small: before cleaning, a hybrid's table is larger than block-level's by design.

`scenario=mapping-cost`, using §44.9's own figures (4 KB page, 256 KB block, 4-byte entry, 64 log blocks):

| strategy | table size |
|---|---|
| page-level | 1 GB |
| block-level | 16 MB |
| hybrid | 16 MB + 16 KB (one 4-byte entry per page of every log block: 64 pages × 4 B = 256 B per log block) |

1 GB is the chapter's number. The reduction is exactly `Size_block/Size_page` = 64×. The hybrid's log table is the only per-page memory it adds, and it is bounded by the log-block budget rather than by capacity — which is why 64 log blocks cost 16 KB against page-level's 1 GB.

## Bugs found while building this

1. **`Dictionary.TryGetValue` wrote 0 on a miss, and block 0 is real.** The hybrid merge read `oldBase = 0` for a chunk that was not in the data table, treated that as a valid location, and freed block 0 — taking live data with it. Fixed by testing the `TryGetValue` result rather than the value.

2. **The merge freed its own source.** A merge copies a chunk's surviving pages into a fresh block, then freed the old block — but the old block could be the very log block being read from. The free now happens after the replacement is committed.

3. **The log block was retired after being promoted.** A successful switch merge turns the log block into the data block and removes it from the log list; the retirement pass then erased it anyway, leaving a block pointer aimed at erased flash.

4. **The page-level collector destroyed live data.** Its first version erased the current log block to reclaim it, but a page-level table has no block pointer, so every page in it is still referenced. It now erases only blocks with no live page, scanning the whole device because a block that filled earlier was never returned to the free pool.

5. **`MergeLogBlocks` was a no-op that hid the above.** For the block-level FTL it also did nothing, so a half-assembled chunk could not be read back. It now commits the pending chunk.

Four more were found by a code review pass, after the slice's own tests were green:

6. **A complete but permuted log block was promoted as a switch merge.** Switch promotion repoints the data table at the log block's base address, which is only valid when logical offset N already sits at base+N. Writing a chunk out of order and promoting it handed every offset the wrong page — `Read(0)` returned the value written to logical 2. The promotion now requires physical alignment, and a misaligned block is rewritten in offset order instead.

7. **A failed write destroyed the previous value.** Both page-level and hybrid invalidated the old page before allocating the replacement. On a full device the allocation then threw, and the LBA — whose old mapping was the only remaining copy — became unreadable. Both now program the replacement first and drop the old mapping after.

8. **The hybrid table size was multiplied by the entry size twice**, inflating the log's share 4×. And ceiling division written `(a + b - 1) / b` wrapped negative near `long.MaxValue`, which the saturation helper could not repair because it assumed a non-negative count.

9. **The log-block count was accepted and ignored.** `logBlocks` was stored in a field nothing read, so the FTL merged on every rollover regardless of the setting. The budget is now real: the FTL keeps that many blocks outstanding and cleans the oldest only when it runs out of room.

Each of these is now covered by a test that was mutation-checked — reverting the fix makes the corresponding test fail.

## Tests

- `ftl mapping table for a 1TB drive matches the §44.9 worked figures` — 1 GB and 16 MB exactly, 64× apart, and the hybrid table between them. The oracle is the chapter's own arithmetic, not the implementation's output.
- `block-level FTL preserves the page offset within a block` — chunk 500 (OSEP's worked example) → four logical pages on four consecutive physical pages; a partial-block write leaves the other three readable.
- `block-level FTL pays full read-modify-write on a small write` — one client write, eight pages programmed. Asserted as a delta, not a running total.
- `sequential writes trigger switch merges that copy nothing` — `SwitchMerges > 0`, `PagesCopiedOnMerge == 0`, amplification exactly 1.00, every address readable after the merge.
- `scattered writes force partial or full merges and copy pages` — no switch merge, and pages are actually copied.
- `hybrid FTL merge cost is switch < partial < full` — 0 < 2 < 12 pages copied.
- `hybrid FTL write amplification sits between page-level and block-level` — 40 scattered writes over a 512-LBA space on a 32-page-per-block device, asserting `page < hybrid < block` as inequalities rather than pinning numbers. The 1.00× / 3.21× / 63.36× figures in the table above come from the route (`writes=200`, 1024 LBAs), which is a different workload; the test deliberately does not re-pin those values, because they move with the workload and a test that quoted them would be testing the RNG seed.
- `hybrid FTL keeps per-page log writes and amortizes them into a data block` — after a merge the log table is empty and every address reads through a block pointer.
- `a permuted log block is not promoted as a zero-copy switch merge` — writing a complete chunk out of order still reads back correctly, and is counted as a copying merge rather than a switch.
- `a failed write leaves the previous value readable` — an overwrite on a full device throws, and the old value is still readable.
- `mapping cost arithmetic survives huge capacities and exact hybrid size` — the exact hybrid size, and non-negative results at `long.MaxValue`.
- `hybrid FTL log block budget delays the first merge` — a larger log budget produces fewer merges over the same writes.

The switch-merge, permuted-block, failed-write and arithmetic assertions were each mutation-checked: reverting the corresponding fix makes that test fail.

## Three corrections to the original spec

1. **The table is sized by capacity, not by write count.** The spec asserted ≈1000 entries for 1000 writes; the table is indexed by logical address space, so it is `capacity/page_size` entries no matter how many writes arrive.
2. **256 pages per block is not in OSTEP.** §44.9 says blocks "can be 256KB or larger", which with a 4 KB page is 64 pages per block — so the block-level table is 16 MB, not the spec's 4 MB.
3. **A `LogBuffer` on the block-level FTL would have deleted the trade-off.** Per-page pointers into a side area *are* the hybrid FTL. Block-level does the read-modify-write the chapter describes; the log table belongs to hybrid.

Also: a switch merge copies zero pages, but the block it replaces still has to be erased. The spec's "zero extra I/O" conflated copying with erasing.

## What this slice does NOT do

- **DFTL** (demand-based selective caching, [GP07]) — the modern answer to §44.9's problem. Out of scope.
- **Wear-aware allocation.** M26 tracks erase counts; M32 does not steer writes by them.
- **Over-provisioning.** Real drives reserve spare blocks [A+08]; here every block is visible, so the page-level baseline fills sooner than it would in reality.
- **FAST** and other research FTLs.

## Source documents

- `docs/learning/m26-ssd/overview.md` — predecessor surface.
- `docs/adr/0022-m32-block-hybrid-ftl.md` — decision record, including the three spec corrections.
- OSEP Ch. 44 §44.7 — page-level mapping table.
- OSEP Ch. 44 §44.9 — table size, block-based mapping, hybrid mapping, the three merges.