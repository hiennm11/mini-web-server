# Milestone 19: Complete VM Systems

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

What features does a real-world VM system add beyond "page table + TLB + replacement policy"? How do they interact?

## Scope

This milestone covers OSEP Ch. 23 ("Complete VM Systems") which examines two real-world systems:

1. **VAX/VMS** (1970s-80s) — early "modern" VM manager. Key innovations:
   - Segmented FIFO page replacement with **second-chance lists** (clean + dirty global lists).
   - **Resident Set Size (RSS)** per process — cap on how many pages each process can keep.
   - **Demand zeroing** — pages added to address space start inaccessible; zero on first access.
   - **Copy-on-write (COW)** — `fork()` shares pages as read-only; first write triggers copy.
   - Page-table placement in kernel VM (so PT pages themselves can be swapped).
2. **Linux VM** (modern) — incremental, performance-driven evolution:
   - 64-bit virtual address space (top 16 bits unused; bottom 12 bits offset; middle 36 bits translated).
   - 4-level page tables (P1/P2/P3/P4 + offset).
   - **Huge pages** (2 MB / 1 GB) for TLB coverage.
   - **Page cache** — unified cache for file data + anonymous (heap/stack) memory.
   - **2Q replacement** — active + inactive lists to avoid LRU cache-pollution.
   - **Copy-on-write fork** — same as VMS.
   - **Security mitigations**: NX bit, ASLR (user + kernel), KPTI (post-Meltdown).

For the simulator, we focus on what's testable + distinctive: **copy-on-write** (slice 19.1). The other features are documented as deferred.

## Slice

- **[s1-cow.md](./s1-cow.md)** — copy-on-write page sharing between two processes; first write triggers a private copy.

## OSEP coverage

- **Ch. 23 Complete Virtual Memory Systems** (entire chapter)
  - §23.1 VAX/VMS Virtual Memory:
    - §23.1 "Page Replacement" — segmented FIFO + second-chance lists.
    - §23.1 "Other Neat Tricks" — demand zeroing + COW.
    - §23.1 "A Real Address Space" — null page, kernel mapped into user space.
  - §23.2 Linux VM:
    - §23.2 "Page Table Structure" — 4-level page tables.
    - §23.2 "Large Page Support" — huge pages (2 MB / 1 GB).
    - §23.2 "The Page Cache" — unified cache + 2Q replacement.
    - §23.2 "Security And Buffer Overflows" — NX bit, ASLR, KPTI.

## OSEP §-specific deviations

We implement only **COW** (M19.1) in this slice. The other Ch. 23 features are documented as deferred:

- **Demand zeroing** (OSEP §23.1): our simulator already does this — `Map(pid, vpn, frameNo=-1)` zeroes the frame on first-touch. We don't model the "marked inaccessible until accessed" optimization.
- **Resident Set Size per process** (OSEP §23.1 VMS): not implemented. Our replacement policy is global (OSEP §22.5 notes this is LRU's weakness — "memory hogs").
- **2Q replacement** (OSEP §23.2 Linux): not implemented. Our eviction policies (FIFO, Random, LRU) are simple; 2Q adds active/inactive lists to handle cyclic-large-file access patterns.
- **Huge pages** (OSEP §23.2): not implemented. Our page size is fixed at 4 KB.
- **4-level page tables** (OSEP §23.2): our `TwoLevelLookup` is 2-level (matches OSEP §20); 4-level would require more code.
- **NX bit, ASLR, KPTI** (OSEP §23.2 security): not applicable to a simulator.
- **TwoLevelLookup.InSwap + COW**: COW only works with `LinearLookup`. `TwoLevelLookup` throws `NotSupportedException` if you try to share a frame.

## Key OSEP quotes

> "Another cool optimization found in VMS (and again, in virtually every modern OS) is **copy-on-write** (COW for short). The idea, which goes at least back to the TENEX operating system [BB+72], is simple: when the OS needs to copy a page from one address space to another, instead of copying it, it can map it into the target address space and mark it read-only in both address spaces. If both address spaces only read the page, no further action is taken, and thus the OS has realized a fast copy without actually moving any data. If, however, one of the address spaces does indeed try to write to the page, it will trap into the OS. The OS will then notice that the page is a COW page, and thus (lazily) allocate a new page, fill it with the data, and map this new page into the address space of the faulting process." (OSEP §23.1 VMS)

> "Linux performs lazy copy-on-write copying of pages upon fork(), thus lowering overheads by avoiding unnecessary copying." (OSEP §23.2 Linux VM)

## .NET mechanism

- `SwappablePte.ReadOnly` flag (slice 19.1) marks a PTE as COW-shared.
- `IPageTableLookup.ShareFrame(vpn, frameNo)` marks a VPN as sharing an existing frame.
- `Pager.ShareFrame(sourcePid, sourceVpn, destPid, destVpn)` — the COW fork helper.
- `Pager.Write(pid, vpn, data)` — performs a write. On a shared PTE, allocates a new frame, copies old contents, updates this PTE to point to the new frame (writable).

## Files

- `src/MiniWebServer.Host/MiniPager/Pte.cs` — `SwappablePte.ReadOnly` added.
- `src/MiniWebServer.Host/MiniPager/CowPte.cs` (new, ~50 lines) — `CowPte` (the conceptual type — current LinearLookup still uses `SwappablePte` for simplicity).
- `src/MiniWebServer.Host/MiniPager/Pager.cs`:
  - `LookupResult.HitReadOnly` added.
  - `IPageTableLookup.ShareFrame` / `UnshareFrame` / `IsCowShared` methods added.
  - `LinearLookup` extended with `ShareFrame` etc. (`TwoLevelLookup` stubs throw `NotSupportedException`).
  - `Pager.ShareFrame(sourcePid, sourceVpn, destPid, destVpn)` — the COW fork helper.
  - `Pager.Write(pid, vpn, data)` — write that triggers COW.
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` — `RunCow(int numFrames)` smoke that drives the COW scenario.
- `src/MiniWebServer.Host/Program.cs` — `/pager/run?workload=cow&frames=N` route.

## What this slice does NOT do

- Demand zeroing as an explicit "marked inaccessible" optimization (we always zero on first-touch Map).
- Per-process RSS limits.
- 2Q replacement.
- Huge pages.
- COW on `TwoLevelLookup`.
- NX bit / ASLR / KPTI.

## Where this leads

- The COW + RSS combination is what makes `fork()` cheap on Linux (entire address space is shared as RO + COW; only writes pay for copies).
- Real COW implementation also needs a per-frame reference count (to detect "last reference" and free the frame). Our implementation just splits 1:1 — the source keeps the original frame, the destination gets a new one. Documented as deferred.
- Could be combined with M18 replacement policy: a COW copy creates a new frame, which counts toward the process's frame usage and might trigger eviction if memory is tight.

## Next milestone

The roadmap continues with M13.2 (Stride / Lottery scheduling, Ch. 9) and M13.3 (Multi-CPU, Ch. 10). After that, M20 (Dining Philosophers, Ch. 31.6), M22 (Lock-free, Ch. 29), then back to FS-related work.
