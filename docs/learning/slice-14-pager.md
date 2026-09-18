# Milestone 14 (slice 14.1): Linear Page Table + VA→PA Translation

## What is paging?

OSEP Ch. 18 introduces paging as the dominant approach to virtual memory in modern systems. Instead of giving each process a contiguous region of physical memory (base + bound), the OS gives each process a virtual address space, divides that space into fixed-size pages, and maintains a page table that maps each virtual page to a physical frame.

The translation pipeline (OSEP §18.4, Figure 18.6):

```
1. VPN = (VirtualAddress & VPN_MASK) >> SHIFT
2. PTEAddr = PTBR + (VPN * sizeof(PTE))
3. PTE = AccessMemory(PTEAddr)
4. if (PTE.Valid == False)        RaiseException(SEGMENTATION_FAULT)
5. else if (CanAccess(ProtectBits) == False)  RaiseException(PROTECTION_FAULT)
6. else
7.     offset = VirtualAddress & OFFSET_MASK
8.     PhysAddr = (PTE.PFN << SHIFT) | offset
9.     Register = AccessMemory(PhysAddr)
```

This slice implements the linear (single-level) page table — the simplest possible version, matching OSEP §18.3 ("Linear Page Table").

### Implementation deviations vs. OSEP §18

1. **TranslateOutcome naming**: OSEP distinguishes **SEGMENTATION_FAULT** (no PTE for the VPN at all) from **PROTECTION_FAULT** (PTE exists but access not allowed) from **PAGEFAULT** (PTE says "present bit = 0", i.e., needs swap-in). Our `TranslateOutcome` has only three values:
   - `Hit` — PTE.Valid and in memory → PA computed.
   - `PageFault` — PTE.Valid is false → no mapping. **This name is misleading**; OSEP calls this a SEGMENTATION_FAULT in the no-swap case. The literal "page fault" in OSEP terminology is reserved for "valid but not present" which requires swap (Ch. 21).
   - `OutOfRange` — VPN beyond the configured address space.

   The naming is a tradeoff: "PageFault" is the colloquial term most readers expect, even though OSEP distinguishes it from SEGFAULT. Documented.

2. **PTE fields**: OSEP §18.3 lists: Valid, Protection (R/W/U/S), Present, Dirty, Reference (Accessed), PFN. Our `Pte` has only `Valid`, `FrameNo`, `Dirty`, `Referenced`. Missing: Protection bits, Present bit. The Present bit is what makes the OSEP `PAGEFAULT` distinguishable from `SEGMENTATION_FAULT`. Adding `Present` is in M14.4's plan (replacement policy uses Dirty + Referenced).

3. **VA size**: OSEP examples use 16-bit VA (64-byte address space, 16-byte pages = 4 pages) for illustration, and 32-bit VA (4 GB address space, 4 KB pages) for realistic. Our simulator uses 32-bit VA / 4 KB pages (20-bit VPN), matching OSEP §18.2's "32-bit address space with 4 KB pages → 20-bit VPN" example.

4. **VA decomposition in .NET**: OSEP's algorithm uses bit shifts on unsigned values. Our `Vpn` getter does `(int)((uint)Value >> 12)` to get unsigned-style right shift (preserves negative values as positive before shifting). This matches `VPN = (VirtualAddress & VPN_MASK) >> SHIFT` for 32-bit addresses.

5. **§18.5 memory trace**: OSEP walks through a `for (i = 0; i < 1000; i++) array[i]++` trace showing 10 memory accesses per loop iteration (4 instruction fetches, 1 explicit store, 5 page-table accesses for translation). Our simulator doesn't model this overhead — it just reports whether each Translate succeeded or faulted. The point of the simulator is to exercise the page-table lookup path, not to measure instruction-side costs.

6. **§19 TLB**: The motivation for Ch. 19's TLB is that pure paging requires "one extra memory reference in order to first fetch the translation from the page table" (§18.4). Our simulator doesn't model this cost; every Translate is O(1) array index. TLB simulation is M14.2 (deferred).

## The mini implementation

`src/MiniWebServer.Host/MiniPager/` (6 files, ~360 lines):

- `VirtualAddress.cs` — VA struct, decompose into VPN + offset. PA struct.
- `Pte.cs` — Page table entry (Valid, FrameNo, Dirty, Referenced).
- `PhysicalMemory.cs` — Flat byte array of `numFrames * PAGE_SIZE` bytes.
- `PageTable.cs` — Per-process linear page table (PTE array).
- `MemoryAccess.cs` — Workload item (Pid, VA, AccessKind) + trace event.
- `Pager.cs` — Multi-process page-table manager + Translate pipeline.
- `Workloads.cs` — Sequential / Random / TwoProcesses workloads + PagerRunner.

### Key design decisions

1. **PAGE_SIZE = 4096 (4 KB)** — the canonical small page size. x86_64 and ARM64 both default to 4 KB pages.

2. **32-bit virtual addresses** — the simulator uses `int` for VA/PA. That gives 20-bit VPN (2^20 = ~1M pages per process) and 12-bit offset (4 KB). Real x86_64 is 48-bit; we stay at 32-bit for simplicity.

3. **Linear (flat) page table** — every process gets a `Pte[]` of length 1 << 20 entries. This is the textbook trade-off: simple, fast O(1) lookup, but wastes memory for sparse address spaces (4 MB per process for an empty address space, since each PTE is 8 bytes). Multi-level page tables in slice 14.3 fix this.

4. **No swap in slice 14.1** — every page fault is fatal (recorded as a fault in the trace but not handled). The simulation continues but the OS would crash on a real fault. Slice 14.4 (replacement) and 14.5 (swap) add the fault-handling path.

5. **PTE.Dirty + PTE.Referenced** are placeholders for now. They're set/cleared by the access but no eviction policy uses them yet (slice 14.4 will use them).

6. **Trace-based** — every access adds an event to the trace. Events have a step number, PID, formatted VA/PA, outcome (Hit/PageFault/OutOfRange), and a detail string. The HTTP route returns the formatted trace.

7. **Two-process workloads** demonstrate that the page tables are per-process — pid 1's frame mapping (vpn 0-3 → frames 0-3) is independent of pid 2's (vpn 0-3 → frames 4-7).

## HTTP integration

`/pager/run?workload=seq|rand|two&frames=N` returns the trace as plain text.

- `seq` — sequential single-process (16 accesses to vpn 0, offsets 0-3840)
- `rand` — random single-process (20 accesses to vpn 0-7, only 0-3 mapped)
- `two` — two-process overlap (12 accesses, all pre-mapped)

## Smoke evidence (.gitnexus/smoke-m14-1.ps1)

| Workload | Expected | Actual |
|---|---|---|
| seq | 16 hits, 0 faults | ✓ 16/0 |
| rand | ~11 hits, ~9 faults | ✓ 11/9 |
| two | 12 hits, 0 faults | ✓ 12/0 |
| unknown | 400 | ✓ 400 |

### Sample trace (sequential)

```
=== Pager run: 8 frames (32768 bytes physical) ===

step=  1 pid=1 va=0x00000000 (vpn=0, off=0) -> pa=0x00000000 (frame=0, off=0) Hit         frame=0
step=  2 pid=1 va=0x00000100 (vpn=0, off=256) -> pa=0x00000100 (frame=0, off=256) Hit         frame=0
...
step= 16 pid=1 va=0x00000F00 (vpn=0, off=3840) -> pa=0x00000F00 (frame=0, off=3840) Hit        frame=0

=== stats: accesses=16 hits=16 faults=0 outofrange=0 processes=1 ===
```

### Sample trace (random)

```
step=  1 pid=1 va=0x00005241 (vpn=5, off=577)                 PageFault   vpn 5 not mapped (no swap in slice 14.1)
step=  2 pid=1 va=0x00003312 (vpn=3, off=786) -> pa=...         Hit         frame=3
...
=== stats: accesses=20 hits=11 faults=9 outofrange=0 processes=1 ===
```

## OSEP concept

This slice implements the simplest version of OSEP §18.5-§18.7. The translation pipeline is exactly the one OSEP §18.7 walks through. The page-fault behavior is the prelude to §21's replacement policy and §22's swap mechanism.

## .NET mechanism

- `VirtualAddress` and `PhysicalAddress` are `readonly record struct` — zero allocation for value semantics.
- `Pte` is a `struct` (mutable, used inside arrays).
- The `Pager` is a plain class holding a `Dictionary<int, PageTable>`.
- The trace is a `List<TraceEvent>`; events are immutable records.
- Page table lookup is O(1) array index, just like real hardware MMUs.

## Files added (slice 14.1)

- `src/MiniWebServer.Host/MiniPager/VirtualAddress.cs` (60 lines)
- `src/MiniWebServer.Host/MiniPager/Pte.cs` (25 lines)
- `src/MiniWebServer.Host/MiniPager/PhysicalMemory.cs` (45 lines)
- `src/MiniWebServer.Host/MiniPager/PageTable.cs` (55 lines)
- `src/MiniWebServer.Host/MiniPager/MemoryAccess.cs` (50 lines)
- `src/MiniWebServer.Host/MiniPager/Pager.cs` (105 lines)
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` (90 lines)
- `src/MiniWebServer.Host/Program.cs` — added `/pager/run` route (~45 lines)
- `docs/adr/0007-mini-pager-mmu-tlb-multilevel-replacement.md` — roadmap ADR (130 lines)
- `.gitnexus/smoke-m14-1.ps1` — smoke script

## Deferred (per ADR 0007)

- **M14.2 (TLB)**: small hardware cache with ASID; demonstrates the 99% hit-rate property.
- **M14.3 (multi-level page tables)**: 2-level PD/PT split for sparse address spaces.
- **M14.4 (replacement policy)**: LRU / Clock eviction when physical memory is full.
- **M14.5 (complete VM)**: backing store on MiniFs + swap in/out simulation.
- **M14.6 (HTTP integration)**: JSON output, workload DSL.

## What you can do now

```bash
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj

curl "http://localhost:8080/pager/run?workload=seq&frames=8"
curl "http://localhost:8080/pager/run?workload=rand&frames=8"
curl "http://localhost:8080/pager/run?workload=two&frames=16"
```

Vary `frames` and `workload` to see how the address translation behaves.
