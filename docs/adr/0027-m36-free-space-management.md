# ADR 0027: M36 Free-Space Management as a New Milestone, and What It Retires (OSEP Ch. 17)

## Status

Accepted

## Date

2026-10-08

## Context

ADR 0025 listed Ch. 17 as M36 and recorded that two chapters had been *misrecorded* rather than merely omitted. This records how M36 was built, and confirms the second of those corrections.

### The false claim this milestone retires

`CONTEXT.md` and ADR 0007 both stated that Ch. 17 was "implicitly covered by the journal (M12)". That was wrong. M12's `MiniFs` has a bitmap (`TestBit` / `SetBit` / `ClearBit`) and M21's FFS has `HasFreeBlock()`; both record *what is free*. Neither chooses a block — `MiniFs.AllocateDataBlock` scans for the first clear bit and returns it. That is **first fit**, arrived at by default rather than by decision.

The distinction is the chapter's own. §17.2's data structures (stack, linked list, bitmap) are about *recording* free space; §17.3's strategies are about *choosing* from it. The repo had the first half and no trace of the second, and the coverage list did not know that.

### New milestone, or an extension of M12

New milestone, on ADR 0025's criterion — the unit of verification. An extension asserts on a primitive that already exists; M36's assertions are about policies that did not exist in any form.

The alternative was "add a policy enum to M12 and call it Ch. 17". That would have been smaller, and wrong in a specific way: `MiniFs` allocates *disk blocks* for a filesystem, where every block is the same size and a fragment cannot exist. External fragmentation is a property of **variable-sized** allocation, and §17.1 says so in its first paragraph: "Managing free space can certainly be easy ... It is easy when the space you are managing is divided into fixed-sized units". Putting the policies next to the bitmap would have implied that M12 had a fragmentation problem it cannot have.

## Decision

Two simulators, because they answer different questions.

**`HeapAllocator`** — §17.2-§17.3. Splitting, coalescing, the allocation header, and the four fit policies over a free list of variable-sized extents.

**`BuddyAllocator`** — §17.4. Power-of-two blocks, the XOR buddy relation, and recursive coalescing.

### Two headers, not one

§17.2 uses "header" for two different structures, and the arithmetic only works with both present:

- the **node header** — the free list's first entry (`size` + `next`), which makes a 4096-byte heap start with **4088** free;
- the **allocation header** — stored before each handed-out chunk, which makes a 100-byte request consume **108**.

An early version had only the second and produced 3988 where the chapter prints 3980. Both are now constructor parameters, and `split` passes both.

### Offsets, not pointers

The chapter draws its heaps as `addr:20 len:10`. An integer offset is the same number without the ceremony, and it makes every literal diagram in the chapter directly assertable — which is the only reason these tests are worth having.

### The free list is kept address-ordered

§17.3: "by keeping the list ordered by the address of the free space, coalescing becomes easier, and fragmentation tends to be reduced". Every resulting list the chapter prints is in address order too. Coalescing is then a single pass merging adjacent entries, with no search for neighbours.

### A factory for the chapter's starting state

`FixedExtentHeap` exists because §17.3's example starts from "a free list with three elements on it, of sizes 10, 30, and 20" — a *given* list. First-fit over a fresh 60-byte heap hands out extents in address order and cannot produce that state by allocating, so it is built directly. Without it the chapter's own worked example would be untestable.

## Consequences

### Positive

- **Ch. 17 §17.2-§17.4 is runnable**, and every figure the chapter prints is reproduced: 4088, 3980, 3764, the three-chunk trap and its coalesced repair, the literal `10 30 5` and `10 15 20` lists, the 7 KB → 8 KB split with 1024 bytes of internal fragmentation, and the round trip back to one 64 KB extent.
- **The false coverage claim is retired**, and `MiniFs`'s first-fit-by-default becomes a documented decision rather than an unexamined default. A reader who wants a different policy for M12 now knows it is a policy, not a scan.
- **External fragmentation is a number, not a word.** `FreeBytes > LargestFreeChunk` is §17.1's "the total amount of free space exceeds the size of the request" as an assertion.

### Negative

- **Two allocators where one would do.** They share no code beyond the concept; the buddy tree's level bookkeeping is genuinely different from a linear free list. Merging them would couple two mechanisms the chapter separates on different pages.
- **`FixedExtentHeap` is test scaffolding that ships.** It is a factory on a public type and reachable from the route. Its only purpose is to build a starting state the chapter states rather than one an allocator produces, which is a legitimate reason, but it is not something a real allocator offers.
- **The buddy tree stores block addresses in `List<int>` per level**, so a split is a linear scan to find the buddy. §17.4's selling point is that the buddy is found by flipping one bit — which is true of the *computation*, and this implementation does compute it that way, but the free list is still searched. A production buddy allocator keeps the free list ordered and indexed by block number.
- **Internal fragmentation is reported but not minimized.** The buddy allocator rounds every request up to a power of two, so a 100-byte request wastes 28 bytes. A real allocator would round to a size class instead.

### Neutral

- Five bugs, all found before the suite was green: the buddy merge checked the wrong level, the merge decremented the level count by 1 instead of 2, the free list was not address-ordered after a split, the route parsed its own scenario names as integers, and `split` omitted the node header.
- **One thing that looked like a bug was not.** `_allocated[start] = need` was rewritten to `1 << level` while chasing a suspected block-size bug. They are equal by construction — `level = Log2(need)` — and the mutation check proved it: the suite stayed green. The rewrite was reverted and the comment claiming a regression was deleted rather than kept, because a test that cannot fail is worse than no test.
- The structural invariants were checked with a throwaway sweep (3100 random allocate/free operations across both allocators, verifying that free + allocated equals heap size, that free blocks never overlap, that every block is a power of two aligned to its own size, and that coalescing leaves no adjacent extents). That sweep is not committed; the committed tests assert the chapter's figures and the invariants they imply.

## Verification

- 8 tests, all asserting figures printed in Ch. 17 rather than recomputing them:
  - §17.2's 4088 → 3980 → 3764 sequence, the 108-byte charge, and the two extents left by freeing the middle chunk
  - §17.2's three-chunk trap, the 20-byte request that fails with 20 free, and the coalesced heap that serves it
  - §17.3's literal `10 30 5` / `10 15 20` lists for best, worst and first fit, plus a second workload where first fit's early exit and best fit's exhaustive search give different extents
  - §17.4's 7 KB → 8 KB split, the 1024-byte internal fragmentation, the XOR buddy, and the round trip
  - a multi-level split (100 bytes in a 64 KB heap walks nine levels; 65536 = 32768 + … + 128 + 128) and its round trip
  - requests the tree cannot represent: larger than the heap, one byte too big, and `int.MaxValue`
  - §17.1's opening example as a number: 20 free, largest extent 10, a 15-byte request refused
  - inputs the chapter's model cannot express: a non-power-of-two buddy heap, a zero-size heap or request, and a double free
- Mutation-checked: placing the last split sibling at the wrong address makes the buddy test fail ("Expected 8192, got 16384").
- Structural invariants swept over 3100 random operations with a throwaway harness — all hold.
- Smoke: all four scenarios return 200; unknown scenario, unknown policy, non-integer and non-positive parameters, and a non-power-of-two buddy heap return 400. `split` reproduces 4088 → 3980; `buddy?request=7168` reports the 1024-byte internal fragmentation and the 57344-byte remainder.
- 116/116 tests pass (108 before M36 + 8).

> **Scope claim corrected by ADR 0029.** This milestone covers Ch. 17 in full (§17.1-§17.4), so the chapter-level claim was already accurate. The glossary entry that described it as "the gap" was not — `CONTEXT.md` had not been updated when this landed.

## Source Documents

- `docs/learning/m36-free-space/overview.md` — scope and what is deliberately excluded.
- `docs/learning/m36-free-space/s1-split-coalesce-fit-buddy.md` — the chapter's figures, measured tables, and the five bugs.
- `docs/adr/0025-remaining-coverage-gaps-m35-m36-m37.md` — why this is a new milestone.
- `docs/adr/0007-mini-pager-mmu-tlb-multilevel-replacement.md` — the Ch. 17 claim this ADR retires.
- OSTEP Ch. 17 §17.1, §17.2, §17.3, §17.4.