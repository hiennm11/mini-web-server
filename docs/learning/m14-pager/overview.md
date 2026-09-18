# Milestone 14: Linear Page Table

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does the OS translate a virtual address to a physical address using a page table?

## Scope

A user-space paging simulator with the linear page table data structure. Multi-process, with trace output for each memory access. Exposed via `/pager/run` HTTP route.

## Slice

- **[s1-pager.md](./s1-pager.md)** — `MiniPager` library: `VirtualAddress` / `PhysicalAddress` (decompose into VPN + offset), `Pte` (valid/frame/dirty/referenced), `PhysicalMemory` (flat byte array), `PageTable` (per-process linear array of PTE), `Pager` (multi-process manager with Translate pipeline), `Workloads` (Sequential/Random/TwoProcessesOverlap + PagerRunner). HTTP route `/pager/run?workload=seq|rand|two&frames=N`.

## OSEP concept

- **Ch. 18 Introduction to Paging** — the linear page table; VPN → PTE → PA translation pipeline (§18.4 figure 18.6).
- **§18.2** — page-table size analysis: 32-bit VA + 4KB pages = 20-bit VPN = 2^20 PTEs = 4 MB per process.
- **§18.3** — PTE fields (valid, protection, present, dirty, referenced, PFN).
- **§18.5** — the cost of paging: every memory reference requires an extra memory lookup for the page table.

## Implementation deviations

- `TranslateOutcome.PageFault` is our label for `PTE.Valid == false`. OSEP §18.4 calls this `SEGMENTATION_FAULT`; OSEP reserves `PAGEFAULT` for "valid but not present" (M14.5 swap case).
- `Pte` is missing `Present` + `Protection` bits. Adding `Present` is in M14.4 plan (replacement policy uses Dirty + Referenced).
- 32-bit VA / 4KB pages matches OSEP §18.2 example.

## .NET mechanism

- `VirtualAddress` / `PhysicalAddress` are `readonly record struct` (zero allocation for value semantics).
- `Pte` is a `struct` (mutable, used inside arrays).
- The `Pager` is a plain class holding a `Dictionary<int, PageTable>`.

## Files

- `src/MiniWebServer.Host/MiniPager/VirtualAddress.cs`
- `src/MiniWebServer.Host/MiniPager/Pte.cs`
- `src/MiniWebServer.Host/MiniPager/PhysicalMemory.cs`
- `src/MiniWebServer.Host/MiniPager/PageTable.cs`
- `src/MiniWebServer.Host/MiniPager/MemoryAccess.cs`
- `src/MiniWebServer.Host/MiniPager/Pager.cs`
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` + `PagerRunner`
- `src/MiniWebServer.Host/Program.cs` — `/pager/run` route.

## Where this leads

- M14.2 (TLB), M14.3 (multi-level page tables), M14.4 (replacement policy), M14.5 (swap on MiniFs), M14.6 (HTTP integration polish) — per `docs/adr/0007-mini-pager-mmu-tlb-multilevel-replacement.md`.
