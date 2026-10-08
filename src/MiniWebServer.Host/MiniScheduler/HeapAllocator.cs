namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// OSTEP §17.3: how an allocator chooses which free chunk to satisfy a request.
/// </summary>
public enum FitPolicy
{
    /// <summary>§17.3 First Fit — "finds the first block that is big enough".</summary>
    First,

    /// <summary>§17.3 Best Fit — "the smallest in that group of candidates".</summary>
    Best,

    /// <summary>§17.3 Worst Fit — "find the largest chunk and return the requested amount".</summary>
    Worst,

    /// <summary>§17.3 Next Fit — first fit, but resuming where the last search stopped.</summary>
    Next,
}

/// <summary>One free extent: <see cref="Start"/> .. <see cref="Start"/>+<see cref="Length"/>-1.</summary>
public readonly record struct FreeChunk(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// A heap allocator over a fixed region, implementing OSTEP Ch. 17 §17.2-§17.3.
/// Slice 36.1.
/// </summary>
/// <remarks>
/// <para>
/// §17.2's three mechanisms are here: <b>splitting</b> on allocation, <b>coalescing</b> on
/// free, and a free list. The <b>header</b> of §17.2 is modelled as a per-allocation
/// size field the caller does not pass — the point of the chapter's interface is that
/// <c>free(ptr)</c> takes no size, so the library must already know it.
/// </para>
/// <para>
/// The heap is <see cref="int"/> offsets rather than pointers, which is what makes the
/// chapter's literal diagrams checkable: it prints addresses, and an offset is the same
/// number with less ceremony.
/// </para>
/// </remarks>
public sealed class HeapAllocator
{
    private readonly List<FreeChunk> _free = new();
    private readonly Dictionary<long, int> _allocated = new();  // start -> total bytes incl. header
    private readonly FitPolicy _policy;
    private readonly int _headerBytes;
    private readonly bool _coalesce;
    private readonly int _nodeHeaderBytes;
    private readonly int _size;

    /// <summary>§17.3 Next Fit's remembered position in the free list.</summary>
    private int _nextFitCursor;

    public int Size => _size;

    /// <summary>The free list, in address order. The chapter's diagrams are address-ordered.</summary>
    public IReadOnlyList<FreeChunk> FreeChunks => _free;

    public int FreeBytes => _free.Sum(c => c.Length);
    public int AllocatedBytes => _allocated.Values.Sum();

    /// <summary>
    /// Largest request that could be satisfied right now. This is the number external
    /// fragmentation is about: it can be far below <see cref="FreeBytes"/>.
    /// </summary>
    public int LargestFreeChunk => _free.Count == 0 ? 0 : _free.Max(c => c.Length);

    public HeapAllocator(int size, FitPolicy policy, int headerBytes = 0, bool coalesce = true,
        int nodeHeaderBytes = 0)
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), "a heap needs room");
        if (headerBytes < 0) throw new ArgumentOutOfRangeException(nameof(headerBytes));
        if (nodeHeaderBytes < 0) throw new ArgumentOutOfRangeException(nameof(nodeHeaderBytes));
        if (size <= nodeHeaderBytes)
            throw new ArgumentException("the free-list node header must fit in the heap", nameof(nodeHeaderBytes));
        _size = size;
        _policy = policy;
        _headerBytes = headerBytes;
        _coalesce = coalesce;
        // §17.2 embedding: the free list's first node lives at the head of the heap
        // and is not available for allocation, so a 4096-byte heap starts with
        // "a single entry, of size 4088" - 4096 minus the node's own header.
        _nodeHeaderBytes = nodeHeaderBytes;
        _free.Add(new FreeChunk(nodeHeaderBytes, size - nodeHeaderBytes));
    }

    /// <summary>
    /// §17.2's embedding step: the free list's first node lives inside the free space,
    /// so a 4096-byte heap with an 8-byte node starts with 4088 free.
    /// </summary>
    public static HeapAllocator Heap(int size, FitPolicy policy, int headerBytes = 0, bool coalesce = true,
        int nodeHeaderBytes = 0) =>
        new(size, policy, headerBytes, coalesce, nodeHeaderBytes);

    /// <summary>
    /// §17.1's <c>malloc(size)</c>. Returns the start offset, or -1 when no single free
    /// chunk is large enough — which is what §17.2 means by a failed request.
    /// </summary>
    public long Malloc(int size)
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), "a request is at least 1 byte");

        // §17.2: "the library does not search for a free chunk of size N; rather, it
        // searches for a free chunk of size N plus the size of the header."
        int need = size + _headerBytes;

        int index = Select(need);
        if (index < 0) return -1;

        FreeChunk chunk = _free[index];
        _free.RemoveAt(index);

        if (chunk.Length > need)
        {
            // §17.2 splitting: hand back `need`, keep the remainder on the list.
            // §17.3 notes that address-ordered lists make coalescing easier and
            // reduce fragmentation, and the chapter's resulting lists are all
            // written in address order - so the remainder goes back in position.
            int insertAt = _free.FindIndex(c => c.Start > chunk.Start);
            var remainder = new FreeChunk(chunk.Start + need, chunk.Length - need);
            if (insertAt < 0) _free.Add(remainder); else _free.Insert(insertAt, remainder);
        }
        _allocated[chunk.Start] = need;
        return chunk.Start;
    }

    /// <summary>
    /// §17.1's <c>free(ptr)</c>. The size is not supplied by the caller, so it is
    /// recovered from the allocation record the way §17.2 recovers it from the header.
    /// </summary>
    public void Free(long ptr)
    {
        if (!_allocated.TryGetValue(ptr, out int bytes))
            throw new ArgumentException($"0x{ptr:X} was not returned by Malloc", nameof(ptr));

        _allocated.Remove(ptr);
        _free.Add(new FreeChunk((int)ptr, bytes));

        if (!_coalesce) return;

        // §17.2 coalescing: merge neighbours. The list is kept in address order, so
        // the whole heap coalesces back to one chunk with no extra bookkeeping -
        // which is the chapter's stated reason for preferring address ordering.
        _free.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 0; i < _free.Count - 1; i++)
        {
            if (_free[i].End == _free[i + 1].Start)
            {
                _free[i] = _free[i] with { Length = _free[i].Length + _free[i + 1].Length };
                _free.RemoveAt(i + 1);
                i--;   // the merged chunk may now touch the next one
            }
        }
    }

    /// <summary>
    /// §17.3's worked example: a free list of three extents of the given sizes,
    /// laid out contiguously in that order, with no coalescing and no headers.
    /// The chapter writes the resulting list for each policy in literals, so this
    /// is the starting state those literals describe.
    /// </summary>
    public static HeapAllocator FixedExtentHeap(int[] sizes, FitPolicy policy = FitPolicy.Best)
    {
        if (sizes.Length == 0) throw new ArgumentException("need at least one extent");
        var heap = new HeapAllocator(sizes.Sum(), policy, coalesce: false);
        heap._free.Clear();
        int at = 0;
        foreach (int len in sizes)
        {
            if (len < 1) throw new ArgumentException("an extent is at least 1 byte");
            heap._free.Add(new FreeChunk(at, len));
            at += len;
        }
        return heap;
    }

    /// <summary>Total bytes a caller would receive back for a request of this size, or -1.</summary>
    public bool CanSatisfy(int size) => Select(size + _headerBytes) >= 0;

    private int Select(int need)
    {
        switch (_policy)
        {
            case FitPolicy.First:
                return _free.FindIndex(c => c.Length >= need);

            case FitPolicy.Best:
            {
                // "find chunks ... that are as big or bigger than the requested size.
                //  Then, return the one that is the smallest in that group."
                int best = -1;
                for (int i = 0; i < _free.Count; i++)
                {
                    if (_free[i].Length < need) continue;
                    if (best < 0 || _free[i].Length < _free[best].Length) best = i;
                }
                return best;
            }

            case FitPolicy.Worst:
            {
                // "find the largest chunk and return the requested amount; keep the
                //  remaining (large) chunk on the free list."
                int worst = -1;
                for (int i = 0; i < _free.Count; i++)
                {
                    if (_free[i].Length < need) continue;
                    if (worst < 0 || _free[i].Length > _free[worst].Length) worst = i;
                }
                return worst;
            }

            case FitPolicy.Next:
            {
                // §17.3: "keeps an extra pointer to the location within the list where
                // one was looking last", spreading searches so the head of the list
                // is not splintered.
                for (int step = 0; step < _free.Count; step++)
                {
                    int i = (_nextFitCursor + step) % _free.Count;
                    if (_free[i].Length >= need)
                    {
                        _nextFitCursor = (i + 1) % Math.Max(1, _free.Count);
                        return i;
                    }
                }
                return -1;
            }

            default:
                throw new InvalidOperationException($"unhandled policy {_policy}");
        }
    }

    /// <summary>A map bar for the route: one character per 4-byte cell of the heap.</summary>
    public string FormatLayout(int cellBytes = 4)
    {
        var used = new bool[_size / cellBytes + 1];
        foreach (var (start, bytes) in _allocated)
        {
            for (int i = 0; i < bytes; i++)
            {
                int cell = (int)((start + i) / cellBytes);
                if (cell < used.Length) used[cell] = true;
            }
        }
        int cells = Math.Min(_size / cellBytes, used.Length);
        var sb = new System.Text.StringBuilder(cells);
        for (int i = 0; i < cells; i++) sb.Append(used[i] ? '#' : '.');
        return sb.ToString();
    }
}