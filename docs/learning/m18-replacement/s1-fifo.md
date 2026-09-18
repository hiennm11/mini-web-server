# Slice 18.1: Replacement Policy (FIFO / Random / LRU)

## What it does

Adds swap space (`Swap` class) and three replacement policies (`FifoEviction`, `RandomEviction`, `LruEviction`) to the `Pager`. When all physical frames are in use and a new page needs to be mapped, the policy picks a victim frame, the page contents are written to swap, and the new page takes its place. When an evicted page is accessed again, its contents are loaded back from swap.

The HTTP route `/pager/run?workload=exceed&policy=fifo|lru|random&frames=N` triggers eviction by accessing more pages than fit in physical memory.

## Files added/changed

- `src/MiniWebServer.Host/MiniPager/Swap.cs` (new, ~70 lines):
  - `SWAP_SLOTS = 256` slots, each `SLOT_SIZE = 4096` bytes.
  - `WriteOut(frame)` returns the slot index where the data was stored.
  - `ReadIn(slot, frame)` + `Free(slot)`.
- `src/MiniWebServer.Host/MiniPager/EvictionPolicy.cs` (new, ~130 lines):
  - `FrameInfo` record (frameNo, ownerPid, vpn, lastUsedTick, insertedTick, allocated).
  - `IEvictionPolicy` interface (`OnAllocate`, `PickVictim`, `OnAccess`, `Name`).
  - `FifoEviction` (slice 18.1 baseline).
  - `RandomEviction` (MIPS R4000 style).
  - `LruEviction` (timestamp-based, no doubly-linked list).
- `src/MiniWebServer.Host/MiniPager/Pte.cs` — added `SwappablePte` struct (Valid, InSwap, FrameNo, SwapSlot, Dirty, Referenced).
- `src/MiniWebServer.Host/MiniPager/Pager.cs`:
  - `IPageTableLookup.TryLookup` returns `LookupResult` (Hit / InSwap / Miss).
  - `LookupResult.Miss` triggers `Map(pid, vpn, frameNo=-1)` — auto-allocates frame or evicts.
  - `LookupResult.InSwap` reads from swap + restores via `pt.RestoreFromSwap`.
  - `Map(vpn=-1)` checks `pt.TryLookup` first — if VPN is in swap, reads from swap instead of zeroing.
  - `EvictFrame(frame)` writes to swap, marks PTE as in-swap, frees frame, flushes TLB.
  - `FrameInfo[] _frames` tracks per-frame state for eviction policies.
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` — `ExceedsMemory()` workload.
- `src/MiniWebServer.Host/MiniPager/PhysicalMemory.cs` — `ReadBytes` + `WriteBytes`.
- `src/MiniWebServer.Host/Program.cs` — `?workload=exceed&policy=...` parameters.

## OSEP alignment

Implements OSEP §21.1 (swap space), §21.4 (page-fault control flow), §22.3 (FIFO), §22.4 (Random), §22.5/§22.7 (LRU).

## Smoke evidence

```
$ curl 'http://localhost:8080/pager/run?workload=exceed&frames=4&policy=fifo'
=== Pager run: 4 frames (16384 bytes physical), TLB=disabled, PT mode=linear, eviction=FIFO
step=  1 ... first-touch frame=0
step=  5 ... first-touch frame=0   (page 4 evicts page 0)
step=  6 ... first-touch frame=1   (page 5 evicts page 1)
step= 10 ... first-touch frame=1
step= 11 ... swap-in frame=2        (page 0 comes back, evicts page 2)
...
=== Replacement stats: evictions=7 swap_ins=7
=== Swap usage: 0 of 256 slots    (all swap-ins completed)
```

All 30 accesses Hit, no PageFaults. The 10 pages cycle through 4 frames with 7 evictions + 7 swap-ins.

## OSEP concept

> "When a fault occurs, simply evict the page that has been in memory the longest, regardless of how often it has been used." (OSEP §22.3 — FIFO)

> "To track which pages have been least-recently used, the system must record, for each page, the time of its last reference." (OSEP §22.7 — LRU)

## .NET mechanism

- `Swap` is `byte[256 * 4096]` + `bool[256]`.
- `IEvictionPolicy.PickVictim(FrameInfo[])` returns the victim frame index.
- `Random` uses `System.Random`.

## What this slice does NOT do

- `TwoLevelLookup.InSwap` (deferred).
- Write-back of dirty pages.
- Belady's MIN.
- Clock / Second-chance approximation.
- Page-out daemon.

## Deferred (next slice candidates)

- Add `InSwap` support to `TwoLevelLookup`.
- M19 complete VM systems (Ch. 23).
