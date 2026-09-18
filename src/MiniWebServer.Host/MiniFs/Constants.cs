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
    public const int JOURNAL_START = INODE_TABLE_START + INODE_BLOCKS;  // = 7
    public const int JOURNAL_BLOCKS = 64;                              // slice 12.6: bumped from 8 to avoid wrap
    public const int DATA_BLOCKS_START = JOURNAL_START + JOURNAL_BLOCKS;  // = 71

    public const int NUM_DATA_BLOCKS = NUM_BLOCKS - DATA_BLOCKS_START;

    // Direct block pointers per inode
    public const int NDIRECT = 12;

    // Magic number for the FS (any 32-bit value, chosen to be recognizable)
    public const uint FS_MAGIC = 0x1F5EF5E1;

    // Journal layout constants (slice 12.5)
    public const int JOURNAL_SUPERBLOCK_BLOCK = JOURNAL_START;        // 7
    public const int JOURNAL_DATA_START = JOURNAL_START + 1;         // 8
    public const int JOURNAL_DATA_BLOCKS = JOURNAL_BLOCKS - 1;        // 7 (data region after superblock)
    public const uint JOURNAL_SB_MAGIC = 0xCAFE_BABE;
    public const uint TXB_MAGIC = 0xAABB_CCDD;
    public const uint TXE_MAGIC = 0xDDCC_BBAA;

    /// <summary>
    /// Max block updates per transaction (slice 12.6). TxB stores
    /// [magic:4][tid:4][count:4][blockNo x MAX_BLOCKS_PER_TX] at fixed
    /// offsets, so 4096/4 = 1024 minus 3 headers = 1021 entries.
    /// </summary>
    public const int MAX_BLOCKS_PER_TX = 1021;
}