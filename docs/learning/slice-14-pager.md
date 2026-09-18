# Milestone 14 (slice 14.1): Linear Page Table + VA→PA Translation

## What is paging?

OSEP Ch. 18 introduces paging as the dominant approach to virtual memory in modern systems. Instead of giving each process a contiguous region of physical memory (base + bound), the OS gives each process a virtual address space, divides that space into fixed-size pages, and maintains a page table that maps each virtual page to a physical frame.

The translation pipeline is:

1. **Decompose VA**: split the virtual address into VPN (virtual page number) and offset (within page).
2. **Look up PTE**: index the page table by VPN → get the page table entry.
3. **Check valid**: if the PTE is not valid → page fault.
4. **Compute PA**: `PA = PTE.Frame * PAGE_SIZE + offset`.
5. **Read/write physical memory** at the PA.

This slice implements the linear (single-level) page table — the simplest possible version.

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
