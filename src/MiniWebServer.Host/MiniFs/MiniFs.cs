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

    // --- Inode table ---

    /// <summary>
    /// Absolute block number of the inode table block that holds
    /// inode number <paramref name="ino"/>.
    /// </summary>
    private static int InodeBlock(int ino)
        => _sb.InodeTableStart + (ino * INODE_SIZE) / BLOCK_SIZE;

    /// <summary>Byte offset within that inode-table block.</summary>
    private static int InodeBlockOffset(int ino)
        => (ino * INODE_SIZE) % BLOCK_SIZE;

    /// <summary>
    /// Read an inode from the inode table. Returns an Inode with
    /// Type=TYPE_FREE if the slot is free. OSEP §40.6.
    /// </summary>
    public static Inode Iget(int ino)
    {
        if (_disk == null) throw new InvalidOperationException("not mounted");
        if (ino < 0 || ino >= NUM_INODES) throw new ArgumentOutOfRangeException(nameof(ino));
        var block = new byte[BLOCK_SIZE];
        ReadBlock(InodeBlock(ino), block);
        int off = InodeBlockOffset(ino);
        var inode = new Inode
        {
            Type = BitConverter.ToUInt16(block, off),
            Nlink = BitConverter.ToUInt16(block, off + 2),
            Size = BitConverter.ToInt32(block, off + 4),
        };
        for (int i = 0; i < NDIRECT; i++)
            inode.DirectBlocks[i] = BitConverter.ToInt32(block, off + 8 + i * 4);
        return inode;
    }

    /// <summary>Write an inode to the inode table.</summary>
    public static void Iput(int ino, Inode inode)
    {
        if (_disk == null) throw new InvalidOperationException("not mounted");
        if (ino < 0 || ino >= NUM_INODES) throw new ArgumentOutOfRangeException(nameof(ino));
        var block = new byte[BLOCK_SIZE];
        ReadBlock(InodeBlock(ino), block);
        int off = InodeBlockOffset(ino);
        BitConverter.GetBytes(inode.Type).CopyTo(block, off);
        BitConverter.GetBytes(inode.Nlink).CopyTo(block, off + 2);
        BitConverter.GetBytes(inode.Size).CopyTo(block, off + 4);
        for (int i = 0; i < NDIRECT; i++)
            BitConverter.GetBytes(inode.DirectBlocks[i]).CopyTo(block, off + 8 + i * 4);
        WriteBlock(InodeBlock(ino), block);
    }

    /// <summary>
    /// Initialize a newly-allocated inode in the inode table. Sets
    /// type, nlink=1, size=0, zeros all direct block pointers.
    /// </summary>
    public static void Iinit(int ino, ushort type)
    {
        var inode = new Inode { Type = type, Nlink = 1, Size = 0 };
        for (int i = 0; i < NDIRECT; i++) inode.DirectBlocks[i] = -1;
        Iput(ino, inode);
    }

    /// <summary>Mark an inode as free in the table and release its blocks.</summary>
    public static void Idestroy(int ino)
    {
        var inode = Iget(ino);
        if (inode.IsFree) return;
        for (int i = 0; i < NDIRECT; i++)
        {
            if (inode.DirectBlocks[i] >= 0)
            {
                Bfree(inode.DirectBlocks[i]);
                inode.DirectBlocks[i] = -1;
            }
        }
        inode.Type = Inode.TYPE_FREE;
        inode.Size = 0;
        inode.Nlink = 0;
        Iput(ino, inode);
        Ifree(ino);
    }

    // --- File read/write ---

    /// <summary>
    /// Read <paramref name="len"/> bytes from the file at offset
    /// <paramref name="offset"/> into <paramref name="buf"/>. Returns
    /// the number of bytes read (may be less than <paramref name="len"/>
    /// at EOF). OSEP §40.6 readi.
    /// </summary>
    public static int Readi(int ino, byte[] buf, int offset, int len)
    {
        var inode = Iget(ino);
        if (inode.IsFree) return 0;
        if (offset >= inode.Size) return 0;
        if (offset + len > inode.Size) len = inode.Size - offset;

        int bytesRead = 0;
        while (bytesRead < len)
        {
            int logicalBlock = (offset + bytesRead) / BLOCK_SIZE;
            int blockOffset = (offset + bytesRead) % BLOCK_SIZE;
            int chunk = Math.Min(len - bytesRead, BLOCK_SIZE - blockOffset);
            if (logicalBlock >= NDIRECT || inode.DirectBlocks[logicalBlock] < 0) break;

            var blk = new byte[BLOCK_SIZE];
            ReadBlock(DataBlockAbsolute(inode.DirectBlocks[logicalBlock]), blk);
            Array.Copy(blk, blockOffset, buf, bytesRead, chunk);
            bytesRead += chunk;
        }
        return bytesRead;
    }

    /// <summary>
    /// Write <paramref name="len"/> bytes from <paramref name="buf"/>
    /// to the file at offset <paramref name="offset"/>. Allocates new
    /// data blocks as needed. OSEP §40.6 writei.
    /// </summary>
    public static int Writei(int ino, byte[] buf, int offset, int len)
    {
        var inode = Iget(ino);
        if (inode.IsFree) return 0;

        int bytesWritten = 0;
        while (bytesWritten < len)
        {
            int logicalBlock = (offset + bytesWritten) / BLOCK_SIZE;
            int blockOffset = (offset + bytesWritten) % BLOCK_SIZE;
            int chunk = Math.Min(len - bytesWritten, BLOCK_SIZE - blockOffset);

            if (logicalBlock >= NDIRECT)
                throw new InvalidOperationException($"file too large (max {NDIRECT * BLOCK_SIZE} bytes)");

            // Allocate a data block on first write to this slot
            if (inode.DirectBlocks[logicalBlock] < 0)
            {
                int newBlock = Balloc();
                if (newBlock < 0) throw new InvalidOperationException("out of data blocks");
                inode.DirectBlocks[logicalBlock] = newBlock;
            }

            var blk = new byte[BLOCK_SIZE];
            ReadBlock(DataBlockAbsolute(inode.DirectBlocks[logicalBlock]), blk);
            Array.Copy(buf, bytesWritten, blk, blockOffset, chunk);
            WriteBlock(DataBlockAbsolute(inode.DirectBlocks[logicalBlock]), blk);
            bytesWritten += chunk;
        }

        if (offset + bytesWritten > inode.Size)
            inode.Size = offset + bytesWritten;
        Iput(ino, inode);
        return bytesWritten;
    }

    // --- Directory operations ---

    /// <summary>
    /// Inode number of the FS root. Allocated on first mount; persists
    /// for the lifetime of the FS. OSEP §40.7.
    /// </summary>
    public const int ROOT_INO = 1;

    /// <summary>
    /// Initialize the root directory: allocate inode 1 (if not yet),
    /// mark it as a directory, write '.' and '..' entries pointing
    /// at itself.
    /// </summary>
    public static void InitRoot()
    {
        // Reserve inode 1 as root if not yet allocated
        if (InodesInUse() <= 1)
        {
            // First mount - allocate inode 1
            int allocated = Ialloc();
            if (allocated != ROOT_INO)
                throw new InvalidOperationException($"first ialloc returned {allocated}, expected {ROOT_INO}");
        }

        var root = Iget(ROOT_INO);
        if (root.IsFree)
        {
            Iinit(ROOT_INO, Inode.TYPE_DIR);
            // Write '.' and '..' entries
            var dot = new DirEntry { Ino = ROOT_INO, Name = "." };
            var dotdot = new DirEntry { Ino = ROOT_INO, Name = ".." };
            Writei(ROOT_INO, dot.ToBytes(), 0, DirEntry.RECORD_SIZE);
            Writei(ROOT_INO, dotdot.ToBytes(), DirEntry.RECORD_SIZE, DirEntry.RECORD_SIZE);
        }
    }

    /// <summary>
    /// Look up a name in a directory inode. Returns the inode number
    /// of the matching entry, or 0 if not found.
    /// </summary>
    public static int Lookup(int dirIno, string name)
    {
        var dir = Iget(dirIno);
        if (dir.Type != Inode.TYPE_DIR) return 0;
        if (dir.Size == 0) return 0;

        var buf = new byte[BLOCK_SIZE];
        int off = 0;
        while (off < dir.Size)
        {
            int n = Readi(dirIno, buf, off, BLOCK_SIZE);
            if (n == 0) break;
            for (int i = 0; i + DirEntry.RECORD_SIZE <= n; i += DirEntry.RECORD_SIZE)
            {
                var entry = DirEntry.FromBytes(buf, i);
                if (entry.Ino != 0 && entry.Name == name) return entry.Ino;
            }
            off += n;
        }
        return 0;
    }

    /// <summary>
    /// Add a name -> ino entry to a directory. Returns true if added,
    /// false if the name already exists.
    /// </summary>
    public static bool DirLink(int dirIno, string name, int ino)
    {
        if (string.IsNullOrEmpty(name) || name.Length > DirEntry.MAX_NAME)
            throw new ArgumentException("invalid name", nameof(name));
        if (Lookup(dirIno, name) != 0) return false;  // already exists

        var dir = Iget(dirIno);
        if (dir.Type != Inode.TYPE_DIR) throw new InvalidOperationException("not a directory");

        // Find first free slot, or append at the end
        var buf = new byte[BLOCK_SIZE];
        int off = 0;
        while (off < dir.Size)
        {
            int n = Readi(dirIno, buf, off, BLOCK_SIZE);
            if (n == 0) break;
            for (int i = 0; i + DirEntry.RECORD_SIZE <= n; i += DirEntry.RECORD_SIZE)
            {
                var entry = DirEntry.FromBytes(buf, i);
                if (entry.Ino == 0)
                {
                    // Free slot - rewrite
                    var newEntry = new DirEntry { Ino = (ushort)ino, Name = name };
                    Writei(dirIno, newEntry.ToBytes(), off + i, DirEntry.RECORD_SIZE);
                    return true;
                }
            }
            off += n;
        }

        // No free slot - append at end
        var append = new DirEntry { Ino = (ushort)ino, Name = name };
        Writei(dirIno, append.ToBytes(), dir.Size, DirEntry.RECORD_SIZE);
        return true;
    }

    /// <summary>
    /// Remove a name -> ino entry from a directory. Frees the slot
    /// by zeroing the ino field. Returns true if removed, false if
    /// the name was not found.
    /// </summary>
    public static bool DirUnlink(int dirIno, string name)
    {
        var dir = Iget(dirIno);
        if (dir.Type != Inode.TYPE_DIR) return false;

        var buf = new byte[BLOCK_SIZE];
        int off = 0;
        while (off < dir.Size)
        {
            int n = Readi(dirIno, buf, off, BLOCK_SIZE);
            if (n == 0) break;
            for (int i = 0; i + DirEntry.RECORD_SIZE <= n; i += DirEntry.RECORD_SIZE)
            {
                var entry = DirEntry.FromBytes(buf, i);
                if (entry.Ino != 0 && entry.Name == name)
                {
                    // Zero out the slot
                    var zero = new DirEntry { Ino = 0, Name = "" };
                    Writei(dirIno, zero.ToBytes(), off + i, DirEntry.RECORD_SIZE);
                    return true;
                }
            }
            off += n;
        }
        return false;
    }

    /// <summary>
    /// Enumerate the entries of a directory. Returns an array of
    /// (name, ino) pairs for non-free entries.
    /// </summary>
    public static (string name, int ino)[] Readdir(int dirIno)
    {
        var dir = Iget(dirIno);
        if (dir.Type != Inode.TYPE_DIR) return Array.Empty<(string, int)>();
        if (dir.Size == 0) return Array.Empty<(string, int)>();

        var result = new System.Collections.Generic.List<(string, int)>();
        var buf = new byte[BLOCK_SIZE];
        int off = 0;
        while (off < dir.Size)
        {
            int n = Readi(dirIno, buf, off, BLOCK_SIZE);
            if (n == 0) break;
            for (int i = 0; i + DirEntry.RECORD_SIZE <= n; i += DirEntry.RECORD_SIZE)
            {
                var entry = DirEntry.FromBytes(buf, i);
                if (entry.Ino != 0) result.Add((entry.Name, entry.Ino));
            }
            off += n;
        }
        return result.ToArray();
    }

    /// <summary>
    /// Walk a slash-separated path starting from the root. Returns the
    /// inode number of the final component, or 0 if any component is
    /// not found. An absolute path starting with '/' begins at ROOT_INO.
    /// </summary>
    public static int WalkPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        if (path[0] != '/') return 0;  // only absolute paths supported
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int current = ROOT_INO;
        foreach (var part in parts)
        {
            current = Lookup(current, part);
            if (current == 0) return 0;
        }
        return current;
    }

    /// <summary>
    /// Create a new file at the given absolute path. Parent directory
    /// must exist. Returns the new inode number or -1 on failure.
    /// </summary>
    public static int CreateFile(string path)
    {
        int slash = path.LastIndexOf('/');
        if (slash < 0) return -1;
        string parentPath = slash == 0 ? "/" : path.Substring(0, slash);
        string name = path.Substring(slash + 1);
        if (string.IsNullOrEmpty(name)) return -1;

        int parentIno = WalkPath(parentPath);
        if (parentIno == 0) return -1;
        var parent = Iget(parentIno);
        if (parent.Type != Inode.TYPE_DIR) return -1;

        int existing = Lookup(parentIno, name);
        if (existing != 0) return existing;  // already exists

        int newIno = Ialloc();
        if (newIno < 0) return -1;
        Iinit(newIno, Inode.TYPE_FILE);
        DirLink(parentIno, name, newIno);
        return newIno;
    }

    /// <summary>
    /// Create a new directory at the given absolute path.
    /// </summary>
    public static int CreateDir(string path)
    {
        int slash = path.LastIndexOf('/');
        if (slash < 0) return -1;
        string parentPath = slash == 0 ? "/" : path.Substring(0, slash);
        string name = path.Substring(slash + 1);
        if (string.IsNullOrEmpty(name)) return -1;

        int parentIno = WalkPath(parentPath);
        if (parentIno == 0) return -1;

        int newIno = Ialloc();
        if (newIno < 0) return -1;
        Iinit(newIno, Inode.TYPE_DIR);
        // Write '.' and '..'
        var dot = new DirEntry { Ino = (ushort)newIno, Name = "." };
        var dotdot = new DirEntry { Ino = (ushort)parentIno, Name = ".." };
        Writei(newIno, dot.ToBytes(), 0, DirEntry.RECORD_SIZE);
        Writei(newIno, dotdot.ToBytes(), DirEntry.RECORD_SIZE, DirEntry.RECORD_SIZE);
        DirLink(parentIno, name, newIno);
        return newIno;
    }

    /// <summary>
    /// Remove a file (not a directory) at the given absolute path.
    /// </summary>
    public static bool UnlinkFile(string path)
    {
        int slash = path.LastIndexOf('/');
        if (slash < 0) return false;
        string parentPath = slash == 0 ? "/" : path.Substring(0, slash);
        string name = path.Substring(slash + 1);
        int parentIno = WalkPath(parentPath);
        if (parentIno == 0) return false;

        int targetIno = Lookup(parentIno, name);
        if (targetIno == 0) return false;

        var target = Iget(targetIno);
        if (target.Type != Inode.TYPE_FILE) return false;

        DirUnlink(parentIno, name);
        Idestroy(targetIno);
        return true;
    }
}