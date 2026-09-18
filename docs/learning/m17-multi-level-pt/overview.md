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
  - §20.1 "A Two-Level Page Table" — Figure 20.3: PGD + PT structure.
  - §20.2 "A Two-Level Page Table Entry" — PDE = (Valid, PT Frame No).
  - §20.3 "Translating with a Two-Level Page Table" — VPN decomposition: PGD index (top bits) + PT index (middle bits) + offset (bottom bits).
  - §20.4 "A Memory Trace" — the array.c trace that motivates multi-level. With 32-bit VA + 4 KB pages, a process with 4 used pages allocates 1 PGD entry + 1 PT page = 8 KB instead of 4 MB linear.

## OSEP §-specific deviations

- OSEP §20.1 example: 32-bit VA, 4 KB pages → 10-bit PGD index + 10-bit PT index + 12-bit offset. We use the same bit layout (constants `PgdBits = 10`, `PtBits = 10`, `OffsetBits = 12`).
- OSEP §20.1 "page directory" + "page-of-pages" array. Our `TwoLevelLookup` uses `Dictionary<int, InnerPageTable>` to hold the inner PT pages — only populated PGD entries get a real PT. This matches OSEP's "Page Directory Entries" + "Page Table Entries" structure.
- OSEP §20.3 "the page directory is used to find the right page-of-pages" → walk PGD first, then walk PT. Our `TryTranslate` does this in two steps: `Decompose(vpn)` returns `(pgdIndex, ptIndex)`, then we look up PDE, then PTE.
- We don't store the PT pages in `PhysicalMemory` (would be more realistic — the PT itself takes physical frames — but complicates allocation). The simulator already has a frame table for user data; the PT structure is purely in-memory.

## Key OSEP quotes

> "If we want a large address space, we are forced to use a small page size, or the page table will be too big. Both are problematic. With a small page size, we waste a lot of memory in the page table — the page table is itself divided into pages, and each page of the page table may take up many pages of physical memory." (OSEP §20 intro)

> "Thus, our simple solution is a two-level page table, which effectively chops up the linear page table into page-sized units. With a 32-bit address space, we can split the virtual page number (VPN) into two pieces: the top bits are the index into the top-level directory, and the bottom bits are the offset within the page of the page table." (OSEP §20.1)

## .NET mechanism

- `IPageTableLookup` is a small interface (`TryTranslate`, `Map`, `MemoryBytes`, `PopulatedEntries`, `Capacity`). The Pager holds a `Dictionary<int, IPageTableLookup>` keyed by PID.
- `TwoLevelLookup._pts` is `Dictionary<int, InnerPageTable>` — only populated PGD entries get an inner PT. Allocated on first `Map()` call for a given PGD index.
- `MemoryBytes` calculation uses `Marshal.SizeOf<T>()` to give the actual byte cost in a real C# process. The slice doc prints the savings at the end of the trace.

## Files

- `src/MiniWebServer.Host/MiniPager/PageDirectory.cs` (new, ~90 lines) — `PageDirectoryEntry`, `PageDirectory`, `PageTableEntry`, `InnerPageTable`.
- `src/MiniWebServer.Host/MiniPager/Pager.cs` — added `IPageTableLookup` + `LinearLookup` + `TwoLevelLookup`. `Pager.CreateProcess(pid, twoLevel)` chooses. `Pager.Translate()` dispatches through `IPageTableLookup.TryTranslate`.
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` — `PagerRunner` accepts `twoLevel` flag; emits the memory-savings line.
- `src/MiniWebServer.Host/Program.cs` — `/pager/run?pt=linear|level2` query parameter.

## What this slice does NOT do

- 3-level or 4-level page tables (x86-64 uses 4-level; Linux uses 5-level on newer CPUs).
- Storing the inner PT pages in `PhysicalMemory` (would consume real frames).
- TLB shootdown on context switch (we use ASID + TLB lookup; slice 16).
- Inverted page tables (OSEP Ch. 21 alternative).

## Where this leads

- M18 replacement policy (Ch. 21/22) — when physical memory is full, which frame to evict?
- M19 complete VM systems (Ch. 23) — capstone combining paging + swap + replacement.
