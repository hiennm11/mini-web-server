# Milestone 14: Linear Page Table

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does the OS translate a virtual address to a physical address using a page table?

## Scope

A user-space paging simulator with the linear page table data structure. Multi-process, with trace output for each memory access. Exposed via `/pager/run` HTTP route.

## Slice

- **[s1-pager.md](./s1-pager.md)** — `MiniPager` library: `VirtualAddress` / `PhysicalAddress`, `Pte`, `PhysicalMemory`, `PageTable`, `Pager`, `Workloads` + `PagerRunner`. HTTP route `/pager/run?workload=seq|rand|two&frames=N`.

## OSTEP coverage

- **Ch. 18 Introduction to Paging**
  - §18.1 example: 64-byte address space, 16-byte pages, 4 pages, 8 page frames.
  - §18.2 Where Are Page Tables Stored? — 32-bit VA + 4 KB pages = 20-bit VPN = 2^20 PTEs = 4 MB per process. *This is exactly what we use*.
  - §18.3 What's Actually In The Page Table? — Valid bit, protection bits (R/W/X), Present bit, Dirty bit, Reference/Accessed bit. Plus PFN. *The full PTE structure.*
  - §18.4 Paging: Also Too Slow — extra memory reference per instruction fetch + data access. The motivation for TLB (Ch. 19).
  - §18.5 A Memory Trace — the `array.c` example showing the I/O overhead of paging.
  - §18.6 Summary.

- **Ch. 19 Translation Lookaside Buffers** — TLB motivation + algorithm. *We don't implement a TLB (deferred to M14.2).*

## OSEP §-specific deviations

### Translation outcome naming

OSEP §18.4 Figure 18.6 uses three distinct exceptions:
- `SEGMENTATION_FAULT` — `PTE.Valid == False` (no mapping).
- `PROTECTION_FAULT` — `CanAccess(ProtectBits) == False`.
- `PAGEFAULT` (lowercase in OSEP, lowercase here too) — *reserved* by OSEP §18.3 for "valid but not present" (i.e., needs swap-in).

Our `TranslateOutcome`:
- `Hit` — Valid + present.
- `PageFault` — Valid = false. **The label is misleading**; OSEP calls this SEGFAULT in the no-swap case. Our "PageFault" really means "invalid PTE" per OSEP's terminology.
- `OutOfRange` — VPN beyond configured address space.

### PTE fields

OSEP §18.3 lists: Valid, Protection (R/W/U/S), Present, Dirty, Reference/Accessed, PFN.

Our `Pte`: `Valid`, `FrameNo`, `Dirty`, `Referenced`. Missing: Protection bits, Present bit.

Adding `Present` is required for the OSEP-defined `PAGEFAULT` semantics. Adding it is in M14.4's plan (replacement policy needs it for write-back decisions).

### Address-space size

OSEP §18.1 example uses 16-bit VA (64-byte address space, 16-byte pages). OSEP §18.2 uses 32-bit VA (4 GB address space, 4 KB pages).

We use 32-bit VA / 4 KB pages (matching OSEP §18.2's "32-bit address space with 4 KB pages → 20-bit VPN" example).

### Memory-trace overhead

OSEP §18.5 walks through an `array.c` trace showing 10 memory accesses per loop iteration (4 instruction fetches, 1 explicit store, 5 page-table accesses). Our simulator doesn't model this — every Translate is O(1) array index, not a memory fetch. The point of the simulator is to exercise the page-table lookup path, not to measure instruction-side costs.

## Key OSEP quotes

> "Paging has many advantages over previous approaches (such as segmentation). First, it does not lead to external fragmentation... Second, it is quite flexible, enabling the sparse use of virtual address spaces. However, implementing paging support without care will lead to a slower machine (with many extra memory accesses to access the page table) as well as memory waste (with memory filled with page tables instead of useful application data)." (OSEP §18.6)

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
