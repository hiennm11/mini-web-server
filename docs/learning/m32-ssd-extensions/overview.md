# Milestone 32: Block-Level FTL (Ch. 44 §44.9)
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
How does the FTL mapping-table size scale with mapping granularity? Why is page-level mapping impractical for large SSDs, and how do block-level / hybrid mappings trade mapping-table RAM for write amplification?
## Scope
A side-by-side comparison of three FTL mapping strategies on top of the M26 SSD simulator:
1. **Page-level FTL** (M26 already has this, OSEP §44.7): every logical page maps to one physical page. Mapping table = `O(capacity_in_pages)` — the §44.9 "impractical for 1 TB devices" case (1 GB of mapping memory for a 1 TB / 4 KB-page SSD).
2. **Block-level FTL** (this slice, OSEP §44.9): every logical block maps to one physical block; inside a block, the offset is preserved. Mapping table = `O(capacity_in_blocks)` — `1 / pagesPerBlock` smaller.
3. **Hybrid (log-block) FTL** (OSEP §44.9): the FTL keeps a few blocks erased and directs all writes to them (called **log blocks**); per-page mappings for the log blocks, per-block mappings for the data blocks. The key is keeping the number of log blocks small via switch merge / partial merge / full merge.

The slice reports mapping-table RAM, write amplification, and GC efficiency for each strategy under the same workload (`/ssd/run?scenario=ftl-comparison&ftl=pagelevel|blocklevel|hybrid&writes=N&workload=seq|random`).

## Slice
- **[s1-block-level-ftl.md](./s1-block-level-ftl.md)** — block-level FTL class + hybrid FTL + comparison route.

## OSTEP coverage
- **Ch. 44 §44.9** "Mapping Table Size" [KK+02, GY+09]: the canonical mapping-strategy trade-off. OSEP says "this page-level FTL scheme is impractical" for 1 TB devices, then introduces block-based mapping (smaller table, higher write amplification on small writes) and hybrid mapping (log blocks with per-page maps + data blocks with per-block maps + switch/partial/full merges).
- **Ch. 44 §44.7** (predecessor): M26's page-level FTL with the mapping table.

## Files
- `src/MiniWebServer.Host/MiniScheduler/FtlMapping.cs` — `FtlMappingCost` (table arithmetic), `RawFlash` (shared media), the three FTL strategies, `HybridMergeDemo`.
- `src/MiniWebServer.Host/MiniScheduler/FtlDemos.cs` — route output formatting.
- `src/MiniWebServer.Host/Program.cs` — `/ssd/run` gains `ftl-comparison`, `mapping-cost` and `merges`.

Three scenarios rather than one: `mapping-cost` answers the chapter's table-size question with the book's own 1 TB figures, `merges` isolates the three merge kinds, and `ftl-comparison` drives all three strategies against one workload.

## Implementation deviations from OSEP

- **Mapping tables are in-memory** (consistent with M26). Real FTL tables live in a small battery-backed NOR area on the controller.
- **Merge classification is by chunk count**, not by the chapter's figures. §44.9 distinguishes partial from full merge by *where the sibling pages live* — one other block versus many. The simulator classifies by how many chunks a log block touches, which yields the same three-way ordering but simplifies the picture. See `docs/adr/0022-m32-block-hybrid-ftl.md`.
- **No wear-aware allocation.** M26 tracks erase counts; M32 does not steer writes by them.
- **No over-provisioning.** Every block is visible to the client, so the page-level baseline fills sooner than a real drive with spare blocks would.

## What this slice does NOT do

- **DFTL** (Demand-based FTL, [GP07]) — the modern solution that page-maps only the cached mapping entries. Out of scope.
- **FAST** (Fully Associative Sector Translation) — sibling research. Out of scope.
- **Wear-aware block allocation** beyond M26's simple wear counter: deferred.

## Where this leads

- Block-level FTL is what real consumer SSDs use; this slice makes the cost model observable and shows the measured figure the chapter only states: a small scattered write costs a whole block of amplification.
- Future: a future M34-style "device drivers" slice could add FTL-above-the-disk, exposing the FTL choices to the file-system layer above.
