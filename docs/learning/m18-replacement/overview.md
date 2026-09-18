# Milestone 18: Replacement Policy

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

When physical memory is full and we need to bring a new page in, which existing page do we evict? How do we bring evicted pages back when they're needed again?

## Scope

Add (a) a swap device — an on-disk region where evicted pages live — and (b) a replacement policy that picks the victim frame. Three policies: FIFO, Random, LRU. The workload `exceed` accesses more pages than fit in physical memory, triggering evictions + swap-ins.

## Slice

- **[s1-fifo.md](./s1-fifo.md)** — first replacement policy (FIFO), simplest case.

## OSTEP coverage

- **Ch. 21 Swapping: Mechanisms**
  - §21.1 Swap Space — fixed-size disk region for evicted pages.
  - §21.2 The Present Bit — PTE Valid / InSwap / Miss distinction.
  - §21.4 Page-Fault Control Flow — Figure 21.2: software OS handler steps 1-9.
  - §21.6 Behind the Scenes: Full VMM — overall flow.
- **Ch. 22 Swapping: Policies**
  - §22.1 Cache Management — frame allocation as a cache-management problem.
  - §22.2 Optimal (Belady's MIN) — theoretical lower bound.
  - §22.3 FIFO — evict oldest.
  - §22.4 Random — evict a random page.
  - §22.5 Using History: LRU — evict least-recently used.
  - §22.6 Workload Examples — the `exceed` workload cycles more pages than memory holds.
  - §22.7 Implementing LRU — list-based or timestamp-based.
  - §22.8 Approximating LRU — use bit + periodic clear (not implemented).

## OSEP §-specific deviations

- OSEP §21.4 step 8 ("issue swap read"): we do this in `Pager.Translate` on `LookupResult.InSwap` (instead of via an explicit page-fault handler). The simulator handles the entire access in one call.
- OSEP §22.5 "implementation" — we use timestamps (`LastUsedTick`) instead of a doubly-linked list. Same semantics, simpler code.
- We implement `LinearLookup` with the `SwappablePte` extension; `TwoLevelLookup` does NOT yet support `InSwap` (its PTEs are just `Valid` bool + frame). Workaround: use linear for swap demos. Adding `InSwap` to `PageTableEntry` is straightforward.
- We don't model "write-back" of dirty pages (OSEP §22.9) — every eviction writes the frame to swap regardless of the `Dirty` bit.

## Key OSEP quotes

> "Thus, we will reserve some space on the disk for swapping. When memory pressure arises, the OS will evict some pages from memory to that swap space." (OSEP §21.1)

> "When a fault occurs, simply evict the page that has been in memory the longest, regardless of how often it has been used." (OSEP §22.3 — FIFO)

> "To track which pages have been least-recently used, the system must record, for each page, the time of its last reference." (OSEP §22.7 — LRU)

## .NET mechanism

- `Swap` is `byte[256 * 4096]` + `bool[256]`. Each `WriteOut()` finds the first free slot.
- `IEvictionPolicy` interface — `OnAllocate`, `PickVictim`, `OnAccess`. Each frame has a `FrameInfo` record (ownerPid, vpn, lastUsedTick, insertedTick, allocated).
- `FifoEviction.PickVictim` returns the frame with the smallest `InsertedTick`.
- `LruEviction.PickVictim` returns the frame with the smallest `LastUsedTick` (updated on every access).
- `RandomEviction.PickVictim` returns a random frame (real TLBs use this — MIPS R4000).

## Files

- `src/MiniWebServer.Host/MiniPager/Swap.cs` (new, ~70 lines).
- `src/MiniWebServer.Host/MiniPager/EvictionPolicy.cs` (new, ~130 lines).
- `src/MiniWebServer.Host/MiniPager/Pte.cs` — added `SwappablePte` struct (M18 only — used by `LinearLookup`).
- `src/MiniWebServer.Host/MiniPager/PageDirectory.cs` — added `InnerPageTable` etc.
- `src/MiniWebServer.Host/MiniPager/Pager.cs`:
  - `IPageTableLookup.TryLookup` returns `LookupResult` enum (Hit / InSwap / Miss).
  - `LookupResult.Miss` triggers `Map(pid, vpn, frameNo=-1)` — auto-allocates a frame (or evicts).
  - `LookupResult.InSwap` triggers `Swap.ReadIn` + `pt.RestoreFromSwap`.
  - `Map(vpn=-1)` checks if VPN is `InSwap` (from a previous eviction) and reads from swap; else allocates fresh.
  - `EvictFrame` writes the frame to swap, marks the PTE as `InSwap`, frees the frame, flushes the TLB.
  - `FrameInfo[]` array tracks per-frame state for eviction policies.
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` — `ExceedsMemory()` workload (3 sequential passes over 10 pages, with `numFrames=4` triggers evictions).
- `src/MiniWebServer.Host/MiniPager/PhysicalMemory.cs` — added `ReadBytes` + `WriteBytes` for swap in/out.
- `src/MiniWebServer.Host/Program.cs` — `/pager/run?workload=exceed&policy=fifo|lru|random` parameters.

## What this slice does NOT do

- `TwoLevelLookup.InSwap` support (mentioned in slice doc as deferred).
- Write-back of dirty pages (always write to swap on eviction).
- Belady's MIN policy (theoretical optimal — too expensive to compute online).
- Clock / Second-chance approximation (§22.8).
- Multi-process LRU (per-process working sets).
- Page-out daemon (write-ahead for eviction — we evict synchronously on demand).

## Where this leads

- M19 complete VM systems (Ch. 23) — capstone combining paging + swap + replacement.
- Could be enhanced with `/proc/pagetable_info` debug route.
