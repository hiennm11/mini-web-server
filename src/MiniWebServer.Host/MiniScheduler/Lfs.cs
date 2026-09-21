using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// LFS (Log-Structured File System) simulator (OSEP Ch. 43).
///
/// OSEP §43.1 "Writing To Disk Sequentially":
///   "An ideal file system would thus focus on write performance, and try
///    to make use of the sequential bandwidth of the disk."
///
/// OSEP §43.2 "Writing Sequentially And Effectively":
///   "Before writing to the disk, LFS keeps track of updates in memory;
///    when it has received a sufficient number of updates, it writes them
///    to disk all at once, thus ensuring efficient use of the disk. The
///    large chunk of updates LFS writes at one time is referred to by the
///    name of a **segment**."
///
/// OSEP §43.5 "Solution Through Indirection: The Inode Map":
///   "The imap is a structure that takes an inode number as input and
///    produces the disk address of the most recent version of the inode."
///
/// OSEP §43.6 "Completing The Solution: The Checkpoint Region":
///   "LFS has just such a fixed place on disk for this, known as the
///    **checkpoint region (CR)**. The checkpoint region contains pointers
///    to (i.e., addresses of) the latest pieces of the inode map."
///
/// OSEP §43.10 "Determining Block Liveness":
///   "LFS includes, for each data block D, its inode number (which file
///    it belongs to) and its offset (which block of the file this is).
///    This information is recorded in a structure at the head of the
///    segment known as the **segment summary block**."
///
/// OSEP §43.9 "A New Problem: Garbage Collection":
///   "LFS must periodically find these old dead versions of file data,
///    inodes, and other structures, and **clean** them; cleaning should
///    thus make blocks on disk free again for use in subsequent writes."
///
/// Our simulator:
///   - A disk of Segments x BlocksPerSegment. Segment 0 holds the
///     checkpoint region (CR); remaining segments hold flushed segments.
///   - In-memory Segment buffer accumulates LfsBlocks (data, inode, imap
///     chunks). When full, it flushes to the next free disk segment.
///   - The imap lives in memory as Dictionary&lt;int,int&gt; (inodeNum -&gt;
///     disk address). Pieces ride along in each segment flush.
///   - The CR lives at segment 0, block 0. After every flush we update
///     the CR with the latest imap piece address + log head.
///   - SegmentSummary[(seg, slot)] = (inodeNum, offset) per block.
///   - IsLive(seg, slot) compares the summary's (inode, offset) against
///     the current imap + inode's offset pointer.
///   - Clean() picks the coldest segment (fewest live blocks), copies
///     live blocks to a new segment, frees the old one.
/// </summary>
public sealed class Lfs
{
    public readonly int Segments;
    public readonly int BlocksPerSegment;

    /// <summary>Disk: [segment, slot] -&gt; LfsBlock. Segment 0 starts with the CR slot.</summary>
    private readonly LfsBlock[,] _disk;

    /// <summary>In-memory inode map (inodeNum -&gt; disk address as linear (seg*BlocksPerSegment + slot)).</summary>
    private readonly Dictionary<int, int> _imap = new();

    /// <summary>In-memory inode cache (inodeNum -&gt; LfsInode). Keyed by inode number.</summary>
    private readonly Dictionary<int, LfsInode> _inodes = new();

    /// <summary>Filename -&gt; inodeNum.</summary>
    private readonly Dictionary<string, int> _nameToInode = new(StringComparer.Ordinal);

    /// <summary>Per-segment summary: (seg, slot) -&gt; (inodeNum, offset). Stored separately for fast liveness checks.</summary>
    private readonly Dictionary<(int seg, int slot), (int inode, int offset)> _summary = new();

    /// <summary>In-memory segment being assembled. Flushed when full.</summary>
    private readonly List<LfsBlock> _buffer = new();

    /// <summary>Disk addresses of segments currently free.</summary>
    private readonly HashSet<int> _freeSegments = new();

    /// <summary>Pointer to the most recently flushed segment (log head).</summary>
    private int _logHeadSegment = -1;

    /// <summary>Next inode number to allocate.</summary>
    private int _nextInodeNum = 1;

    /// <summary>CR pointer: disk address of the latest imap piece.</summary>
    private int _latestImapDiskAddr = -1;

    public Lfs(int segments, int blocksPerSegment)
    {
        if (segments < 2) throw new ArgumentOutOfRangeException(nameof(segments), "LFS needs at least 2 segments (1 for CR + 1 for data)");
        if (blocksPerSegment < 4) throw new ArgumentOutOfRangeException(nameof(blocksPerSegment), "LFS needs at least 4 blocks/segment (1 summary + 1 imap + 1 inode + 1 data headroom)");

        Segments = segments;
        BlocksPerSegment = blocksPerSegment;
        _disk = new LfsBlock[segments, blocksPerSegment];
        for (int s = 0; s < segments; s++)
            for (int b = 0; b < blocksPerSegment; b++)
                _disk[s, b] = new LfsBlock(LfsBlockKind.Free, -1, -1, null);

        // Segment 0 holds the CR (slot 0). The rest of segment 0 is unused.
        _disk[0, 0] = new LfsBlock(LfsBlockKind.Checkpoint, -1, -1, Encoding.UTF8.GetBytes("CR:init"));

        // Free segments start at 1 (segment 0 is reserved for the CR).
        for (int s = 1; s < segments; s++) _freeSegments.Add(s);
    }

    /// <summary>Number of segments currently free (not holding live or dead blocks).</summary>
    public int FreeSegmentCount => _freeSegments.Count;

    /// <summary>Number of dead blocks across all flushed segments.</summary>
    public int DeadBlockCount
    {
        get
        {
            int dead = 0;
            for (int s = 1; s < Segments; s++)
            {
                if (_freeSegments.Contains(s)) continue;
                for (int b = 0; b < BlocksPerSegment; b++)
                {
                    if (_disk[s, b].Kind == LfsBlockKind.Free) continue;
                    if (!IsLive(s, b)) dead++;
                }
            }
            return dead;
        }
    }

    /// <summary>Total live blocks across all flushed segments.</summary>
    public int LiveBlockCount
    {
        get
        {
            int live = 0;
            for (int s = 1; s < Segments; s++)
            {
                if (_freeSegments.Contains(s)) continue;
                for (int b = 0; b < BlocksPerSegment; b++)
                {
                    if (_disk[s, b].Kind == LfsBlockKind.Free) continue;
                    if (IsLive(s, b)) live++;
                }
            }
            return live;
        }
    }

    // ----- Write path -----

    /// <summary>
    /// Create a new file. Returns the new inode number. Buffers an inode block
    /// in the in-memory segment.
    /// </summary>
    public int CreateFile(string name)
    {
        if (_nameToInode.ContainsKey(name)) throw new InvalidOperationException($"file already exists: {name}");
        int ino = _nextInodeNum++;
        var inode = new LfsInode(ino, name);
        _inodes[ino] = inode;
        _nameToInode[name] = ino;
        AppendToBuffer(new LfsBlock(LfsBlockKind.Inode, ino, -1, Encoding.UTF8.GetBytes($"inode:{name}")));
        // Flush an imap chunk when we have a few new inodes to record.
        if (_imap.Count % 4 == 1 || _buffer.Count >= BlocksPerSegment - 1) TryFlush();
        return ino;
    }

    /// <summary>
    /// Write a block of data to the file. Buffers a data block + an inode
    /// pointer update + an imap piece.
    /// </summary>
    public void WriteData(string name, int offset, byte value)
    {
        if (!_nameToInode.TryGetValue(name, out var ino)) throw new InvalidOperationException($"no such file: {name}");
        WriteData(ino, offset, value);
    }

    public void WriteData(int ino, int offset, byte value)
    {
        if (!_inodes.TryGetValue(ino, out var inode)) throw new InvalidOperationException($"no such inode: {ino}");
        inode.EnsureCapacity(offset);
        inode.Size = Math.Max(inode.Size, offset + 1);
        AppendToBuffer(new LfsBlock(LfsBlockKind.Data, ino, offset, new byte[] { value }));
        TryFlush();
    }

    /// <summary>Force the in-memory segment to disk, regardless of fill level.</summary>
    public void Flush() => TryFlush(force: true);

    // ----- Read path -----

    /// <summary>Read a block of data from the file via the imap + inode chain.</summary>
    public byte Read(string name, int offset)
    {
        if (!_nameToInode.TryGetValue(name, out var ino)) throw new InvalidOperationException($"no such file: {name}");
        return Read(ino, offset);
    }

    public byte Read(int ino, int offset)
    {
        if (!_inodes.TryGetValue(ino, out var inode)) throw new InvalidOperationException($"no such inode: {ino}");
        if (offset < 0 || offset >= inode.Size) throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset >= inode.DataAddresses.Length)
            throw new InvalidOperationException($"inode {ino} has no address for offset {offset}");
        var addr = inode.DataAddresses[offset];
        int seg = addr / BlocksPerSegment;
        int slot = addr % BlocksPerSegment;
        var value = _disk[seg, slot].Value ?? throw new InvalidOperationException("block has no value");
        return value[0];
    }

    // ----- Garbage collection -----

    /// <summary>
    /// Run the segment cleaner: pick the segment with fewest live blocks,
    /// compact its live blocks into a new segment, free the old one.
    /// Returns the count of freed segments (-1 = 0 since 1 new segment replaces
    /// 1 old segment, but only if the old segment was not entirely dead).
    /// </summary>
    public LfsCleanReport Clean()
    {
        int coldest = -1;
        int coldestLive = int.MaxValue;
        for (int s = 1; s < Segments; s++)
        {
            if (_freeSegments.Contains(s)) continue;
            int live = CountLive(s);
            if (live == 0)
            {
                // Entire segment is dead - free it directly, no compaction needed.
                FreeSegment(s);
                return new LfsCleanReport(s, 0, BlocksPerSegment, true);
            }
            if (live < coldestLive)
            {
                coldestLive = live;
                coldest = s;
            }
        }
        if (coldest < 0) return new LfsCleanReport(-1, 0, 0, false);  // nothing to clean

        // Collect live blocks from the coldest segment, in disk order.
        var liveBlocks = new List<LfsBlock>();
        for (int b = 0; b < BlocksPerSegment; b++)
        {
            var blk = _disk[coldest, b];
            if (blk.Kind == LfsBlockKind.Free) continue;
            if (IsLive(coldest, b))
            {
                // Make a copy so we can clear the old segment safely.
                liveBlocks.Add(blk with { });
            }
        }

        // Free the cold segment; we'll allocate a new one for the live blocks.
        FreeSegment(coldest);

        // Append the live blocks to the current buffer; flush if needed.
        foreach (var blk in liveBlocks)
        {
            AppendToBuffer(blk);
        }
        TryFlush(force: true);

        return new LfsCleanReport(coldest, coldestLive, BlocksPerSegment, false);
    }

    // ----- Helpers -----

    private void FreeSegment(int seg)
    {
        for (int b = 0; b < BlocksPerSegment; b++)
        {
            _disk[seg, b] = new LfsBlock(LfsBlockKind.Free, -1, -1, null);
            _summary.Remove((seg, b));
        }
        _freeSegments.Add(seg);
    }

    private int CountLive(int seg)
    {
        int live = 0;
        for (int b = 0; b < BlocksPerSegment; b++)
        {
            if (_disk[seg, b].Kind == LfsBlockKind.Free) continue;
            if (IsLive(seg, b)) live++;
        }
        return live;
    }

    /// <summary>
    /// OSEP §43.10 liveness check: compare the summary's (inode, offset)
    /// against the current imap + inode. Inode and imap blocks themselves are
    /// live iff their inode is still in the imap.
    /// </summary>
    public bool IsLive(int seg, int slot)
    {
        if (!_summary.TryGetValue((seg, slot), out var so)) return false;
        if (!_imap.TryGetValue(so.inode, out var inodeAddr)) return false;
        if (!_inodes.TryGetValue(so.inode, out var inode)) return false;
        // Inode/Imap blocks have offset = -1; they're live iff the imap points here.
        if (so.offset < 0)
        {
            // This is an inode or imap block. Live iff the imap points to it.
            int actualAddr = (seg * BlocksPerSegment) + slot;
            return inodeAddr == actualAddr;
        }
        if (so.offset >= inode.DataAddresses.Length) return false;
        int expectedAddr = inode.DataAddresses[so.offset];
        int thisAddr = (seg * BlocksPerSegment) + slot;
        return expectedAddr == thisAddr;
    }

    private void AppendToBuffer(LfsBlock block)
    {
        _buffer.Add(block);
        // Flush eagerly if we're close to filling the segment.
        if (_buffer.Count >= BlocksPerSegment - 1) TryFlush();
    }

    private void TryFlush(bool force = false)
    {
        if (_buffer.Count == 0) return;
        if (!force && _buffer.Count < BlocksPerSegment / 2) return;

        // Pick the lowest free segment.
        if (_freeSegments.Count == 0) throw new InvalidOperationException("disk full - run cleaner first");
        int targetSeg = _freeSegments.OrderBy(s => s).First();
        _freeSegments.Remove(targetSeg);

        // Block 0 of each flushed segment holds the segment summary.
        var summaryEntries = new List<LfsBlock>();
        for (int b = 0; b < BlocksPerSegment; b++)
        {
            _disk[targetSeg, b] = new LfsBlock(LfsBlockKind.Free, -1, -1, null);
            _summary.Remove((targetSeg, b));
        }

        int slot = 1;  // slot 0 reserved for summary
        for (int i = 0; i < _buffer.Count && slot < BlocksPerSegment; i++, slot++)
        {
            var blk = _buffer[i];
            _disk[targetSeg, slot] = blk;
            int addr = targetSeg * BlocksPerSegment + slot;
            _summary[(targetSeg, slot)] = (blk.InodeNum, blk.Offset);

            // Side effects on disk write:
            switch (blk.Kind)
            {
                case LfsBlockKind.Data:
                {
                    if (!_inodes.TryGetValue(blk.InodeNum, out var inode)) break;
                    inode.DataAddresses[blk.Offset] = addr;
                    break;
                }
                case LfsBlockKind.Inode:
                {
                    // The imap maps inode -> the address where the inode lives.
                    _imap[blk.InodeNum] = addr;
                    break;
                }
                case LfsBlockKind.Imap:
                {
                    _latestImapDiskAddr = addr;
                    break;
                }
            }
        }

        // Slot 0: segment summary block. Stores (count) followed by (inodeNum, offset) pairs.
        // We model the count + pairs inside the block's value bytes (first byte = count).
        var summaryValue = new byte[] { (byte)_buffer.Count };
        _disk[targetSeg, 0] = new LfsBlock(LfsBlockKind.Summary, -1, -1, summaryValue);

        // Update CR pointer.
        _latestImapDiskAddr = targetSeg * BlocksPerSegment + 0;
        _disk[0, 0] = new LfsBlock(LfsBlockKind.Checkpoint, -1, -1,
            Encoding.UTF8.GetBytes($"imap_piece_at_seg={targetSeg},log_head_seg={_logHeadSegment}"));

        _logHeadSegment = targetSeg;
        _buffer.Clear();
    }

    /// <summary>Pretty-print the disk layout for the /lfs/run route.</summary>
    public string FormatLayout()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== LFS Layout (M25 / OSEP Ch. 43) ===");
        sb.AppendLine($"disk: {Segments} segments x {BlocksPerSegment} blocks/seg");
        sb.AppendLine($"free segments: {FreeSegmentCount}   live blocks: {LiveBlockCount}   dead blocks: {DeadBlockCount}");
        sb.AppendLine($"log head segment: {_logHeadSegment}   latest imap addr: {_latestImapDiskAddr}");

        sb.AppendLine();
        sb.AppendLine("imap (inode -> seg/slot):");
        foreach (var kv in _imap.OrderBy(kv => kv.Key))
        {
            int seg = kv.Value / BlocksPerSegment;
            int slot = kv.Value % BlocksPerSegment;
            sb.AppendLine($"  inode {kv.Key} -> seg {seg} slot {slot}");
        }

        sb.AppendLine();
        sb.AppendLine("files:");
        foreach (var kv in _nameToInode.OrderBy(kv => kv.Key))
        {
            if (!_inodes.TryGetValue(kv.Value, out var inode)) continue;
            sb.AppendLine($"  {kv.Key} (inode {inode.InodeNum}, size={inode.Size})");
            for (int b = 0; b < inode.Size; b++)
            {
                int addr = inode.DataAddresses[b];
                int seg = addr / BlocksPerSegment;
                int slot = addr % BlocksPerSegment;
                var valArr = _disk[seg, slot].Value ?? new byte[] { 0 };
                byte v = valArr[0];
                sb.AppendLine($"    offset {b} -> seg {seg} slot {slot} = '{DisplayByte(v)}'");
            }
        }

        sb.AppendLine();
        sb.AppendLine("segment grid (D=data I=inode P=imap S=summary C=CR . = free, [dead] = obsolete):");
        sb.AppendLine("seg  | blocks");
        sb.AppendLine("-----|--------------------------------------------------");
        for (int s = 0; s < Segments; s++)
        {
            var cells = new List<string>();
            for (int b = 0; b < BlocksPerSegment; b++)
            {
                var blk = _disk[s, b];
                char label = blk.Kind switch
                {
                    LfsBlockKind.Free => '.',
                    LfsBlockKind.Data => 'D',
                    LfsBlockKind.Inode => 'I',
                    LfsBlockKind.Imap => 'P',
                    LfsBlockKind.Summary => 'S',
                    LfsBlockKind.Checkpoint => 'C',
                    _ => '?',
                };
                if (s > 0 && label != '.' && label != 'S' && label != 'C' && !IsLive(s, b))
                    cells.Add($"{label}*");   // dead
                else
                    cells.Add(label.ToString());
            }
            string status = s == 0 ? " (CR slot)" : (_freeSegments.Contains(s) ? " [FREE]" : "");
            sb.AppendLine($"  {s,-2} | [{string.Join(" ", cells)}]{status}");
        }
        return sb.ToString();
    }

    private static string DisplayByte(byte b) =>
        (char)b >= ' ' && (char)b <= '~' ? ((char)b).ToString() : $"\\x{b:X2}";
}

/// <summary>One block on the LFS disk. OSEP §43.10 segment-summary entry.</summary>
public sealed record LfsBlock(LfsBlockKind Kind, int InodeNum, int Offset, byte[]? Value)
{
    public string DisplayValue => Value switch
    {
        null => ".",
        byte[] { Length: 1 } => Value[0] >= ' ' && Value[0] <= '~' ? ((char)Value[0]).ToString() : $"\\x{Value[0]:X2}",
        _ => Encoding.UTF8.GetString(Value),
    };
}

/// <summary>Internal representation. LfsInode stores the array of data addresses by offset.</summary>
public sealed class LfsInode
{
    public readonly int InodeNum;
    public readonly string Name;
    public int Size;
    /// <summary>Data addresses indexed by block offset. Grown as the file grows.</summary>
    public int[] DataAddresses;

    public LfsInode(int inodeNum, string name)
    {
        InodeNum = inodeNum;
        Name = name;
        Size = 0;
        DataAddresses = new int[8];
        for (int i = 0; i < DataAddresses.Length; i++) DataAddresses[i] = -1;
    }

    public void EnsureCapacity(int requiredOffset)
    {
        if (requiredOffset < DataAddresses.Length) return;
        int newSize = DataAddresses.Length * 2;
        while (newSize <= requiredOffset) newSize *= 2;
        var newArr = new int[newSize];
        Array.Copy(DataAddresses, newArr, DataAddresses.Length);
        for (int i = DataAddresses.Length; i < newArr.Length; i++) newArr[i] = -1;
        DataAddresses = newArr;
    }
}

public enum LfsBlockKind { Free, Data, Inode, Imap, Summary, Checkpoint }

public sealed record LfsCleanReport(int CleanedSegment, int LiveBlocksCompacted, int TotalBlocks, bool FreedDirectly);
