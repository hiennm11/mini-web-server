namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// OSTEP §17.4's binary buddy allocator [K65]. Slice 36.1.
/// </summary>
/// <remarks>
/// <para>
/// Free memory is a tree of power-of-two blocks. Allocation divides the largest
/// free block repeatedly until a further split would be too small, hands out the block
/// found, and leaves its siblings on the free lists. Freeing walks back up: if the
/// block's <b>buddy</b> is free, the two merge into one block of the next size up, and
/// that repeats.
/// </para>
/// <para>
/// The buddy is found by XOR, which is the property that makes the scheme work at all:
/// "the address of each buddy pair only differs by a single bit; which bit is determined
/// by the level in the buddy tree". A block at address <c>a</c> with size <c>2^k</c> has
/// its buddy at <c>a ^ 2^k</c>.
/// </para>
/// <para>
/// The cost is stated by the chapter and is visible in the tests: "this scheme can
/// suffer from internal fragmentation, as you are only allowed to give out power-of-two-
/// sized blocks". A 7 KB request gets 8 KB, and the 1 KB difference is waste inside an
/// allocated block — the other kind of fragmentation, which §17.1 deliberately set aside.
/// </para>
/// </remarks>
public sealed class BuddyAllocator
{
    private readonly int _size;

    /// <summary>Free block sizes at each level: level k holds blocks of 2^k bytes.</summary>
    private readonly List<int> _freeCounts = new();

    /// <summary>Start address of every free block at each level, ascending.</summary>
    private readonly List<List<int>> _freeStarts = new();

    /// <summary>Allocated blocks as (start, size), so <c>Free</c> can find a block's level.</summary>
    private readonly Dictionary<long, int> _allocated = new();

    public BuddyAllocator(int size)
    {
        // §17.4: "free memory is first conceptually thought of as one big space of
        // size 2^N". A buddy tree over a non-power-of-two cannot represent the tail.
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), "a heap needs room");
        if ((size & (size - 1)) != 0)
            throw new ArgumentException($"a buddy heap is 2^N bytes; {size} is not a power of two",
                nameof(size));

        _size = size;
        int levels = Log2(size) + 1;
        for (int k = 0; k < levels; k++)
        {
            _freeCounts.Add(0);
            _freeStarts.Add(new List<int>());
        }

        // The whole heap is one block at the top level.
        int top = levels - 1;
        _freeCounts[top] = 1;
        _freeStarts[top].Add(0);
    }

    /// <summary>
    /// Total free bytes. Summed per level: a block at level <c>k</c> is <c>2^k</c>
    /// bytes, and mixing that into a single count times a constant would count a
    /// block as its own size regardless of which level it sits on.
    /// </summary>
    public int FreeBytes => Enumerable.Range(0, _freeCounts.Count)
        .Sum(k => _freeCounts[k] * (1 << k));

    /// <summary>Per-level free counts, for assertions and debugging.</summary>
    public IReadOnlyList<int> DebugCounts() => _freeCounts;

    /// <summary>Total free blocks across all levels.</summary>
    public int FreeBlockCount => _freeCounts.Sum(c => c);

    public int AllocationBlockCount => _allocated.Count;

    /// <summary>The unit of waste: §17.4 only ever hands out multiples of this.</summary>
    public int SmallestBlockSize => 1;

    public int HeapSize => _size;

    /// <summary>Size of the block starting at <paramref name="start"/>, or 0 if free.</summary>
    public int BlockSizeOf(long start) =>
        _allocated.TryGetValue(start, out int bytes) ? bytes : 0;

    /// <summary>
    /// §17.4's XOR trick: the buddy of a block at <paramref name="start"/> of
    /// <paramref name="blockSize"/> bytes.
    /// </summary>
    public static long BuddyAddress(long start, int blockSize) => start ^ (long)blockSize;

    /// <summary>
    /// §17.4's search: "the search for free space recursively divides free space by
    /// two until a block that is big enough to accommodate the request is found (and a
    /// further split into two would result in a space that is too small)."
    /// </summary>
    public long Allocate(int request)
    {
        if (request < 1) throw new ArgumentOutOfRangeException(nameof(request), "a request is at least 1 byte");

        int need = RoundUpToPowerOfTwo(request);
        int level = Log2(need);

        // Not enough room even after splitting: the tree has no block at or above
        // this level. Failing is §17.2's "returning NULL is an honorable approach".
        if (level >= _freeCounts.Count && _freeCounts.Count == 0) return -1;
        if (level > _freeCounts.Count - 1) return -1;

        int source = FindLevelWithFreeBlock(level);
        if (source < 0) return -1;

        // Split down from `source` to `level`, handing each sibling to the level below.
        //
        // The block being split HALVES at each step, so its own address stays the
        // same while its size drops; the sibling goes at `start + half`, where
        // `half` is that level's size and not a fixed one.
        long start = TakeBlock(source);
        for (int k = source; k > level; k--)
        {
            long half = 1L << (k - 1);
            // The right half is the buddy of the left, and is now free.
            _freeCounts[k - 1]++;
            _freeStarts[k - 1].Add((int)(start + half));
            // The left half keeps `start` and is what we keep splitting.
        }

        // `need` is the block size, not the request: it is the rounded-up power of
        // two, and `1 << level` is that same number. The block is recorded at the
        // level it occupies, because that is what `Free` needs to find its buddy.
        _allocated[start] = need;
        return start;
    }

    /// <summary>
    /// §17.4's recursive coalescing: free the block, then merge with the buddy for as
    /// long as the buddy is free.
    /// </summary>
    public void Free(long ptr)
    {
        if (!_allocated.TryGetValue(ptr, out int bytes))
            throw new ArgumentException($"0x{ptr:X} was not returned by Allocate", nameof(ptr));
        _allocated.Remove(ptr);

        // The freed block joins the free list at its own level. The chapter's
        // example: freeing an 8 KB block puts it on the 8 KB list, where it can be
        // merged with its 8 KB buddy to make a 16 KB block.
        //
        // Level bookkeeping: this block adds one entry at `level`. Every merge below
        // removes two entries at `level` and adds one at `level+1`, so the count at
        // `level` must drop by 1 per merge, not by 2 — the newly merged block is
        // counted at the level above. Getting this wrong leaves phantom counts
        // behind and FreeBytes drifts upward past the heap size.
        int level = Log2(bytes);
        long current = ptr;
        _freeCounts[level]++;
        _freeStarts[level].Add((int)current);

        // Then walk up. Both halves of the next level up must already be free for
        // the merge to be legal - the buddy of a 2^k block at address `current` is
        // at `current ^ 2^k`, and both are recorded on the SAME level k list.
        // Checking one level up (as an earlier version did) never fires, because a
        // freshly freed block is by definition not yet on the level above.
        while (level + 1 < _freeCounts.Count)
        {
            long buddy = BuddyAddress(current, 1 << level);
            int buddyIdx = _freeStarts[level].IndexOf((int)buddy);
            int selfIdx = _freeStarts[level].IndexOf((int)current);
            if (buddyIdx < 0 || selfIdx < 0) break;

            // Remove both 2^k blocks from the level-k list and record one 2^(k+1).
            // Both entries leave the level-k list and nothing is added there, so the
            // count drops by 2 - the merged block is recorded at level k+1 instead.
            // Dropping it by 1 leaves a phantom count with no matching entry, and
            // FreeBytes then reports more free space than the heap has.
            int lo = (int)Math.Min(current, buddy);
            _freeStarts[level].RemoveAt(Math.Max(buddyIdx, selfIdx));
            _freeStarts[level].RemoveAt(Math.Min(buddyIdx, selfIdx));
            _freeCounts[level] -= 2;

            current = lo;
            level++;
            _freeCounts[level]++;
            _freeStarts[level].Add((int)current);
        }
    }

    /// <summary>Free block boundaries, for the route's tree display.</summary>
    public IEnumerable<(int Start, int Size)> FreeBlocks()
    {
        for (int k = 0; k < _freeCounts.Count; k++)
        {
            int size = 1 << k;
            foreach (int start in _freeStarts[k]) yield return (start, size);
        }
    }



    private int FindLevelWithFreeBlock(int minLevel)
    {
        for (int k = minLevel; k < _freeCounts.Count; k++)
            if (_freeCounts[k] > 0) return k;
        return -1;
    }

    /// <summary>
    /// Removes and returns a free block at <paramref name="level"/>. With no
    /// address given it takes the lowest one, which is the left half of the lowest
    /// free block — the same choice first-fit makes, and the one that keeps
    /// coalescing cheap because the buddy is then the neighbour.
    /// </summary>
    private long TakeBlock(int level, int start = -1)
    {
        var list = _freeStarts[level];
        int idx = start < 0 ? 0 : list.IndexOf(start);
        if (idx < 0)
        {
            // Falling back to "some other block" here would return a plausible
            // address for a request that does not exist. Failing loudly is better.
            throw new InvalidOperationException(
                $"no free block of size {1 << level} starts at {start}");
        }
        int taken = list[idx];
        list.RemoveAt(idx);
        _freeCounts[level]--;
        return taken;
    }

    private static int RoundUpToPowerOfTwo(int n)
    {
        int p = 1;
        while (p < n)
        {
            if (p > int.MaxValue / 2) throw new ArgumentOutOfRangeException(nameof(n), "too large");
            p <<= 1;
        }
        return p;
    }

    private static int Log2(int n)
    {
        int k = 0;
        while ((1 << k) < n) k++;
        return k;
    }
}