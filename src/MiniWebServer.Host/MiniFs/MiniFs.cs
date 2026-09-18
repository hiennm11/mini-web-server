using static MiniWebServer.Host.MiniFs.Constants;

namespace MiniWebServer.Host.MiniFs;

/// <summary>
/// Mini file system in user space. The "disk" is a byte[] of size
/// NUM_BLOCKS * BLOCK_SIZE. Operations translate logical concepts
/// (inodes, data blocks) into byte offsets in this array.
///
/// Layout (simplified xv6):
///   block 0         : superblock
///   block 1         : inode bitmap (1 bit per inode, NUM_INODES bits)
///   block 2         : data bitmap (1 bit per data block)
///   blocks 3..6     : inode table (NUM_INODES inodes * INODE_SIZE bytes)
///   blocks 7..255   : data blocks (NUM_DATA_BLOCKS = 249 blocks)
///
/// OSEP Ch.40 file system implementation; this is slice 12.1: superblock
/// + bitmaps + alloc/free primitives only. Inodes and data block
/// content are wired up in subsequent slices.
/// </summary>
public static class MiniFs
{
    private static byte[]? _disk;
    private static Superblock _sb = new();
    private static bool _mounted;

    public static bool IsMounted => _mounted;
    public static Superblock Superblock => _sb;

    /// <summary>
    /// Mount the FS. If the disk is uninitialized (magic wrong), calls
    /// Format() to create a fresh empty FS. Returns true on success.
    /// </summary>
    public static bool Mount()
    {
        if (_mounted) return true;
        _disk = new byte[NUM_BLOCKS * BLOCK_SIZE];

        // Try to read existing superblock
        var existing = ReadSuperblock();
        if (existing != null && existing.Magic == FS_MAGIC)
        {
            _sb = existing;
            _mounted = true;
            return true;
        }

        // Fresh disk - format
        Format();
        _mounted = true;
        return true;
    }

    public static void Unmount()
    {
        _disk = null;
        _mounted = false;
    }

    /// <summary>
    /// Format the disk: zero everything, write the superblock, mark
    /// all inodes and data blocks as free (except inode 0 which is
    /// reserved as "no inode" - similar to xv6's dev/sw convention).
    /// </summary>
    public static void Format()
    {
        if (_disk == null) throw new InvalidOperationException("disk not allocated");

        // Zero the disk
        Array.Clear(_disk, 0, _disk.Length);

        _sb = new Superblock
        {
            Magic = FS_MAGIC,
            TotalInodes = NUM_INODES,
            TotalBlocks = NUM_BLOCKS,
            FreeInodes = NUM_INODES - 1,  // reserve inode 0
            FreeDataBlocks = NUM_DATA_BLOCKS,
            InodeBitmapBlock = INODE_BITMAP_BLOCK,
            DataBitmapBlock = DATA_BITMAP_BLOCK,
            InodeTableStart = INODE_TABLE_START,
            DataBlocksStart = DATA_BLOCKS_START,
            NumDataBlocks = NUM_DATA_BLOCKS,
        };
        WriteSuperblock();

        // Set bits 1..NUM_INODES-1 to 0 (free) in inode bitmap
        // Bit i is in byte (i / 8), position (i % 8)
        // Reserve inode 0 by setting bit 0 to 1 (in-use as "no inode")
        SetBit(INODE_BITMAP_BLOCK, 0);

        // All data blocks start free (zeros from Array.Clear above)
    }

    // --- Superblock IO ---

    private static Superblock? ReadSuperblock()
    {
        if (_disk == null) return null;
        int off = SUPERBLOCK_BLOCK * BLOCK_SIZE;
        var sb = new Superblock
        {
            Magic = BitConverter.ToUInt32(_disk, off),
            TotalInodes = BitConverter.ToInt32(_disk, off + 4),
            TotalBlocks = BitConverter.ToInt32(_disk, off + 8),
            FreeInodes = BitConverter.ToInt32(_disk, off + 12),
            FreeDataBlocks = BitConverter.ToInt32(_disk, off + 16),
            InodeBitmapBlock = BitConverter.ToInt32(_disk, off + 20),
            DataBitmapBlock = BitConverter.ToInt32(_disk, off + 24),
            InodeTableStart = BitConverter.ToInt32(_disk, off + 28),
            DataBlocksStart = BitConverter.ToInt32(_disk, off + 32),
            NumDataBlocks = BitConverter.ToInt32(_disk, off + 36),
        };
        return sb;
    }

    private static void WriteSuperblock()
    {
        if (_disk == null) throw new InvalidOperationException("disk not allocated");
        int off = SUPERBLOCK_BLOCK * BLOCK_SIZE;
        BitConverter.GetBytes(_sb.Magic).CopyTo(_disk, off);
        BitConverter.GetBytes(_sb.TotalInodes).CopyTo(_disk, off + 4);
        BitConverter.GetBytes(_sb.TotalBlocks).CopyTo(_disk, off + 8);
        BitConverter.GetBytes(_sb.FreeInodes).CopyTo(_disk, off + 12);
        BitConverter.GetBytes(_sb.FreeDataBlocks).CopyTo(_disk, off + 16);
        BitConverter.GetBytes(_sb.InodeBitmapBlock).CopyTo(_disk, off + 20);
        BitConverter.GetBytes(_sb.DataBitmapBlock).CopyTo(_disk, off + 24);
        BitConverter.GetBytes(_sb.InodeTableStart).CopyTo(_disk, off + 28);
        BitConverter.GetBytes(_sb.DataBlocksStart).CopyTo(_disk, off + 32);
        BitConverter.GetBytes(_sb.NumDataBlocks).CopyTo(_disk, off + 36);
    }

    // --- Bitmap primitives ---

    private static bool TestBit(int block, int bit)
    {
        if (_disk == null) throw new InvalidOperationException("not mounted");
        int byteOff = block * BLOCK_SIZE + (bit / 8);
        int bitPos = bit % 8;
        return (_disk[byteOff] & (1 << bitPos)) != 0;
    }

    private static void SetBit(int block, int bit)
    {
        if (_disk == null) throw new InvalidOperationException("not mounted");
        int byteOff = block * BLOCK_SIZE + (bit / 8);
        int bitPos = bit % 8;
        _disk[byteOff] |= (byte)(1 << bitPos);
    }

    private static void ClearBit(int block, int bit)
    {
        if (_disk == null) throw new InvalidOperationException("not mounted");
        int byteOff = block * BLOCK_SIZE + (bit / 8);
        int bitPos = bit % 8;
        _disk[byteOff] &= (byte)~(1 << bitPos);
    }

    // --- Alloc/free for inodes and data blocks ---

    /// <summary>
    /// Allocate an inode. Returns the inode number (1..NUM_INODES-1) or
    /// -1 if no inodes are free. OSEP §40.4: scan the bitmap, set the
    /// first zero bit, update the free count.
    /// </summary>
    public static int Ialloc()
    {
        if (_sb.FreeInodes <= 0) return -1;
        for (int i = 1; i < NUM_INODES; i++)
        {
            if (!TestBit(_sb.InodeBitmapBlock, i))
            {
                SetBit(_sb.InodeBitmapBlock, i);
                _sb.FreeInodes--;
                WriteSuperblock();
                return i;
            }
        }
        return -1;
    }

    /// <summary>Free an inode (mark its bit as 0).</summary>
    public static void Ifree(int ino)
    {
        if (ino <= 0 || ino >= NUM_INODES) return;
        if (!TestBit(_sb.InodeBitmapBlock, ino)) return;
        ClearBit(_sb.InodeBitmapBlock, ino);
        _sb.FreeInodes++;
        WriteSuperblock();
    }

    /// <summary>
    /// Allocate a data block. Returns the block number (relative to
    /// DATA_BLOCKS_START) or -1 if no blocks are free.
    /// </summary>
    public static int Balloc()
    {
        if (_sb.FreeDataBlocks <= 0) return -1;
        for (int i = 0; i < NUM_DATA_BLOCKS; i++)
        {
            if (!TestBit(_sb.DataBitmapBlock, i))
            {
                SetBit(_sb.DataBitmapBlock, i);
                _sb.FreeDataBlocks--;
                WriteSuperblock();
                return i;  // block index in the data-block region
            }
        }
        return -1;
    }

    /// <summary>Free a data block.</summary>
    public static void Bfree(int dataBlockIndex)
    {
        if (dataBlockIndex < 0 || dataBlockIndex >= NUM_DATA_BLOCKS) return;
        if (!TestBit(_sb.DataBitmapBlock, dataBlockIndex)) return;
        ClearBit(_sb.DataBitmapBlock, dataBlockIndex);
        _sb.FreeDataBlocks++;
        WriteSuperblock();
    }

    /// <summary>Absolute block number on the disk for a given data block index.</summary>
    public static int DataBlockAbsolute(int dataBlockIndex)
        => DATA_BLOCKS_START + dataBlockIndex;

    // --- Block IO ---

    public static void ReadBlock(int blockNo, byte[] dest)
    {
        if (_disk == null) throw new InvalidOperationException("not mounted");
        if (blockNo < 0 || blockNo >= NUM_BLOCKS) throw new ArgumentOutOfRangeException(nameof(blockNo));
        if (dest.Length != BLOCK_SIZE) throw new ArgumentException("dest must be BLOCK_SIZE bytes", nameof(dest));
        Array.Copy(_disk, blockNo * BLOCK_SIZE, dest, 0, BLOCK_SIZE);
    }

    public static void WriteBlock(int blockNo, byte[] src)
    {
        if (_disk == null) throw new InvalidOperationException("not mounted");
        if (blockNo < 0 || blockNo >= NUM_BLOCKS) throw new ArgumentOutOfRangeException(nameof(blockNo));
        if (src.Length != BLOCK_SIZE) throw new ArgumentException("src must be BLOCK_SIZE bytes", nameof(src));
        Array.Copy(src, 0, _disk, blockNo * BLOCK_SIZE, BLOCK_SIZE);
    }

    // --- Stats ---

    /// <summary>Count of bits set (= in-use inodes) in the inode bitmap.</summary>
    public static int InodesInUse()
    {
        int count = 0;
        for (int i = 0; i < NUM_INODES; i++)
            if (TestBit(_sb.InodeBitmapBlock, i)) count++;
        return count;
    }

    /// <summary>Count of bits set (= in-use data blocks) in the data bitmap.</summary>
    public static int DataBlocksInUse()
    {
        int count = 0;
        for (int i = 0; i < NUM_DATA_BLOCKS; i++)
            if (TestBit(_sb.DataBitmapBlock, i)) count++;
        return count;
    }

    /// <summary>Total bytes on the "disk".</summary>
    public static int DiskSizeBytes
        => _disk?.Length ?? 0;
}