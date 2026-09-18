namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// One entry in the eviction-policy's bookkeeping. Tracks which frame
/// each "resident" page occupies and the metadata the policy needs.
///
/// OSEP §22.5 "Using History: LRU" + §22.7 "Implementing LRU":
///   "To track which pages have been least-recently used, the system
///    must record, for each page, the time of its last reference."
///
/// We use a 64-bit logical tick (Pager's step counter) as the time
/// stamp. The frame owner is the PID + VPN of the page currently in
/// the frame.
/// </summary>
public sealed class FrameInfo
{
    /// <summary>The frame index in PhysicalMemory (0..NumFrames-1).</summary>
    public int FrameNo;
    /// <summary>Which process owns this frame (-1 if free).</summary>
    public int OwnerPid = -1;
    /// <summary>The VPN of the page in this frame (-1 if free).</summary>
    public int Vpn = -1;
    /// <summary>Logical tick of last reference. LRU picks the smallest.</summary>
    public long LastUsedTick;
    /// <summary>Insertion order. FIFO picks the smallest.</summary>
    public long InsertedTick;
    /// <summary>True if this frame is currently allocated.</summary>
    public bool Allocated;
}

/// <summary>
/// Strategy for choosing which frame to evict when physical memory is
/// full. OSEP §22 covers FIFO, Random, LRU.
///
/// Implementations are stateless — the Pager maintains the FrameInfo[]
/// table and passes the relevant data to the policy on each eviction.
/// </summary>
public interface IEvictionPolicy
{
    /// <summary>Called when a frame is allocated (either at boot or after eviction).</summary>
    void OnAllocate(FrameInfo[] frames, int frameNo, long tick);

    /// <summary>
    /// OSEP §22.5 "Pick the victim":
    /// Returns the frame index to evict.
    /// </summary>
    int PickVictim(FrameInfo[] frames);

    /// <summary>Called on every memory access so policies like LRU can update their state.</summary>
    void OnAccess(FrameInfo[] frames, int frameNo, long tick);

    public string Name { get; }
}

/// <summary>
/// FIFO: evict the frame that was allocated earliest.
///
/// OSEP §22.3 "A Simple Policy: FIFO":
///   "When a fault occurs, simply evict the page that has been in memory
///    the longest, regardless of how often it has been used."
/// </summary>
public sealed class FifoEviction : IEvictionPolicy
{
    public string Name => "FIFO";

    public void OnAllocate(FrameInfo[] frames, int frameNo, long tick)
    {
        frames[frameNo].InsertedTick = tick;
    }

    public int PickVictim(FrameInfo[] frames)
    {
        int victim = 0;
        long oldest = frames[0].InsertedTick;
        for (int i = 1; i < frames.Length; i++)
        {
            if (frames[i].InsertedTick < oldest)
            {
                oldest = frames[i].InsertedTick;
                victim = i;
            }
        }
        return victim;
    }

    public void OnAccess(FrameInfo[] frames, int frameNo, long tick) { /* no-op */ }
}

/// <summary>
/// Random: pick a random allocated frame. OSEP §22.4 "Another Simple
/// Policy: Random" notes that random is surprisingly effective and
/// avoids the worst-case behaviors that plague LRU.
/// </summary>
public sealed class RandomEviction : IEvictionPolicy
{
    private readonly Random _rng;
    public string Name => "Random";

    public RandomEviction(int seed = 42)
    {
        _rng = new Random(seed);
    }

    public void OnAllocate(FrameInfo[] frames, int frameNo, long tick) { /* no-op */ }

    public int PickVictim(FrameInfo[] frames)
    {
        return _rng.Next(frames.Length);
    }

    public void OnAccess(FrameInfo[] frames, int frameNo, long tick) { /* no-op */ }
}

/// <summary>
/// LRU (Least Recently Used): evict the frame whose last access was longest ago.
///
/// OSEP §22.7 "Implementing LRU":
///   "the system keeps a list of pages, in order of use, so that the
///    most-recently used page is at the front of the list, and the
///    least-recently used at the back. To find the victim, just take
///    the page at the back."
///
/// We approximate with per-frame LastUsedTick timestamps; eviction picks
/// the smallest. Simpler than a linked list, equivalent semantics for
/// "pure LRU" (does not implement the "use bit + periodic clear" trick
/// of §22.8 Approximating LRU — that's a hardware optimization).
/// </summary>
public sealed class LruEviction : IEvictionPolicy
{
    public string Name => "LRU";

    public void OnAllocate(FrameInfo[] frames, int frameNo, long tick)
    {
        frames[frameNo].LastUsedTick = tick;
    }

    public int PickVictim(FrameInfo[] frames)
    {
        int victim = 0;
        long oldest = frames[0].LastUsedTick;
        for (int i = 1; i < frames.Length; i++)
        {
            if (frames[i].LastUsedTick < oldest)
            {
                oldest = frames[i].LastUsedTick;
                victim = i;
            }
        }
        return victim;
    }

    public void OnAccess(FrameInfo[] frames, int frameNo, long tick)
    {
        frames[frameNo].LastUsedTick = tick;
    }
}
