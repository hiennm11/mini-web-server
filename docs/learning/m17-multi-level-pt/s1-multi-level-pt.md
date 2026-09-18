# Slice 17.1: Two-Level Page Table

## What it does

Refactors the Pager to use an `IPageTableLookup` strategy. The Pager holds one lookup per process — either `LinearLookup` (slice 14.1, default) or `TwoLevelLookup` (slice 17.1, opt-in via `?pt=level2`).

The HTTP route `/pager/run?pt=level2` enables the 2-level structure. The trace shows each page-table hit as `pt-walk-2lvl frame=X` (vs `pt-walk frame=X` for linear). At the end, a memory-savings line compares the two structures' byte cost.

## Files added/changed

- `src/MiniWebServer.Host/MiniPager/PageDirectory.cs` (new, ~90 lines):
  - `PageDirectoryEntry` (PDE: Valid, PageTableFrame).
  - `PageDirectory` (array of PDEs).
  - `PageTableEntry` (PTE: Valid, FrameNo, Dirty, Referenced).
  - `InnerPageTable` (array of PTEs).
- `src/MiniWebServer.Host/MiniPager/Pager.cs`:
  - Added `IPageTableLookup` interface.
  - `LinearLookup` (slice 14.1, factored out of `PageTable`).
  - `TwoLevelLookup` (slice 17.1, new).
  - `Pager._lookups` is now `Dictionary<int, IPageTableLookup>`.
  - `Pager.CreateProcess(pid, twoLevel = false)` — pass `true` for 2-level.
  - `Pager.Translate()` dispatches via `IPageTableLookup.TryTranslate()`.
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` — `PagerRunner` accepts `twoLevel` flag; prints memory-savings line.
- `src/MiniWebServer.Host/Program.cs` — `/pager/run?pt=linear|level2` query parameter.

## OSEP alignment

Implements OSEP §20.1 (two-level page table), §20.3 (translation algorithm), §20.4 (memory savings motivation).

## Smoke evidence

```
$ curl 'http://localhost:8080/pager/run?workload=array&frames=16&pt=level2'
...
=== stats: accesses=17 hits=7 faults=10 outofrange=0 trace_events=17
=== PT memory: 2-level used 24,576 bytes vs linear would use 16,777,216 bytes (saved 16,752,640)
```

24 KB vs 16 MB — a 683× reduction. With only 4 mapped pages, the 2-level structure uses 1 PGD entry + 1 PT page (each ~12 KB) while linear allocates the full 2^20-entry array (~16 MB).

## OSEP concept

> "the page directory, which has an entry for each page in the top level of the page table. Each PDE contains a valid bit and a page frame number, just like a PTE, but the page frame number here is to a page of the page table, not to a page of user data." (OSEP §20.1)

> "Thus, our simple solution is a two-level page table, which effectively chops up the linear page table into page-sized units. With a 32-bit address space, we can split the virtual page number (VPN) into two pieces: the top bits are the index into the top-level directory, and the bottom bits are the offset within the page of the page table." (OSEP §20.1)

## .NET mechanism

- `IPageTableLookup` interface — `TryTranslate(int vpn, out int frameNo)`, `Map(int vpn, int frameNo)`, plus `MemoryBytes` / `PopulatedEntries` / `Capacity`.
- `TwoLevelLookup._pts` is `Dictionary<int, InnerPageTable>` — only allocated on first `Map()` for a PGD index.
- `Marshal.SizeOf<T>()` gives actual byte cost for the comparison line.

## What this slice does NOT do

- 3-level or 4-level page tables.
- PT pages stored in `PhysicalMemory` (would consume real frames, complicates allocation).
- Multi-process shared PGD (Linux kernel has this).
- Inverted page tables (OSEP Ch. 21 alternative).

## Deferred (next slice candidates)

- M18 replacement policy (Ch. 21/22).
- M19 complete VM systems (Ch. 23).
