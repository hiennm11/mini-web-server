# ADR 0007: Mini Pager (Address Translation + TLB + Multi-Level + Replacement)

## Status

Accepted

## Date

2026-09-18

## Context

ADR 0004 lists OSEP Part II (Ch. 14-23: virtual memory) as out of scope, deferring it to its own ADR. The repo now covers OSEP's three pieces via the running server (virtualization = bounded worker pool, concurrency = locks/pools/caches, persistence = Mini FS journal), plus the recent M13 (MLFQ scheduler simulation). But the actual CPU memory-management unit (MMU) and its page-table data structures are not implemented. These are foundational to how every modern OS shares physical memory between processes.

This ADR records the next concrete milestones for the virtual-memory chapters. The work is a self-contained library that simulates an address-translation pipeline against a synthetic memory-access workload, exposed via HTTP routes. Same pattern as the M13 scheduler work.

## Decision

We will add a `MiniPager` library plus a series of lesson slices, one per major concept, scoped small enough to land as a single commit per slice.

| ID | Name | OSEP chapter(s) | Lesson slices | Status |
|---|---|---|---|---|
| **M14 (14.1)** | Linear page table + VA→PA translation + page fault | Ch. 18 | 1 slice (PTE struct, PageTable, page-fault trace) | Future |
| **M14 (14.2)** | TLB + TLB miss handling + ASID | Ch. 19 | 1 slice (TLB cache, miss → page-table walk, hit rate) | Future |
| **M14 (14.3)** | Multi-level page table (2-level, 4KB pages) | Ch. 20 | 1 slice (PD / PT split, large pages, sparse address space) | Future |
| **M14 (14.4)** | Page replacement policy (LRU / Clock) | Ch. 21 | 1 slice (frame allocator with eviction policy) | Future |
| **M14 (14.5)** | Complete VM (swap in/out + access-trace replay) | Ch. 22, 23 | 1 slice (backing store + full swap simulation) | Future |
| **M14 (14.6)** | HTTP integration: workload DSL + `/pager/run` route | n/a | 1 slice (JSON / textual workload, replayable traces) | Future |

Ordering rationale:

- **M14.1 (linear page table)** is the foundation of every paging system. It implements the basic VA→PA translation and the page-fault abstraction. Small enough to land as one slice.
- **M14.2 (TLB)** layers a small hardware cache on top of M14.1; demonstrates the 99% hit-rate property OSEP §19 motivates.
- **M14.3 (multi-level)** shows how to handle 64-bit address spaces without huge linear page tables. The 2-level split (PD + PT) is enough to demonstrate the concept.
- **M14.4 (replacement)** turns the simulator from "infinite physical memory" to "limited physical memory + eviction". LRU is the textbook default; Clock is the practical alternative.
- **M14.5 (swap + complete VM)** wires everything together with a backing store (we can reuse Mini FS from M12 to keep dependencies tight).
- **M14.6 (HTTP integration)** mirrors how M13.4 polished M13 — adds the `/pager/run` route that runs a workload and returns the trace.

These milestones do **not** modify the actual MiniWebServer memory layout. The `MiniPager` is a pure simulation, exactly like the `MiniScheduler`. The `MiniFs` image (M12) is reused only as a backing store for swap, in M14.5.

## Architecture

```
src/MiniWebServer.Host/MiniPager/
  VirtualAddress.cs   // VA struct: decompose into VPN + offset, page size constants
  PhysicalMemory.cs   // byte[] of size NUM_FRAMES * PAGE_SIZE; Read/Write byte APIs
  Pte.cs              // page-table entry: valid, frame, present, dirty, accessed bits
  PageTable.cs        // linear (and later, 2-level) page table
  Tlb.cs              // TLB cache with ASID (slice 14.2)
  FrameAllocator.cs   // NUM_FRAMES physical frames + replacement policy (slice 14.4)
  SwapSpace.cs        // backing store on MiniFs (slice 14.5)
  Pager.cs            // orchestrates translation: TLB → page table → frame alloc → swap
  Workload.cs         // memory-access workload DSL (process id, VA, access type)
  Trace.cs            // per-access trace event (hit / miss / fault / evict / swap-in)
```

The HTTP integration (M14.6) lives in `Program.cs` as `/pager/run?workload=...&pages=N&frames=M`, returning the trace as plain text.

The smoke tests live in `.gitnexus/smoke-m14-*.ps1`, one per slice.

## OSTEP Coverage Map (after M14 lands)

| Chapter | Concept | Slice |
|---|---|---|
| Ch. 14 | Address spaces | implicit (the simulator is the address space) |
| Ch. 15 | Base + bound | not covered (superseded by paging) |
| Ch. 16 | Segmentation | not covered |
| Ch. 18 | Paging (linear) | 14.1 |
| Ch. 19 | TLB | 14.2 |
| Ch. 20 | Advanced page tables | 14.3 |
| Ch. 21 | Replacement policy | 14.4 |
| Ch. 22-23 | Complete VM | 14.5 |

Ch. 15 (base+bound) and Ch. 16 (segmentation) are out of scope; modern systems use paging directly. Ch. 17 (free-space management) is implicitly covered by the journal (M12).

## Non-goals

- **Real MMU manipulation**: this is a user-space simulator. We are not modifying page tables from inside a real OS process.
- **Hardware TLB simulation**: the TLB is a simple dictionary keyed by (ASID, VPN).
- **Segmentation**: skipped; the goal is to walk through paging only.
- **NUMA / huge pages / memory-mapped files**: out of scope.

## Consequences

Good:

- Closes OSEP Part II (Ch. 14-23) for the lab. The repo becomes a complete walkthrough of OSEP Parts I (intro + virtualization), II (concurrency), III (persistence), and Part IV (memory).
- Each slice is small (~30-60 min) and self-contained, so partial progress is still useful.
- The pager simulator can later be wired into the scheduler simulator to model "process + CPU + memory" end-to-end.

Tradeoffs:

- A simulated MMU is not the same as a real one. Students may mistake the simulation for hardware behavior.
- The simulator is detached from the actual MiniWebServer memory. Linking them would require unsafe pointer manipulation in `Program.cs`, which is out of scope.

## Verification

- A new slice doc under `docs/learning/` is the unit of work. Each is independently testable per `lesson-slices.md`.
- `dotnet build MiniWebServer.sln` and the existing test project remain the green bar.
- Each slice adds its own smoke (PowerShell) and learning note.
- The roadmap is complete when the Status column here is all Done.

## Next Steps

1. Land M14.1 (linear page table) first. This ADR is committed alongside slice 14.1's code so the roadmap and the first slice land together.
2. After linear paging, TLB (M14.2) is the obvious next step (it depends on M14.1).
3. Multi-level page tables (M14.3) demonstrates the sparse-address-space argument.
4. Replacement (M14.4) is the largest of the early slices and ties M14 together.
5. Swap + complete VM (M14.5) and HTTP integration (M14.6) land at the end.
