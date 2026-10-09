# Milestone 17: Multi-level Page Tables

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

A linear page table uses too much memory for sparse address spaces. How do we structure the page table hierarchically so only the populated regions consume memory?

## Scope

Add a 2-level page table (Page Directory + Page Tables) alongside the linear one (slice 14.1). The Pager chooses which to use per process. The TLB (slice 16) works with either — it caches VPN→PFN regardless of how the PT is structured.

## Slice

- **[s1-multi-level-pt.md](./s1-multi-level-pt.md)** — `IPageTableLookup` interface, `LinearLookup` + `TwoLevelLookup` implementations, `Pager` dispatch.

## OSTEP coverage

- **Ch. 20 Advanced Page Tables**
  - §20.1 "Simple Solution: Bigger Pages" — 16 KB pages cut the table from 4 MB to 1 MB, at the cost of internal fragmentation. Not implemented.
  - §20.2 "Hybrid Approach: Paging and Segments" — one page table per segment, selected by segment bits, with a bounds register per segment. Not implemented.
  - §20.3 "Multi-level Page Tables" — the implemented one. A page directory of PDEs points to pages of the page table; a fully-invalid page of the table is never allocated. Figure 20.3 contrasts linear (left) with multi-level (right): the linear table must hold space for the invalid middle, the multi-level one makes it disappear. The chapter's "More Than Two Levels" worked example (a 30-bit VA with 512-byte pages, where the page directory itself overflows one page and needs a third level) is inside this same §20.3 and is **not** implemented — `Pager` builds exactly two levels.
  - §20.4 "Inverted Page Tables" — one table for the whole system, indexed by physical page. Deferred; the chapter's closing point is that "page tables are just data structures", which is the argument for both this and §20.3.
  - §20.5 "Swapping the Page Tables to Disk" — deferred; the chapter defers the detail to the VMS case study.

## OSEP §-specific deviations

- OSEP §20.1 example: 32-bit VA, 4 KB pages → 10-bit PGD index + 10-bit PT index + 12-bit offset. We use the same bit layout (constants `PgdBits = 10`, `PtBits = 10`, `OffsetBits = 12`).
- OSEP §20.1 "page directory" + "page-of-pages" array. Our `TwoLevelLookup` uses `Dictionary<int, InnerPageTable>` to hold the inner PT pages — only populated PGD entries get a real PT. This matches OSEP's "Page Directory Entries" + "Page Table Entries" structure.
- OSEP §20.3 "the page directory is used to find the right page-of-pages" → walk PGD first, then walk PT. Our `TryLookup` does this in two steps: `Decompose(vpn)` returns `(pgdIndex, ptIndex)`, then we look up PDE, then PTE.
- We don't store the PT pages in `PhysicalMemory` (would be more realistic — the PT itself takes physical frames — but complicates allocation). The simulator already has a frame table for user data; the PT structure is purely in-memory.

## Key OSEP quotes

> "If we want a large address space, we are forced to use a small page size, or the page table will be too big. Both are problematic. With a small page size, we waste a lot of memory in the page table — the page table is itself divided into pages, and each page of the page table may take up many pages of physical memory." (OSEP §20 intro)

> "Thus, our simple solution is a two-level page table, which effectively chops up the linear page table into page-sized units. With a 32-bit address space, we can split the virtual page number (VPN) into two pieces: the top bits are the index into the top-level directory, and the bottom bits are the offset within the page of the page table." (OSEP §20.1)

## .NET mechanism

- `IPageTableLookup` is a small interface (`TryLookup`, `Map`, `MemoryBytes`, `PopulatedEntries`, `Capacity`). The Pager holds a `Dictionary<int, IPageTableLookup>` keyed by PID.
- `TwoLevelLookup._pts` is `Dictionary<int, InnerPageTable>` — only populated PGD entries get an inner PT. Allocated on first `Map()` call for a given PGD index.
- `MemoryBytes` calculation uses `Marshal.SizeOf<T>()` to give the actual byte cost in a real C# process. The slice doc prints the savings at the end of the trace.

## Files

- `src/MiniWebServer.Host/MiniPager/PageDirectory.cs` (new, ~90 lines) — `PageDirectoryEntry`, `PageDirectory`, `PageTableEntry`, `InnerPageTable`.
- `src/MiniWebServer.Host/MiniPager/Pager.cs` — added `IPageTableLookup` + `LinearLookup` + `TwoLevelLookup`. `Pager.CreateProcess(pid, twoLevel)` chooses. `Pager.Translate()` dispatches through `IPageTableLookup.TryLookup`.
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` — `PagerRunner` accepts `twoLevel` flag; emits the memory-savings line.
- `src/MiniWebServer.Host/Program.cs` — `/pager/run?pt=linear|level2` query parameter.

## What this slice does NOT do

- 3-level or 4-level page tables (x86-64 uses 4-level; Linux uses 5-level on newer CPUs).
- Storing the inner PT pages in `PhysicalMemory` (would consume real frames).
- TLB shootdown on context switch (we use ASID + TLB lookup; slice 16).
- Inverted page tables (OSEP §20.4, deferred).

## Where this leads

- M18 replacement policy (Ch. 21/22) — when physical memory is full, which frame to evict?
- M19 complete VM systems (Ch. 23) — capstone combining paging + swap + replacement.
