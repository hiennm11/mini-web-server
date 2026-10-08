# Slice 36.1: Splitting, Coalescing, Fit Policies, Buddy Allocation

> **What it does** — runs OSTEP Ch. 17's mechanisms over two simulators and exposes all four at
> `/heap/run?scenario=split|coalesce|strategies|buddy`.

## Route

```
/heap/run?scenario=split|coalesce|strategies|buddy
           &size=N&request=N&header=N&policy=first|best|worst|next&coalesce=0|1
```

Unknown scenario, unknown policy, a non-integer numeric parameter, a non-positive size or
request, and a non-power-of-two buddy heap all answer 400.

## The chapter's own numbers

Every expectation below is a figure OSTEP prints. A test that recomputed the metric the way
the code does would pass by construction.

| § | What | Chapter says | Simulator |
|---|---|---|---|
| §17.2 | 4096-byte heap, 8-byte node header | "a single entry, of size 4088" | 4088 |
| §17.2 | `malloc(100)` with an 8-byte header | "the library allocated 108 bytes ... shrinks the one free node ... to 3980 bytes (4088 minus 108)" | 3980 |
| §17.2 | Three 100-byte allocations | "now only 3764 bytes in size" | 3764 |
| §17.2 | Freeing the middle chunk | two extents, 108 and the 3764 tail — "the free space is fragmented" | 108 + 3764 |
| §17.2 | Same heap, all freed, no coalescing | "the entire heap is now free, it is seemingly divided into three chunks of 10 bytes each" | 3 extents, largest 10 |
| §17.2 | Same heap with coalescing | "our final list should look like this: head addr:0 len:30" | 1 extent of 30 |
| §17.3 | Best fit on a 10/30/20 list, request 15 | "The resulting free list: head 10 30 5" | 10, 30, 5 |
| §17.3 | Worst fit, same list | "The resulting list: head 10 15 20" | 10, 15, 20 |
| §17.3 | First fit, same list | "does the same thing as worst-fit" | 10, 15, 20 |
| §17.4 | 64 KB heap, 7 KB request | "the leftmost 8 KB block is allocated and returned to the user" | block 8192 at offset 0 |
| §17.4 | The same request's waste | "this scheme can suffer from internal fragmentation" | 1024 bytes |
| §17.4 | Freeing both halves | "restoring the entire free space" | 1 block of 65536 |

## Measured

`scenario=strategies&request=15`, on the chapter's 10/30/20 free list:

```
policy | chose offset | resulting extents        | wasted in splinters
best   |           40 | 10 30 5                  | 15
worst  |           10 | 10 15 20                 | 10
first  |           10 | 10 15 20                 | 10
next   |           10 | 10 15 20                 | 10
```

Best fit is the only policy that picks the 20-byte extent, and it leaves the *most* waste —
15 bytes in a 5-byte splinter plus the untouched 10. §17.3 predicts exactly this trade:
best fit "tries to reduce wasted space" but "naive implementations pay a heavy performance
penalty when performing an exhaustive search". On this workload it loses on both counts.

The splinter column is §17.3's other warning made numeric: first fit "sometimes pollutes
the beginning of the free list with small objects". Here all three policies leave the same
10-byte head untouched, which is the *good* case; the pollution shows up once the head is
satisfied early, which the fourth test covers.

`scenario=buddy&request=7168`:

```
allocate(7168) -> offset 0, block 8192 bytes
  internal fragmentation: 1024 bytes wasted inside the block
free blocks: 8192, 16384, 32768
free total: 57344 bytes
```

65536 = 8192 + 16384 + 32768 + 8192: the allocated block plus the three siblings the split
left behind. Freeing both halves coalesces back to one 65536-byte extent.

## Tests

Eight, all in `tests/MiniWebServer.Host.Tests/Program.cs`:

- `the header is charged to the request and splits are exact` — §17.2's 4088 → 3980 → 3764
  sequence, and that freeing the middle chunk yields two extents rather than coalescing
  across the live neighbours.
- `coalescing restores one extent where a naive free list makes three` — §17.2's three-chunk
  trap, the 20-byte request that fails with 20 free, and the coalesced heap that serves it.
- `best, worst and first fit each choose differently on the chapter's list` — the three
  literal resulting lists, plus a second workload where the head extent fits, to make
  §17.3's *cost* claim visible: first fit leaves the 30-byte extent whole where best fit
  chops it to 25.
- `buddy allocation splits to a power of two and coalesces back up` — the 7 KB / 8 KB split,
  the 1024-byte internal fragmentation, the XOR buddy relation, and the round trip back to
  one 64 KB extent.
- `a buddy split accounts for every byte of the heap` — a 100-byte request walks nine
  levels down; 65536 = 32768 + … + 128 + 128 allocated, and freeing it restores the heap.
- `a buddy heap refuses a request it cannot round up` — larger than the heap, one byte too
  big, and `int.MaxValue` (which the round-up loop cannot represent); an exact fit still
  works.
- `external fragmentation is the gap between free bytes and the largest extent` — §17.1's
  opening example as a number: 20 free, largest extent 10, a 15-byte request refused.
- `the heap rejects what the chapter's model cannot express` — a non-power-of-two buddy
  heap, a zero-size heap or request, and freeing a pointer `Malloc` never returned.

## Bugs found while building this

Five, all caught before the slice's suite was green:

1. **The buddy merge checked the wrong level.** `Free` asked whether the buddy was free at
   level `k+1`, but a freshly freed block is by definition on the level-`k` list, so the
   check never fired and coalescing stopped after one merge. The chapter's example — 64 KB
   freed down to one extent — is what exposed it: the tree ended with four free blocks and
   a `FreeBytes` of 122880, more than the heap.
2. **The buddy merge decremented the level count by 1 instead of 2.** Both 2^k blocks leave
   the level-`k` list and the merged one is recorded a level up, so the net is −2. At −1 a
   phantom count survived with no matching entry, and `FreeBytes` again drifted past the
   heap size while `FreeBlocks()` correctly showed one block.
3. **The free list was not address-ordered after a split.** The remainder was appended, so
   worst fit on the chapter's list produced `10 20 15` rather than the chapter's literal
   `10 15 20`. §17.3 already recommends address ordering ("by keeping the list ordered by
   the address of the free space, coalescing becomes easier"), and every resulting list the
   chapter prints is in address order.
4. **The route rejected its own scenario names.** Every query value was parsed as an
   integer before the key was examined, so `scenario=strategies` answered "parameter
   'scenario' must be an integer". String parameters are now taken first.
5. **`split` ignored the free-list node header**, so it reported 3988 where §17.2 says
   3980. The chapter uses "header" for two different things — the node's own `size`+`next`,
   and the per-allocation header before each chunk — and the arithmetic only works with both.

### Not a bug, recorded because it looked like one

`Allocate` records `_allocated[start] = need`, where `need` is the rounded-up power of two.
A mutation to `1 << level` is *equivalent* — `level = Log2(need)` by construction — and the
mutation check confirmed it: the suite stayed green, because nothing could distinguish them.
The comment that claimed a regression here was removed rather than kept, because a test that
cannot fail is worse than no test.

### Mutation-checked

Placing the last split sibling at the wrong address (`start + half * 2`) makes
`buddy allocation splits to a power of two` fail with "Expected 8192, got 16384", so the
sibling placement is pinned by a real assertion.

## What this slice does NOT do

- **Growing the heap** (§17.2 `sbrk`) — §17.1 fixes the region size.
- **Segregated lists / slab** (§17.4) and the **tree-based** scaling approaches — named by
  the chapter with citations, each a subsystem of its own.
- **Compaction** (§17.1) — ruled out by the `free(ptr)` interface.
- **Concurrency** — §17.4 cites Berger [B+00] and Evans [E06]; a thread-safe allocator is a
  different subject from the allocation *policy* this slice is about.
- **The magic number** in the header — the size is tracked in a side table instead.

## Source documents

- `docs/learning/m36-free-space/overview.md` — milestone scope.
- `docs/adr/0025-remaining-coverage-gaps-m35-m36-m37.md` — why this is a new milestone, and
  the false "Ch. 17 is implicitly covered by M12" claim it retires.
- OSTEP Ch. 17 §17.1 assumptions and the external-fragmentation example, §17.2 splitting,
  coalescing, the header, and the embedded free list, §17.3 the four fit policies, §17.4
  buddy allocation.