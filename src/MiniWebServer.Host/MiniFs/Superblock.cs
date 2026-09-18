namespace MiniWebServer.Host.MiniFs;

/// <summary>
/// On-disk superblock. Stored at offset 0 of the disk image (block 0).
/// Describes the layout of the rest of the file system. OSEP §40.3.
/// </summary>
public sealed class Superblock
{
    public uint Magic;
    public int TotalInodes;
    public int TotalBlocks;
    public int FreeInodes;
    public int FreeDataBlocks;
    public int InodeBitmapBlock;
    public int DataBitmapBlock;
    public int InodeTableStart;
    public int DataBlocksStart;
    public int NumDataBlocks;
}