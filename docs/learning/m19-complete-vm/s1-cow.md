# Slice 19.1: Copy-on-Write (COW)

## What it does

Implements OSEP §23.1 "Other Neat Tricks" (copy-on-write). When the OS wants to "copy" a page from one address space to another (e.g., during `fork()`), it instead shares the same physical frame and marks both PTEs as ReadOnly. The first write to either PTE triggers a private copy.

The HTTP route `/pager/run?workload=cow` runs a smoke scenario:
1. Process 1 maps VPN=0 to frame 0.
2. Process 1 reads VPN=0 (Hit at frame 0).
3. Process 2 "forks" — its VPN=0 is COW-shared with P1's.
4. Process 2 reads VPN=0 (HitReadOnly — page is shared).
5. Process 1 writes VPN=0 → COW fires: allocate new frame, copy old contents, mark P1's PTE as writable.
6. Process 2 reads VPN=0 again → still on frame 0 (original).

## Files added/changed

- `src/MiniWebServer.Host/MiniPager/Pte.cs` — `SwappablePte.ReadOnly` added (COW marker).
- `src/MiniWebServer.Host/MiniPager/CowPte.cs` (new, ~50 lines) — conceptual `CowPte` type (current implementation uses `SwappablePte.ReadOnly`).
- `src/MiniWebServer.Host/MiniPager/Pager.cs`:
  - `LookupResult.HitReadOnly` enum value (signals caller should COW).
  - `IPageTableLookup.ShareFrame` / `UnshareFrame` / `IsCowShared` interface methods.
  - `LinearLookup` implements them; `TwoLevelLookup` stubs throw `NotSupportedException`.
  - `Pager.ShareFrame(sourcePid, sourceVpn, destPid, destVpn)` — the COW fork helper.
  - `Pager.Write(pid, vpn, data)` — write that triggers COW on a shared PTE.
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` — `RunCow(int numFrames)` smoke that drives the scenario.
- `src/MiniWebServer.Host/Program.cs` — `/pager/run?workload=cow&frames=N` route.

## OSEP alignment

Implements OSEP §23.1 VMS "Other Neat Tricks" (COW) and OSEP §23.2 Linux VM (lazy copy-on-write on fork).

## Smoke evidence

```
$ curl 'http://localhost:8080/pager/run?workload=cow&frames=4'
=== COW fork scenario (M19 / OSEP §23.1) ===
step 1: P1 first-touch map VPN=0 (frame=0)
step 2: P1 reads VPN=0 → Hit
step 3: P2 fork — ShareFrame(P1,0) → (P2,0) COW-shared
step 4: P2 reads VPN=0 → HitReadOnly (page is shared)
step 5: P1 writes VPN=0 → COW fires; P1.frame=1, P2.frame=0
        (frames should differ — P1 got a fresh private frame, P2 keeps the original)
step 6: P2 reads VPN=0 → Hit on original frame
```

The trace confirms the COW lifecycle: P1 + P2 both initially share frame 0; after P1's write, P1 has frame 1 (private copy) and P2 keeps frame 0 (the original).

## OSEP concept

> "when the OS needs to copy a page from one address space to another, instead of copying it, it can map it into the target address space and mark it read-only in both address spaces. If both address spaces only read the page, no further action is taken, and thus the OS has realized a fast copy without actually moving any data. If, however, one of the address spaces does indeed try to write to the page, it will trap into the OS. The OS will then notice that the page is a COW page, and thus (lazily) allocate a new page, fill it with the data, and map this new page into the address space of the faulting process." (OSEP §23.1)

## .NET mechanism

- `SwappablePte.ReadOnly` is the COW marker. Our PTE struct now has a `Dirty` bit (existing) + `Referenced` (existing) + `ReadOnly` (new, M19.1).
- `Pager.Write` does the actual COW logic: allocate a new frame, copy old contents from the shared frame, install a writable PTE for the writing process.

## What this slice does NOT do

- Per-frame reference counting. Linux's actual COW maintains a refcount per physical frame and only frees when the last reference is dropped. Our simulator just splits 1:1 (source keeps original, dest gets private copy).
- COW on `TwoLevelLookup` (throws `NotSupportedException`).
- Demand zeroing optimization (we zero on first-touch Map, but don't model the "marked inaccessible" optimization).

## Deferred (other Ch. 23 features)

- Segmented FIFO + second-chance lists (OSEP §23.1 VMS).
- Resident Set Size (RSS) per process (OSEP §23.1).
- 2Q replacement (OSEP §23.2 Linux).
- Huge pages (OSEP §23.2 Linux).
- NX bit / ASLR / KPTI (OSEP §23.2 security).

These are documented in `overview.md` as part of the M19 scope.
