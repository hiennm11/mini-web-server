namespace MiniWebServer.Host.MiniFs;

/// <summary>
/// On-disk layout constants for the mini file system. Block size 4 KB
/// matches the typical sector/page-cache page size on Linux and Windows.
/// </summary>
public static class Constants
{
    public const int BLOCK_SIZE = 4096;
    public const int NUM_INODES = 256;
    public const int INODE_SIZE = 64;
    public const int INODE_BLOCKS = (NUM_INODES * INODE_SIZE + BLOCK_SIZE - 1) / BLOCK_SIZE;
    public const int NUM_BLOCKS = 256;

    // Block layout (xv6-style, simplified)
    public const int SUPERBLOCK_BLOCK = 0;
    public const int INODE_BITMAP_BLOCK = 1;
    public const int DATA_BITMAP_BLOCK = 2;
    public const int INODE_TABLE_START = 3;
    public const int DATA_BLOCKS_START = INODE_TABLE_START + INODE_BLOCKS;

    public const int NUM_DATA_BLOCKS = NUM_BLOCKS - DATA_BLOCKS_START;

    // Direct block pointers per inode
    public const int NDIRECT = 12;

    // Magic number for the FS (any 32-bit value, chosen to be recognizable)
    public const uint FS_MAGIC = 0x1F5EF5E1;
}