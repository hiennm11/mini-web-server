# Milestone 16: Translation Lookaside Buffer (TLB)

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How do we avoid the extra memory reference per instruction fetch + data access that pure paging requires (OSEP §18.4)?

## Scope

Add a small hardware-cache-like structure (the TLB) that caches recent virtual-to-physical translations. Translate() consults the TLB before the page table; on a TLB hit we skip the page-table walk entirely. On a TLB miss we walk the page table and TLB_Insert() the result.

## Slice

- **[s1-tlb.md](./s1-tlb.md)** — `Tlb` class + `Pager.Tlb` field + new `array` workload that demonstrates TLB hit rate.

## OSTEP coverage

- **Ch. 19 Translation Lookaside Buffers** — the entire chapter.
  - §19.1 TLB Basic Algorithm — Figure 19.1: lookup → hit/miss → page-table walk → TLB_Insert.
  - §19.2 Example: Accessing An Array — 10 array elements, 70% hit rate with 16-byte pages.
  - §19.3 Who Handles The TLB Miss? — Hardware-managed (x86) vs software-managed (MIPS R4000). We implement the software-managed variant.
  - §19.4 TLB Contents — VPN, PFN, valid bit, protection bits, ASID, dirty bit.
  - §19.5 Issue: Context Switches — flush on context switch; ASID field for shared TLB.
  - §19.6 Issue: Replacement Policy — LRU vs Random. We default to Random (MIPS choice).
  - §19.7 A Real TLB Entry — MIPS R4000 example.

## OSEP §-specific deviations

- OSEP §19.5 ASID is implemented in `TlbEntry.Asid` (int). When `Pager.Translate()` does `Tlb.Lookup(pid, vpn, ...)`, we pass `pid` as the ASID. This prevents process A's cached translations from being used by process B after a context switch.
- We don't implement explicit `Flush()` calls on context switch (OSEP §19.5 mentions flushing). Two reasons: (a) the simulator doesn't have a context switch concept, and (b) ASID-based matching makes flush unnecessary in this design.
- OSEP §19.6 mentions LRU. Our default replacement is Random (simpler; matches MIPS). An `LruReplacement` variant is straightforward to add.
- We don't model protection-bit enforcement (OSEP §19.1 line 4 checks `CanAccess(ProtectBits)`). Our PTE doesn't have protection bits yet.
- We don't model the TLB control-flow that raises an exception on miss (OSEP §19.3 figure 19.3). Our simulator just inlines the miss handling.

## Key OSEP quotes

> "Thus, paging logically requires an extra memory reference per instruction fetch or data access." (OSEP §18.4)

> "The TLB, like all caches, is built on the premise that in the common case, translations are found in the cache (i.e., are hits)." (OSEP §19.1)

> "When you want to make things fast, the OS usually needs some help." (OSEP §19.1)

## .NET mechanism

- `TlbEntry` is a `struct` (zero allocation when stored in the array).
- `Tlb._entries` is `TlbEntry[]`. Lookup is O(N) where N = TLB capacity. A real TLB is hardware-associative (parallel lookup); we don't model that.
- `Random` replacement uses `System.Random`. Real TLB uses hardware RNG.

## Files

- `src/MiniWebServer.Host/MiniPager/Tlb.cs` — new file.
- `src/MiniWebServer.Host/MiniPager/Pager.cs` — added `Tlb` field + lookup/insert in `Translate()`.
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` — added `ArrayAccessOsep()` + `PagerRunner` TLB parameter.
- `src/MiniWebServer.Host/Program.cs` — `/pager/run?tlb=N` query parameter.

## What this slice does NOT do

- Doesn't add LRU replacement (only Random).
- Doesn't add ASID-based TLB flush semantics on context switch.
- Doesn't add a separate TLB miss exception handler (OSEP §19.3 software-managed path).
- Doesn't model hardware-associative lookup (parallel search).

## Where this leads

- M17 multi-level page tables (Ch. 20) — page table itself can be huge; multi-level fixes that.
- M18 replacement policy (Ch. 21/22) — when physical memory is full, which frame to evict?
