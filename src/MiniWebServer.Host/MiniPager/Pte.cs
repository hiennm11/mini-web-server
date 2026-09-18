namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Page table entry. OSEP §18.5 — the minimal fields for slice 14.1:
///   - Valid: is the page currently mapped?
///   - FrameNo: which physical frame holds the page (if Valid).
///   - Dirty: has the page been written? (used for write-back)
///   - Referenced: has the page been accessed? (used by Clock eviction)
///
/// Slice 14.1 only uses Valid and FrameNo; the rest are placeholders
/// for later slices (replacement policy in M18).
/// </summary>
public struct Pte
{
    public bool Valid;
    public int FrameNo;
    public bool Dirty;
    public bool Referenced;

    public static readonly Pte Empty = new() { Valid = false, FrameNo = -1 };
}

/// <summary>
/// Page table entry with swap support (M18).
///
/// OSEP §21.2 "The Present Bit":
///   "Hardware support for paging is the presence bit. The presence
///    bit indicates whether the page is in physical memory or not.
///    When the bit is set, the page is present in memory; when not,
///    the page is not in memory but is on disk somewhere (e.g., in
///    swap space)."
///
/// In our Pager we use two booleans: Valid (in-memory) and InSwap
/// (evicted to swap). If both are false, the PTE is invalid (no
/// mapping). If Valid is true, FrameNo points to a frame. If InSwap
/// is true, SwapSlot points to a swap slot.
/// </summary>
public struct SwappablePte
{
    public bool Valid;         // true if the page is currently in physical memory
    public bool InSwap;        // true if the page has been evicted to swap
    public int FrameNo;        // valid only when Valid
    public int SwapSlot;       // valid only when InSwap
    public bool Dirty;
    public bool Referenced;

    public static readonly SwappablePte Empty = new() { Valid = false, InSwap = false, FrameNo = -1, SwapSlot = -1 };
}

