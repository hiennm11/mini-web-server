using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// RAID levels supported by the M24 simulator.
/// We implement the four canonical levels (RAID 0 / 1 / 4 / 5) that show the
/// striping -> redundancy -> bottleneck -> fix progression in OSEP Ch. 38.
/// </summary>
public enum RaidLevel
{
    /// <summary>OSEP §38.3: block-level striping, no redundancy.</summary>
    Raid0,
    /// <summary>OSEP §38.4: full mirroring on 2 disks.</summary>
    Raid1,
    /// <summary>OSEP §38.7: block-level striping + dedicated parity disk.</summary>
    Raid4,
    /// <summary>OSEP §38.8: block-level striping + rotating parity.</summary>
    Raid5,
}

/// <summary>
/// Where a logical block lives physically in the RAID array.
/// </summary>
/// <param name="DiskIndex">Disk index in the array (0..DiskCount-1).</param>
/// <param name="BlockIndex">Block index within that disk (0..BlockCount-1).</param>
public readonly record struct RaidDiskBlock(int DiskIndex, int BlockIndex);

/// <summary>
/// RAID simulator (OSEP Ch. 38).
///
/// OSEP §38.1 "Interface":
///   "A RAID system ... presents to the host file system a clean interface:
///    a sequence of blocks that the file system reads or writes."
///
/// OSEP §38.2 "How To Make RAID Work - The Failure Model":
///   "We assume ... that any (and only) one of the N disks in the array may
///    fail at any given time. ... If the mean-time-to-failure (MTTF) of a
///    single disk is, say, 3 years, the MTTF of an array of 100 disks is
///    about 3 years / 100, or roughly 11 days."
///
/// OSEP §38.3 "RAID Level 0: Striping":
///   "The simplest RAID level. ... round-robin placement of blocks across
///    disks."
///
/// OSEP §38.4 "RAID Level 1: Mirroring":
///   "With mirroring, we make a copy of every block we write to disk. So,
///    each logical write becomes two physical writes."
///
/// OSEP §38.7 "RAID Level 4: Saving Space With Parity":
///   "In RAID 4, we have a dedicated parity disk. The parity block is
///    computed by XOR-ing all the corresponding data blocks in the stripe."
///
/// OSEP §38.8 "RAID Level 5: Rotating Parity":
///   "RAID 5 solves the small-write problem of RAID 4 by rotating the
///    parity block across all disks."
///
/// Our simulator models each block as one byte. The "stripes" are the rows;
/// the disk columns hold the bytes that share a stripe index.
/// </summary>
public sealed class Raid
{
    public readonly RaidLevel Level;
    public readonly int DiskCount;
    public readonly int BlockCount;

    private readonly byte[][] _disks;
    private int _failedDiskIndex = -1;

    public Raid(RaidLevel level, int diskCount, int blockCount)
    {
        if (diskCount < 2) throw new ArgumentOutOfRangeException(nameof(diskCount), "RAID requires >= 2 disks");
        if (blockCount < 1) throw new ArgumentOutOfRangeException(nameof(blockCount));
        if (level == RaidLevel.Raid1 && diskCount != 2) throw new ArgumentException("RAID 1 must have exactly 2 disks");
        if (level == RaidLevel.Raid4 && diskCount < 3) throw new ArgumentException("RAID 4 needs at least 3 disks (data disks + 1 parity)");
        if (level == RaidLevel.Raid5 && diskCount < 3) throw new ArgumentException("RAID 5 needs at least 3 disks");

        Level = level;
        DiskCount = diskCount;
        BlockCount = blockCount;

        _disks = new byte[diskCount][];
        for (int d = 0; d < diskCount; d++) _disks[d] = new byte[blockCount];
    }

    public bool HasFailedDisk => _failedDiskIndex >= 0;
    public int FailedDiskIndex => _failedDiskIndex;

    /// <summary>Direct byte read from disk/block (no recovery). Used by tests + layout printing.</summary>
    public byte DiskByte(int diskIndex, int blockIndex) => _disks[diskIndex][blockIndex];

    /// <summary>Mark a disk as failed. Subsequent Read()s use XOR recovery (RAID 4/5) or the surviving mirror (RAID 1). RAID 0 cannot recover.</summary>
    public void FailDisk(int diskIndex)
    {
        if (diskIndex < 0 || diskIndex >= DiskCount) throw new ArgumentOutOfRangeException(nameof(diskIndex));
        _failedDiskIndex = diskIndex;
    }

    /// <summary>Clear the failed-disk marker.</summary>
    public void ReviveDisk() => _failedDiskIndex = -1;

    // ----- RAID 0: block-level striping, no redundancy -----

    /// <summary>
    /// RAID 0 layout: logical block `b` lands on disk `b % N` at offset `b / N`.
    /// </summary>
    public RaidDiskBlock MapRaid0(int logicalBlock)
    {
        if (logicalBlock < 0 || logicalBlock >= DiskCount * BlockCount)
            throw new ArgumentOutOfRangeException(nameof(logicalBlock));
        return new RaidDiskBlock(logicalBlock % DiskCount, logicalBlock / DiskCount);
    }

    public void WriteRaid0(int logicalBlock, byte value)
    {
        var loc = MapRaid0(logicalBlock);
        _disks[loc.DiskIndex][loc.BlockIndex] = value;
    }

    public byte ReadRaid0(int logicalBlock)
    {
        var loc = MapRaid0(logicalBlock);
        if (_failedDiskIndex == loc.DiskIndex)
            throw new InvalidOperationException("RAID 0 cannot recover from disk failure");
        return _disks[loc.DiskIndex][loc.BlockIndex];
    }

    // ----- RAID 1: full mirroring on 2 disks -----

    /// <summary>
    /// RAID 1: each logical block lives on both disks. Disk 0 is the primary,
    /// disk 1 is the mirror.
    /// </summary>
    public void WriteRaid1(int logicalBlock, byte value)
    {
        if (logicalBlock < 0 || logicalBlock >= BlockCount)
            throw new ArgumentOutOfRangeException(nameof(logicalBlock));
        _disks[0][logicalBlock] = value;
        _disks[1][logicalBlock] = value;
    }

    public byte ReadRaid1(int logicalBlock)
    {
        if (logicalBlock < 0 || logicalBlock >= BlockCount)
            throw new ArgumentOutOfRangeException(nameof(logicalBlock));
        if (_failedDiskIndex == 0) return _disks[1][logicalBlock];
        if (_failedDiskIndex == 1) return _disks[0][logicalBlock];
        return _disks[0][logicalBlock];
    }

    // ----- RAID 4 / RAID 5: block-level striping + parity -----

    /// <summary>
    /// For RAID 4: parity disk is fixed at DiskCount - 1.
    /// For RAID 5: parity disk rotates - parity for stripe `s` lives on disk
    /// `(s + 1) % N` (OSEP §38.8 figure 38.8).
    /// </summary>
    public int ParityDiskFor(int stripe)
    {
        return Level switch
        {
            RaidLevel.Raid4 => DiskCount - 1,
            RaidLevel.Raid5 => (stripe + 1) % DiskCount,
            _ => throw new NotSupportedException("ParityDiskFor is for RAID 4/5 only"),
        };
    }

    /// <summary>
    /// Map a data-disk index (0..DiskCount-2) within stripe `s` to its physical
    /// disk, skipping the parity disk.
    /// </summary>
    public int DataDiskFor(int stripe, int dataIndex)
    {
        if (Level != RaidLevel.Raid4 && Level != RaidLevel.Raid5)
            throw new NotSupportedException("DataDiskFor is for RAID 4/5 only");
        int dataDisks = DiskCount - 1;
        if (dataIndex < 0 || dataIndex >= dataDisks)
            throw new ArgumentOutOfRangeException(nameof(dataIndex));
        int parityDisk = ParityDiskFor(stripe);
        int d = dataIndex;
        if (d >= parityDisk) d++;
        return d;
    }

    /// <summary>
    /// OSEP §38.7 "small write" path: write one data block + recompute parity
    /// (read the other data blocks, XOR with the new value, write the parity).
    /// </summary>
    public void WriteRaidParity(int stripe, int dataIndex, byte value)
    {
        if (Level != RaidLevel.Raid4 && Level != RaidLevel.Raid5)
            throw new NotSupportedException("WriteRaidParity is for RAID 4/5 only");
        if (stripe < 0 || stripe >= BlockCount) throw new ArgumentOutOfRangeException(nameof(stripe));

        int dataDisks = DiskCount - 1;
        if (dataIndex < 0 || dataIndex >= dataDisks) throw new ArgumentOutOfRangeException(nameof(dataIndex));

        // 1. Write the data block.
        int dataDisk = DataDiskFor(stripe, dataIndex);
        byte oldData = _disks[dataDisk][stripe];
        _disks[dataDisk][stripe] = value;

        // 2. Recompute parity: parity = parity XOR oldData XOR newData.
        //    (OSEP §38.7 "just XOR out the old and XOR in the new".)
        int parityDisk = ParityDiskFor(stripe);
        byte parity = _disks[parityDisk][stripe];
        parity = (byte)(parity ^ oldData ^ value);
        _disks[parityDisk][stripe] = parity;
    }

    /// <summary>
    /// OSEP §38.7 full-stripe write: pass N-1 data bytes, parity is XOR'd.
    /// </summary>
    public void WriteStripeRaidParity(int stripe, byte[] dataValues)
    {
        if (Level != RaidLevel.Raid4 && Level != RaidLevel.Raid5)
            throw new NotSupportedException("WriteStripeRaidParity is for RAID 4/5 only");
        int dataDisks = DiskCount - 1;
        if (dataValues.Length != dataDisks)
            throw new ArgumentException($"Stripe needs {dataDisks} data bytes, got {dataValues.Length}", nameof(dataValues));
        if (stripe < 0 || stripe >= BlockCount) throw new ArgumentOutOfRangeException(nameof(stripe));

        // Write data blocks.
        for (int i = 0; i < dataDisks; i++)
        {
            int dataDisk = DataDiskFor(stripe, i);
            _disks[dataDisk][stripe] = dataValues[i];
        }
        // Compute + write parity.
        byte parity = 0;
        for (int i = 0; i < dataDisks; i++) parity ^= dataValues[i];
        int parityDisk = ParityDiskFor(stripe);
        _disks[parityDisk][stripe] = parity;
    }

    /// <summary>
    /// Read any logical stripe. If a disk in the stripe failed, recover the
    /// lost block via XOR (OSEP §38.7 "fundamental insight").
    /// </summary>
    public byte ReadRaidParity(int stripe, int dataIndex)
    {
        if (Level != RaidLevel.Raid4 && Level != RaidLevel.Raid5)
            throw new NotSupportedException("ReadRaidParity is for RAID 4/5 only");
        if (stripe < 0 || stripe >= BlockCount) throw new ArgumentOutOfRangeException(nameof(stripe));

        int dataDisks = DiskCount - 1;
        if (dataIndex < 0 || dataIndex >= dataDisks) throw new ArgumentOutOfRangeException(nameof(dataIndex));

        int dataDisk = DataDiskFor(stripe, dataIndex);
        if (_failedDiskIndex < 0)
        {
            return _disks[dataDisk][stripe];
        }
        if (_failedDiskIndex != dataDisk)
        {
            // The failed disk isn't on the path of this read.
            return _disks[dataDisk][stripe];
        }
        // The dataDisk has failed. Recover via XOR of the surviving N-1 blocks in the stripe.
        byte recovered = 0;
        for (int d = 0; d < DiskCount; d++)
        {
            if (d == _failedDiskIndex) continue;
            recovered ^= _disks[d][stripe];
        }
        return recovered;
    }

    /// <summary>Read the parity block for stripe `s` directly (for layout/debug).</summary>
    public byte ReadParity(int stripe)
    {
        int parityDisk = ParityDiskFor(stripe);
        return _disks[parityDisk][stripe];
    }

    // ----- Formatting -----

    /// <summary>Layout summary as text (for the /raid/run route).</summary>
    public string FormatLayout()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== RAID Layout (M24 / OSEP Ch. 38) ===");
        sb.AppendLine($"level: {Level}   disks: {DiskCount}   blocks: {BlockCount}");
        if (_failedDiskIndex >= 0)
        {
            sb.AppendLine($"* failed disk: {_failedDiskIndex} (recovery via {(Level == RaidLevel.Raid1 ? "mirror" : "XOR")}{(Level == RaidLevel.Raid0 ? " - UNRECOVERABLE" : "")})");
        }

        switch (Level)
        {
            case RaidLevel.Raid0: FormatRaid0Layout(sb); break;
            case RaidLevel.Raid1: FormatRaid1Layout(sb); break;
            case RaidLevel.Raid4: FormatParityLayout(sb, rotating: false); break;
            case RaidLevel.Raid5: FormatParityLayout(sb, rotating: true); break;
        }
        return sb.ToString();
    }

    private void FormatRaid0Layout(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("disk | stripe -> (disk, block)");
        sb.AppendLine("-----|--------------------------------------------------");
        for (int d = 0; d < DiskCount; d++)
        {
            var cells = new List<string>();
            for (int b = 0; b < BlockCount; b++)
            {
                byte v = _disks[d][b];
                cells.Add(DisplayByte(v));
            }
            string marker = (_failedDiskIndex == d) ? " [DEAD]" : "";
            sb.AppendLine($"  {d}  | [{string.Join(" ", cells)}]{marker}");
        }
    }

    private void FormatRaid1Layout(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("disk | block values (mirror)");
        sb.AppendLine("-----|--------------------------------------------------");
        for (int d = 0; d < 2; d++)
        {
            var cells = new List<string>();
            for (int b = 0; b < BlockCount; b++) cells.Add(DisplayByte(_disks[d][b]));
            string marker = (_failedDiskIndex == d) ? " [DEAD]" : "";
            sb.AppendLine($"  {d}  | [{string.Join(" ", cells)}]{marker}");
        }
    }

    private void FormatParityLayout(StringBuilder sb, bool rotating)
    {
        sb.AppendLine();
        sb.AppendLine("disk | stripe rows (parity cell marked with [P])");
        sb.AppendLine("-----|--------------------------------------------------");
        // Print disk rows. Each disk d contains, for stripe s, the byte at row s
        // unless that row's parity disk equals d, in which case it's the parity.
        for (int d = 0; d < DiskCount; d++)
        {
            var cells = new List<string>();
            for (int s = 0; s < BlockCount; s++)
            {
                byte b = _disks[d][s];
                bool isParity = ParityDiskFor(s) == d;
                // For RAID 4 (rotating=false) only the bottom disk has parity; for
                // RAID 5 (rotating=true) the parity cell follows the rotating rule
                // -- but ParityDiskFor already encodes that. The "rotating" flag is
                // here only to vary the labelling.
                _ = rotating;
                cells.Add(isParity ? $"P{DisplayByte(b)}" : DisplayByte(b));
            }
            string marker = (_failedDiskIndex == d) ? " [DEAD]" : "";
            sb.AppendLine($"  {d}  | [{string.Join(" ", cells)}]{marker}");
        }
        if (rotating)
        {
            sb.AppendLine();
            sb.AppendLine("parity rotation (stripe -> parity disk):");
            for (int s = 0; s < BlockCount; s++)
                sb.AppendLine($"  stripe {s} -> disk {ParityDiskFor(s)}");
        }
    }

    private static string DisplayByte(byte b) =>
        (char)b >= ' ' && (char)b <= '~' ? ((char)b).ToString() : $"\\x{b:X2}";
}
