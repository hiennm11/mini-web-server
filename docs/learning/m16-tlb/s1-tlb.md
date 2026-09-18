# Slice 16.1: Translation Lookaside Buffer (TLB)

## What it does

Adds a `Tlb` cache in front of the page-table walk in `Pager.Translate()`. On a TLB hit, we skip the page-table walk entirely (OSEP §19.1 line 6). On a miss, we walk the page table and `Tlb.Insert()` the result (line 18).

The HTTP route `/pager/run?workload=array&tlb=N` enables the TLB with capacity N. The trace shows each access as `tlb-hit frame=X` or `pt-walk frame=X` so you can see which path was taken.

## Files added/changed

- `src/MiniWebServer.Host/MiniPager/Tlb.cs` (new, ~120 lines): `TlbEntry` struct + `Tlb` class with `Lookup`, `Insert`, `Flush`, `ResetStats`, `HitRate`, `Stats`.
- `src/MiniWebServer.Host/MiniPager/Pager.cs`: added `Tlb? Tlb { get; set; }` field + TLB lookup/insert branches in `Translate()`.
- `src/MiniWebServer.Host/MiniPager/Workloads.cs`: added `ArrayAccessOsep()` workload + `PagerRunner` TLB parameter.
- `src/MiniWebServer.Host/Program.cs`: `/pager/run?tlb=N` query parameter + `workload=array` option.

## OSEP alignment

Implements OSEP §19.1 (TLB Basic Algorithm), §19.2 (Array Example), §19.4 (TLB Contents), §19.5 (Context Switches + ASID), §19.6 (Random Replacement).

## Smoke evidence

```
$ curl "http://localhost:8080/pager/run?workload=array&frames=16&tlb=4"
=== stats: accesses=17 hits=7 faults=10 outofrange=0 trace_events=17
=== TLB stats: hits=3 misses=14 evictions=0 hit_rate=0.176
```

The trace shows step 13 as `tlb-hit frame=1` — same VPN=1 accessed in step 2 + step 11, hitting the TLB on the third visit. The non-re-visited pages (VPN=4..9) don't show TLB hits because their access ends in PageFault before `Tlb.Insert()` is called (our `Insert` is gated on a successful page-table walk).

## OSEP concept

Implements OSEP §19.1 "TLB Basic Algorithm" (Figure 19.1) verbatim:

```
1  VPN = (VirtualAddress & VPN_MASK) >> SHIFT
2  (Success, TlbEntry) = TLB_Lookup(VPN)
3  if (Success == True)                    // TLB Hit
4    if (CanAccess(TlbEntry.ProtectBits))
5      Offset = VirtualAddress & OFFSET_MASK
6      PhysAddr = (TlbEntry.PFN << SHIFT) | Offset
7      Register = AccessMemory(PhysAddr)
8  else                                    // TLB Miss
9    PTEAddr = PTBR + (VPN * sizeof(PTE))
10   PTE = AccessMemory(PTEAddr)
11   if (PTE.Valid == False) RaiseException(SEGMENTATION_FAULT)
12   ...
13   TLB_Insert(VPN, PTE.PFN, PTE.ProtectBits)
14   RetryInstruction()
```

Our `Pager.Translate()` implements lines 2-13 (no RETRY — the simulator handles the entire access in one call).

## .NET mechanism

- `TlbEntry` is a `struct` (zero allocation).
- Lookup iterates `_entries` linearly. A real TLB is hardware-associative with parallel search; we don't model that.
- `Random` replacement picks a random slot when the TLB is full.

## What this slice does NOT do

- LRU replacement (only Random).
- ASID-based TLB flush on context switch.
- Protection-bit enforcement (PTE doesn't have Protection yet).
- Multi-level TLB (L1/L2).

## Deferred (next slice candidates)

- M17 multi-level page tables (Ch. 20).
- M18 replacement policy (Ch. 21/22).
