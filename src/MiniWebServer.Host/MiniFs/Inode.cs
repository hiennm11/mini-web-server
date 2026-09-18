using static MiniWebServer.Host.MiniFs.Constants;

namespace MiniWebServer.Host.MiniFs;

/// <summary>
/// On-disk inode. Fixed 64 bytes so the inode table can be indexed
/// directly. OSEP §40.6: type, size, and a small array of direct
/// block pointers. Real systems add indirect/double-indirect
/// pointers for large files; we stick to direct only.
/// </summary>
public sealed class Inode
{
    /// <summary>File type. 0 = free, 1 = file, 2 = directory.</summary>
    public ushort Type;

    /// <summary>Number of linkers (only used for directories/unlink accounting).</summary>
    public ushort Nlink;

    /// <summary>File size in bytes.</summary>
    public int Size;

    /// <summary>Direct block pointers into the data-block region (0..NumDataBlocks-1).</summary>
    public int[] DirectBlocks = new int[NDIRECT];

    public const ushort TYPE_FREE = 0;
    public const ushort TYPE_FILE = 1;
    public const ushort TYPE_DIR = 2;

    public bool IsFree => Type == TYPE_FREE;
}