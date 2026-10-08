# Milestone 36: Free-Space Management (Ch. 17)

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

When a client asks for N bytes, which free chunk should the allocator hand back, and what does that choice cost when the chunks are not all the same size?

## Scope

OSEP Ch. 17 §17.2-§17.4, run in two simulators and exposed at `/heap/run`.

- **Splitting** (§17.2) — a request smaller than a free chunk leaves the remainder on the list.
- **Coalescing** (§17.2) — freeing merges with adjacent free chunks, so a heap can be entirely free without looking entirely free.
- **The header** (§17.2) — the library searches for `N + header`, not `N`, because `free(ptr)` carries no size.
- **Fit policies** (§17.3) — first fit, best fit, worst fit, next fit.
- **Buddy allocation** (§17.4) — power-of-two blocks, coalescing via the XOR buddy address.

## Why this is a milestone and not an extension of M12

`CONTEXT.md` and ADR 0007 both recorded Ch. 17 as "implicitly covered by the journal (M12)". That was wrong, and ADR 0025 said so: M12's bitmap records *what is free*, and its `MiniFs` scans for the first clear bit — **first fit, by default rather than by decision**. A bitmap is §17.2's data structure for *recording* free space; choosing *which* extent to satisfy a request is §17.3, and nothing in the repo made that choice visible or measured.

So M36 is not new surface area so much as making an implicit, accidental decision explicit — and then showing the alternatives, because the chapter's point is that none of them is best:

> "The ideal allocator is both fast and minimizes fragmentation. Unfortunately, because the stream of allocation and free requests can be arbitrary ... any particular strategy can do quite badly given the wrong set of inputs. Thus, we will not describe a 'best' approach."

## OSTEP coverage

- **§17.1 Assumptions** — the `malloc`/`free` interface, where the size is *not* passed to `free`; external fragmentation as the primary concern; internal fragmentation named and set aside; no compaction (memory handed out cannot be relocated); a fixed-size region.
- **§17.2 Low-level mechanisms** — splitting, coalescing, the header block (`size` + `magic`), and the embedded free list whose first node lives inside the free space itself.
- **§17.3 Basic Strategies** — best fit, worst fit, first fit, next fit, and the chapter's worked example on a 10/30/20 free list.
- **§17.4 Buddy allocation** — the 2^N heap, the recursive split, the XOR buddy, and the recursive coalesce back up. Also named and **not** implemented: segregated lists / the slab allocator (Bonwick), and the balanced-tree / splay-tree scaling approaches.

**Not covered**: §17.2 "Growing The Heap" (the `sbrk` path — this repo's heap is fixed by §17.1's last assumption), and §17.4's segregated lists and slab allocator.

## Files

- `src/MiniWebServer.Host/MiniScheduler/HeapAllocator.cs` — `FitPolicy`, `FreeChunk`,
  `HeapAllocator` (split, coalesce, header, four policies).
- `src/MiniWebServer.Host/MiniScheduler/BuddyAllocator.cs` — the §17.4 tree.
- `src/MiniWebServer.Host/Program.cs` — `/heap/run` and its four formatters.

## Implementation notes

**Offsets, not pointers.** The chapter draws its heaps as `addr:20 len:10`, and an integer offset is the same number without the ceremony. It also means the chapter's literal diagrams are directly assertable.

**The free list is address-ordered.** §17.3 notes that "by keeping the list ordered by the address of the free space, coalescing becomes easier, and fragmentation tends to be reduced", and every resulting list the chapter prints is in address order. Coalescing then needs no search for neighbours — merge adjacent entries in one pass.

**Two headers, not one.** §17.2 uses `header` twice, for two different things, and the arithmetic depends on the difference:
- the **node header** (the free list's first entry, `size` + `next`), which makes a 4096-byte heap start with **4088** free;
- the **allocation header** (before each handed-out chunk), which makes a 100-byte request consume **108**.

An earlier version had only the second and produced 3988 where the chapter says 3980. Both are parameters now.

**`FixedExtentHeap` exists because the chapter's §17.3 example is not reachable by allocating.** The chapter starts from "a free list with three elements on it, of sizes 10, 30, and 20" — a *given* list, not one an allocator produces. First-fit over a fresh 60-byte heap hands out extents in address order and cannot produce that starting state, so it is built directly.

## What this slice does NOT do

- **Growing the heap** (§17.2) — no `sbrk`, no `mmap`. §17.1 assumes a fixed region and the chapter calls that a simplification.
- **Compaction** (§17.1) — "no compaction of free space is possible". Relocating live chunks would defeat the `free(ptr)` interface, since the caller holds the pointer.
- **Segregated lists / slab** (§17.4) — named with a pointer to Bonwick [B94]; a real subsystem, not a slice.
- **Balanced trees / splay trees** (§17.4 "Other Ideas") — the chapter's point is that list search does not scale, and the answer is a different data structure. Modelling that means modelling a tree allocator.
- **Multi-threaded allocation** (§17.4 cites Berger [B+00] and Evans [E06]) — this repo's M29/M30 concurrency primitives are about teaching Ch. 30, not about making an allocator safe.
- **The magic number.** §17.2's header has `size` + `magic: 1234567`, and `free` asserts on it "as a sanity check". The simulator tracks allocation sizes in a side table instead, which is the same information without modelling a bit pattern in memory.