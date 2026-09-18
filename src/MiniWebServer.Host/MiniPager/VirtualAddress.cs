namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Virtual address decomposed into VPN (virtual page number) and
/// offset (within page). OSEP §18.4 "Where Are Page Tables Stored?"
/// — we keep the page-table data in the same struct's PageTable
/// class so the math is clear.
///
/// Constants:
///   PAGE_SIZE       = 4096 bytes (4 KB) — the canonical small page.
///   OFFSET_BITS     = 12 (log2(4096)).
/// </summary>
public readonly record struct VirtualAddress(int Value)
{
    public const int PAGE_SIZE = 4096;
    public const int OFFSET_BITS = 12;
    public const uint OFFSET_MASK = 0xFFF;

    public int Vpn
    {
        get
        {
            uint u = (uint)Value;
            return (int)(u >> OFFSET_BITS);
        }
    }

    public int Offset => Value & (int)OFFSET_MASK;

    /// <summary>Page number that contains this address.</summary>
    public int PageNumber => Vpn;

    public override string ToString() => $"0x{Value:X8} (vpn={Vpn}, off={Offset})";
}

/// <summary>
/// Physical address — frame number + offset. OSEP §18.3.
/// </summary>
public readonly record struct PhysicalAddress(int Value)
{
    public const int PAGE_SIZE = VirtualAddress.PAGE_SIZE;
    public const int OFFSET_BITS = VirtualAddress.OFFSET_BITS;
    public const uint OFFSET_MASK = VirtualAddress.OFFSET_MASK;

    public int Frame
    {
        get
        {
            uint u = (uint)Value;
            return (int)(u >> OFFSET_BITS);
        }
    }

    public int Offset => Value & (int)OFFSET_MASK;

    public override string ToString() => $"0x{Value:X8} (frame={Frame}, off={Offset})";
}
