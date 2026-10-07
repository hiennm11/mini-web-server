# ADR 0022: M32 Block-Level and Hybrid FTL (OSEP Ch. 44 §44.9)

## Status

Accepted

## Date

2026-10-07

## Context

M26's `Ssd` is a page-level FTL: the mapping table holds one entry per logical page, and §44.9 says plainly that this does not scale.

> "The second cost of log-structuring is the potential for extremely large mapping tables, with one entry for each 4-KB page of the device. With a large 1-TB SSD, for example, a single 4-byte entry per 4-KB page results in 1 GB of memory needed by the device, just for these mappings! Thus, this page-level FTL scheme is impractical."

§44.9 then gives the two ways out, and what each one costs. Block-based mapping keeps "a pointer per block of the device, instead of per page, reducing the amount of mapping information by a factor of `Size_block/Size_page`" — but pays for it on small writes. Hybrid mapping keeps per-page pointers into a few **log blocks** and per-block pointers for everything merged, and pays for the memory with **switch / partial / full merge** work.

### Three corrections to the milestone spec

**1. The mapping table is a function of capacity, not of write count.** `s1-block-level-ftl.md` asserted that 1000 writes to a page-level FTL give ≈1000 table entries and to a block-level FTL give ≈1000/pages-per-block. That is wrong on its own terms: the table is indexed by **logical address space**, so its size is `capacity / page_size` (or `/ block_size`) entries regardless of how many writes have arrived. The scenario's `mappingTableEntries` therefore measures entries *touched*, and `FtlMappingCost.TableBytes` is the thing that answers the chapter's question.

**2. The spec's 256-pages-per-block figure is not in OSTEP.** The spec's smoke evidence reads "1 TB / 4 KB pages / 256 pages per block → block-level = 4 MB". §44.9 never states a page count per block; it says real blocks "can be 256KB or larger". With a 4 KB page that is **64** pages per block, so the block-level table is **16 MB**, not 4 MB. The implementation uses the book's own numbers (4 KB page, 256 KB block, 4-byte entry) and the reduction factor is exactly `Size_block/Size_page` = 64×.

**3. A `LogBuffer` on the block-level FTL would have deleted the trade-off.** The spec asked for `BlockLevelFtl.LogBuffer` — "a small set of data blocks where partial-block writes queue until the block fills". But per-page pointers into a side area *are* the hybrid FTL. Giving block-level a per-page buffer and then measuring it would have made the two strategies identical and taught the opposite of §44.9. The block-level FTL here does the read-modify-write the chapter describes, and the hybrid FTL is where the log table lives.

A fourth, smaller one: the spec said a switch merge has "zero extra I/O". What §44.9 says is that nothing is *copied* — "In this best case, all the per-page pointers required replaced by a single block pointer" — while the block it replaces still has to be erased. The implementation's switch merge copies zero pages and still erases the old block.

## Decision

### `FtlMappingCost` — the chapter's arithmetic, isolated

Pure arithmetic over capacity numbers, so the worked figures can be checked without instantiating a 1 TB device. Returns page-level, block-level and hybrid table bytes. The hybrid size is the block table plus a bounded per-page log table.

### `RawFlash` — the media underneath

Page state, program/erase rules and wear counters, shared by all three strategies so they differ only in translation. M26's `Ssd` keeps its own copy of this logic because it *is* the page-level FTL, not a layer above one.

### `PageLevelFtl`, `BlockLevelFtl`, `HybridFtl`

All three implement `IFtlStrategy`, so the comparison drives them uniformly and asserts that every address reads back after cleaning.

- **Page-level** is the §44.7 baseline. Its garbage collector can only erase a block once *every* page in it is dead — there is no block pointer to fold a log block into, which is exactly the limitation §44.9 is about.
- **Block-level** maps logical *chunks*. A small write into a chunk already on the device copies the whole block out, which is the "small write" cost the chapter spells out. A chunk being assembled is held until it is full, because a block pointer cannot address a half-written block.
- **Hybrid** keeps per-page log pointers and per-block data pointers, consults the log table first, and cleans when the log blocks are full. A merge is classified by what the block holds: a complete chunk **whose physical layout already matches its logical offsets** → **switch** (the log block becomes the data block, nothing copied); otherwise a copying merge. The log-block count is a real budget — the FTL keeps that many blocks outstanding and cleans the oldest only when it runs out of room.

### Route

Three scenarios on the existing `/ssd/run`, dispatched before M26's simulator is constructed since none of them need it: `ftl-comparison`, `mapping-cost`, `merges`. Unknown `ftl` or `workload`, and non-positive `writes`/`lbas` or negative `logblocks`, answer 400 rather than silently running the default workload.

## Consequences

### Positive

- **§44.9's worked numbers are reproduced**, not paraphrased: 1 TB → 1 GB page-level table, 16 MB block-level, exactly 64× apart.
- **The trade-off is measurable, and it is workload-dependent.** Under scattered writes the three strategies measure 1.00× / 63.36× / 3.21×; under sequential writes they measure 1.00× / 1.28× / 1.04×. Block-level mapping is not simply "slow" — it is slow precisely when writes are smaller than a block, which is the condition §44.9 names.
- **The switch merge is proven zero-copy**, not asserted: sequential writes produce switch merges with `PagesCopiedOnMerge == 0` and amplification exactly 1.00.

### Negative

- **Merge classification is by chunk count, not by the chapter's figures.** §44.9's partial and full merges are distinguished by *where the sibling pages live* (one other block vs many). The simulator distinguishes them by how many chunks a log block touches, which produces the same three-way ordering but is a simplification of the book's picture.
- **No wear leveling.** M26 tracks erase counts; M32 does not steer writes by them, so the measured amplification is not what a wear-aware FTL would achieve.
- **No over-provisioning.** Every block is client-visible, so the page-level baseline runs out of space earlier than a real drive with 7–28% spare [A+08] would.
- **No DFTL.** Demand-based selective caching [GP07] is the modern answer to §44.9's problem and is out of scope; the slice shows the cost it avoids, not the fix.

## Verification

- Build clean (`dotnet build`).
- 12 tests covering the 1 TB table sizes and the `Size_block/Size_page` factor; block-level offset preservation and its full-block copy on a small write; sequential writes producing zero-copy switch merges; scattered writes producing copying merges; the three merge costs being strictly ordered; hybrid amplification landing between the other two; a permuted log block refusing promotion; a failed write leaving the previous value readable; the hybrid table's exact size and non-negative results at `long.MaxValue`; and a larger log budget delaying the first merge.
- Five assertions were mutation-checked by reverting each fix and confirming the corresponding test fails: switch-merge alignment, failed-write safety, the hybrid size arithmetic, and the overflow-safe ceiling division.
- Smoke: all three scenarios return 200, bad `ftl`/`workload`/`writes`/`logblocks` return 400, M26's `write`/`gc`/`wear` are unchanged, and 24 requests leave the thread count flat.

### Defects found by review after the slice's own tests were green

Nine, all now fixed and covered:

1. `Dictionary.TryGetValue` writing 0 on a miss, where block 0 is a real block — the hybrid merge freed live data.
2. A merge freeing its own source block before committing the replacement.
3. A log block being retired after a switch merge had promoted it into a data block.
4. The page-level collector erasing the live log block, since a page-level table has no block pointer to fold into.
5. `MergeLogBlocks` being a no-op that hid the first four.
6. **A complete but permuted log block being promoted as a zero-copy switch merge**, handing every logical offset the wrong page.
7. **A failed write destroying the previous value** — the old mapping was invalidated before the replacement was allocated, so an overwrite that ran out of space left the LBA unreadable.
8. **The hybrid table size multiplying by the entry size twice**, and ceiling division written `(a + b - 1) / b` wrapping negative near `long.MaxValue`.
9. **The log-block count being accepted and ignored** — stored in a field nothing read, so every rollover merged regardless of the setting.

The first five were found by the tests written alongside the code; the last four by a review pass, which is why they had a dedicated test each rather than an extension of an existing one.

## Source Documents

- `docs/learning/m32-ssd-extensions/overview.md` — milestone scope.
- `docs/learning/m32-ssd-extensions/s1-block-level-ftl.md` — slice doc.
- `docs/learning/m26-ssd/overview.md` — predecessor surface (page-level FTL, GC, wear).
- OSTEP Ch. 44 §44.7 — page-level mapping table, "the second is high cost of in-memory mapping tables".
- OSEP Ch. 44 §44.9 — the 1 TB / 4 KB / 4-byte entry figure; `Size_block/Size_page`; the small-write copy cost; hybrid log/data tables; switch, partial and full merge.