namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Page table entry. OSEP §18.5 — the minimal fields for slice 14.1:
///   - Valid: is the page currently mapped?
///   - FrameNo: which physical frame holds the page (if Valid).
///   - Dirty: has the page been written? (used for write-back)
///   - Referenced: has the page been accessed? (used by Clock eviction)
///
/// Slice 14.1 only uses Valid and FrameNo; the rest are placeholders
/// for later slices (replacement policy in 14.4).
/// </summary>
public struct Pte
{
    public bool Valid;
    public int FrameNo;
    public bool Dirty;
    public bool Referenced;

    public static readonly Pte Empty = new() { Valid = false, FrameNo = -1 };
}
