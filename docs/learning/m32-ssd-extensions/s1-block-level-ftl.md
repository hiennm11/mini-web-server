# Slice 32.1: Block-Level FTL (Ch. 44 §44.9)
> **What it does** — adds two new FTL mapping strategies (block-level + hybrid log-block) to the M26 SSD simulator, and a `/ssd/run?scenario=ftl-comparison` driver that measures mapping-table RAM + write amplification under the same workload for all three strategies (page-level vs block-level vs hybrid).
## Surface
- `Ssd` (extended in `MiniScheduler`):
  - `class BlockLevelFtl` — maps logical blocks to physical blocks; the page offset is preserved on read/write. Per OSEP §44.9: "the FTL extracts the chunk number from the logical block address, looks up the chunk-number to physical-page mapping in the table, and computes the address of the desired flash page by adding the offset from the logical address to the physical address of the block."
  - `class HybridFtl` — keeps a few erased **log blocks** with per-page mappings, plus per-block mappings for the rest. Implements switch merge (best case), partial merge, and full merge per §44.9 figure 44.10.
  - `BlockLevelFtl.LogBuffer` — a small set of data blocks where partial-block writes queue until the block fills; then a single block-level remap happens.
- The comparison route reports per-strategy:
  - `mappingTableEntries` (proxy for RAM)
  - `writesAmplification` (data-written / host-written)
  - `gcErasedBlocks` (how many GC cycles)
## Files
- `src/MiniWebServer.Host/MiniScheduler/Ssd.cs` — add the two new classes (~100 LOC).
- `src/MiniWebServer.Host/Program.cs` — new route scenario.
## Smoke evidence
- `/ssd/run?scenario=ftl-comparison&ftl=pagelevel&writes=1000&workload=random` reports mapping-table entries ≈ 1000 (one per write).
- `/ssd/run?scenario=ftl-comparison&ftl=blocklevel&writes=1000&workload=random` reports mapping-table entries ≈ 1000 / `PagesPerBlock` (≈ 8 if pages-per-block = 8); write amplification higher because partial-block writes queue in the log buffer.
- `/ssd/run?scenario=ftl-comparison&ftl=hybrid&writes=1000&workload=random` reports a number in between, with the break-even at the update-count threshold.
- A short body that compares all three side-by-side under identical load.
- For a 1 TB SSD with 4 KB pages and 256 pages/block: page-level table = 256 M entries × 4 bytes = 1 GB. Block-level = 1 M entries = 4 MB. Hybrid = 1 M + small log = ~4 MB + small log.
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds:
  - `Run("block-level FTL mapping table is smaller than page-level", ...)` — assert `BlockLevelFtl.TableEntries() < PageLevelFtl.TableEntries()` for the same workload.
  - `Run("hybrid FTL amortizes the log buffer cost", ...)` — assert hybrid's write amplification is between page-level (lowest) and block-level (highest).
  - `Run("block-level FTL preserves page offset", ...)` — write to a page in the middle of a block, read it back, assert same data.
  - `Run("hybrid FTL switch merge best case", ...)` — assert switch merge has zero extra I/O; partial merge has 1 extra read per merged page.
## Source documents
- `docs/learning/m32-ssd-extensions/overview.md` — milestone scope.
- `docs/learning/m26-ssd/overview.md` — predecessor M26 page-level FTL.
- OSEP Ch. 44 §44.9 — block-level + hybrid mapping + switch/partial/full merge.
