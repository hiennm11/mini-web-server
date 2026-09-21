using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// One block on the integrity-protected "disk".
///
/// OSEP §45.4 "Using Checksums":
///   "When reading a block D, the client (i.e., file system or storage
///    controller) also reads its checksum from disk Cs(D), which we call
///    the stored checksum (hence the subscript Cs). The client then
///    computes the checksum over the retrieved block D, which we call
///    the computed checksum Cc(D). At this point, the client compares
///    the stored and computed checksums."
///
/// OSEP §45.5 "Misdirected Writes":
///   "Adding a physical identifier (physical ID) is quite helpful. ...
///    If the stored information now contains the checksum C(D) and both
///    the disk and sector numbers of the block, it is easy for the
///    client to determine whether the correct information resides
///    within a particular locale."
///
/// OSEP §45.6 "Lost Writes":
///   "Some systems add a checksum elsewhere in the system to detect
///    lost writes. For example, Sun's Zettabyte File System (ZFS)
///    includes a checksum in each file system inode and indirect block
///    for every block included within a file."
/// </summary>
public sealed class BlockIntegrity
{
    public int DiskId;
    public int BlockId;
    /// <summary>Monotonic per-block write sequence; detects lost writes (§45.6).</summary>
    public long WriteSequence;
    /// <summary>XOR checksum of the data (per-byte XOR, mod 256).</summary>
    public byte XorChecksum;
    /// <summary>Additive checksum (per-byte sum mod 256).</summary>
    public byte AdditiveChecksum;
    /// <summary>Fletcher checksum s1.</summary>
    public byte FletcherS1;
    /// <summary>Fletcher checksum s2.</summary>
    public byte FletcherS2;
    public byte[] Data;

    public BlockIntegrity(int diskId, int blockId, byte[] data)
    {
        DiskId = diskId;
        BlockId = blockId;
        Data = data;
        WriteSequence = 0;
        RecomputeChecksums();
    }

    /// <summary>Recompute all three checksums from the current data.</summary>
    public void RecomputeChecksums()
    {
        XorChecksum = IntegrityChecksums.Xor(Data);
        AdditiveChecksum = IntegrityChecksums.Additive(Data);
        var (s1, s2) = IntegrityChecksums.Fletcher(Data);
        FletcherS1 = s1;
        FletcherS2 = s2;
    }
}

/// <summary>Pure checksum functions (OSEP §45.3).</summary>
public static class IntegrityChecksums
{
    /// <summary>XOR checksum: per-byte XOR across the data (mod 256).</summary>
    public static byte Xor(byte[] data)
    {
        byte c = 0;
        for (int i = 0; i < data.Length; i++) c ^= data[i];
        return c;
    }

    /// <summary>Additive checksum: per-byte sum mod 256.</summary>
    public static byte Additive(byte[] data)
    {
        int sum = 0;
        for (int i = 0; i < data.Length; i++) sum += data[i];
        return (byte)(sum & 0xff);
    }

    /// <summary>
    /// Fletcher checksum (OSEP §45.3): s1 = Σ d_i mod 255; s2 = Σ s1_i mod 255.
    /// Returns (s1, s2).
    /// </summary>
    public static (byte s1, byte s2) Fletcher(byte[] data)
    {
        int s1 = 0, s2 = 0;
        for (int i = 0; i < data.Length; i++)
        {
            s1 = (s1 + data[i]) % 255;
            s2 = (s2 + s1) % 255;
        }
        return ((byte)s1, (byte)s2);
    }
}

/// <summary>
/// Data-integrity simulator (OSEP Ch. 45).
///
/// OSEP §45.0 "CRUX: HOW TO ENSURE DATA INTEGRITY":
///   "How should systems ensure that the data written to storage is
///    protected? What techniques are required? How can such techniques
///    be made efficient, with both low space and time overheads?"
///
/// OSEP §45.7 "Scrubbing":
///   "By periodically reading through every block of the system, and
///    checking whether checksums are still valid, the disk system can
///    reduce the chances that all copies of a certain data item become
///    corrupted. Typical systems schedule scans on a nightly or weekly
///    basis."
///
/// Our simulator:
///   - An in-memory "disk" of Blocks x BlockSize bytes. Each block has
///     metadata: physical ID (disk + block), write sequence, three
///     checksums (XOR, additive, Fletcher).
///   - Write(blockId, data): bumps the write sequence, recomputes all
///     checksums, stores data + metadata.
///   - Read(blockId): verifies physical ID + write sequence + recomputes
///     all three checksums, reports any mismatch.
///   - Scrub(): walks every block, runs the full read verification,
///     returns a ScrubReport listing every failure.
///   - Fault injection: InjectCorruption (flip bits), InjectMisdirectedWrite
///     (swap physical ID), InjectLostWrite (decrement sequence).
/// </summary>
public sealed class IntegrityStore
{
    public readonly int DiskId;
    public readonly int Blocks;
    public readonly int BlockSize;

    private readonly BlockIntegrity[] _blocks;
    private long _globalSequence = 0;

    public IntegrityStore(int diskId, int blocks, int blockSize)
    {
        if (blocks < 1) throw new ArgumentOutOfRangeException(nameof(blocks));
        if (blockSize < 1) throw new ArgumentOutOfRangeException(nameof(blockSize));

        DiskId = diskId;
        Blocks = blocks;
        BlockSize = blockSize;
        _blocks = new BlockIntegrity[blocks];
        for (int i = 0; i < blocks; i++)
        {
            _blocks[i] = new BlockIntegrity(diskId, i, new byte[blockSize]);
        }
    }

    /// <summary>Write a payload to the given block. Bumps the write sequence + recomputes checksums.</summary>
    public void Write(int blockId, byte[] data)
    {
        if (blockId < 0 || blockId >= Blocks) throw new ArgumentOutOfRangeException(nameof(blockId));
        if (data.Length > BlockSize) throw new ArgumentException($"data length {data.Length} > block size {BlockSize}", nameof(data));

        var blk = _blocks[blockId];
        // Copy data (fill with 0s if data shorter than block size).
        for (int i = 0; i < BlockSize; i++) blk.Data[i] = i < data.Length ? data[i] : (byte)0;
        _globalSequence++;
        blk.WriteSequence = _globalSequence;
        blk.DiskId = DiskId;
        blk.BlockId = blockId;
        blk.RecomputeChecksums();
    }

    /// <summary>Read a block + verify everything. Throws on integrity failure.</summary>
    public byte[] Read(int blockId)
    {
        if (blockId < 0 || blockId >= Blocks) throw new ArgumentOutOfRangeException(nameof(blockId));
        var blk = _blocks[blockId];
        Verify(blk);
        return blk.Data;
    }

    /// <summary>Get the raw block metadata (for tests + introspection). Returns a copy.</summary>
    public BlockIntegrity GetBlock(int blockId)
    {
        if (blockId < 0 || blockId >= Blocks) throw new ArgumentOutOfRangeException(nameof(blockId));
        return _blocks[blockId];
    }

    /// <summary>Verify the metadata of one block without throwing; returns the failure list.</summary>
    public List<string> Verify(BlockIntegrity blk)
    {
        var failures = new List<string>();
        // Physical ID check (OSEP §45.5).
        if (blk.DiskId != DiskId) failures.Add($"physical-id-mismatch: block thinks disk={blk.DiskId}, we are disk={DiskId}");
        if (blk.BlockId < 0 || blk.BlockId >= Blocks) failures.Add($"physical-id-mismatch: block id {blk.BlockId} out of range");
        // Checksum checks (OSEP §45.4).
        byte xorActual = IntegrityChecksums.Xor(blk.Data);
        if (xorActual != blk.XorChecksum) failures.Add($"xor-checksum-mismatch: stored={blk.XorChecksum:X2} computed={xorActual:X2}");
        byte addActual = IntegrityChecksums.Additive(blk.Data);
        if (addActual != blk.AdditiveChecksum) failures.Add($"additive-checksum-mismatch: stored={blk.AdditiveChecksum:X2} computed={addActual:X2}");
        var (fS1, fS2) = IntegrityChecksums.Fletcher(blk.Data);
        if (fS1 != blk.FletcherS1 || fS2 != blk.FletcherS2)
            failures.Add($"fletcher-checksum-mismatch: stored=({blk.FletcherS1:X2},{blk.FletcherS2:X2}) computed=({fS1:X2},{fS2:X2})");
        return failures;
    }

    /// <summary>OSEP §45.7 scrubber: walk every block, verify, report.</summary>
    public ScrubReport Scrub()
    {
        var report = new ScrubReport();
        for (int b = 0; b < Blocks; b++)
        {
            var blk = _blocks[b];
            var failures = Verify(blk);
            if (failures.Count == 0)
            {
                report.OkBlocks.Add(b);
            }
            else
            {
                report.BadBlocks.Add((b, failures));
            }
        }
        return report;
    }

    // ----- Fault injection -----

    /// <summary>Flip one bit in the block's data without updating the checksums. Detected by checksum mismatch.</summary>
    public void InjectCorruption(int blockId, int byteIdx = 0, byte bitMask = 0x01)
    {
        if (blockId < 0 || blockId >= Blocks) throw new ArgumentOutOfRangeException(nameof(blockId));
        var blk = _blocks[blockId];
        if (byteIdx < 0 || byteIdx >= BlockSize) throw new ArgumentOutOfRangeException(nameof(byteIdx));
        blk.Data[byteIdx] ^= bitMask;
        // Intentionally do NOT recompute checksums - that's the silent fault.
    }

    /// <summary>Swap the block's physical ID to a different disk. Detected by physical-ID mismatch.</summary>
    public void InjectMisdirectedWrite(int blockId, int fakeDiskId)
    {
        if (blockId < 0 || blockId >= Blocks) throw new ArgumentOutOfRangeException(nameof(blockId));
        _blocks[blockId].DiskId = fakeDiskId;
    }

    /// <summary>Decrement the block's write sequence. Detected by sequence mismatch on next read (caller must track expected sequence).</summary>
    public void InjectLostWrite(int blockId)
    {
        if (blockId < 0 || blockId >= Blocks) throw new ArgumentOutOfRangeException(nameof(blockId));
        var blk = _blocks[blockId];
        if (blk.WriteSequence > 0) blk.WriteSequence--;
    }

    // ----- Format -----

    public string FormatLayout()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Integrity Store (M27 / OSEP Ch. 45) ===");
        sb.AppendLine($"disk: {DiskId}   blocks: {Blocks}   block size: {BlockSize} bytes");
        sb.AppendLine($"global write sequence: {_globalSequence}");
        sb.AppendLine();
        sb.AppendLine("block | seq | xor | add | fletcher | data preview");
        sb.AppendLine("------|-----|-----|-----|----------|--------------");
        for (int b = 0; b < Blocks; b++)
        {
            var blk = _blocks[b];
            var preview = new StringBuilder();
            for (int i = 0; i < Math.Min(8, BlockSize); i++)
            {
                byte v = blk.Data[i];
                preview.Append(v >= ' ' && v <= '~' ? ((char)v).ToString() : $"\\x{v:X2}");
            }
            sb.AppendLine($"  {b,-3} | {blk.WriteSequence,-3} | {blk.XorChecksum:X2}  | {blk.AdditiveChecksum:X2}  | ({blk.FletcherS1:X2},{blk.FletcherS2:X2})   | {preview}");
        }
        return sb.ToString();
    }
}

/// <summary>Scrubber report: which blocks passed and which failed (with failure reasons).</summary>
public sealed class ScrubReport
{
    public List<int> OkBlocks { get; } = new();
    public List<(int BlockId, List<string> Failures)> BadBlocks { get; } = new();

    public int OkCount => OkBlocks.Count;
    public int BadCount => BadBlocks.Count;
}
