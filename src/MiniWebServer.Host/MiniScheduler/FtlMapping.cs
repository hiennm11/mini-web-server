using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// Mapping-table memory for an SSD of a given capacity (OSEP §44.9).
/// </summary>
/// <remarks>
/// OSTEP §44.9, verbatim:
/// "The second cost of log-structuring is the potential for extremely large
/// mapping tables, with one entry for each 4-KB page of the device. With a
/// large 1-TB SSD, for example, a single 4-byte entry per 4-KB page results in
/// 1 GB of memory needed by the device, just for these mappings! Thus, this
/// page-level FTL scheme is impractical."
///
/// Block-based mapping "reduce[s] the amount of mapping information by a
/// factor of Size_block / Size_page".
///
/// This type is pure arithmetic over the capacity numbers; it allocates
/// nothing and holds no flash state, so the worked figures can be checked
/// without building a 1 TB device.
/// </remarks>
public static class FtlMappingCost
{
    /// <summary>
    /// Bytes of mapping memory for one capacity under each FTL strategy.
    /// </summary>
    public sealed record FtlTableBytes(long PageLevelBytes, long BlockLevelBytes, long HybridBytes);

    /// <summary>
    /// Compute the mapping-table size for all three strategies.
    /// </summary>
    /// <param name="capacityBytes">Usable device capacity, e.g. 1 TB.</param>
    /// <param name="pageBytes">Flash page size. OSEP uses 4 KB.</param>
    /// <param name="blockBytes">Flash block size. OSEP notes real blocks "can be 256KB or larger".</param>
    /// <param name="entryBytes">Bytes per mapping-table entry.</param>
    /// <param name="logBlocks">
    /// Log blocks reserved by a hybrid FTL. Each contributes per-page entries
    /// to the log table. Defaults to 0 (pure block-level sizing).
    /// </param>
    public static FtlTableBytes TableBytes(
        long capacityBytes,
        long pageBytes,
        long blockBytes,
        int entryBytes = 4,
        int logBlocks = 0)
    {
        if (capacityBytes <= 0) throw new ArgumentOutOfRangeException(nameof(capacityBytes), "capacity must be positive");
        if (pageBytes <= 0) throw new ArgumentOutOfRangeException(nameof(pageBytes), "page size must be positive");
        if (blockBytes <= 0) throw new ArgumentOutOfRangeException(nameof(blockBytes), "block size must be positive");
        if (blockBytes < pageBytes)
            throw new ArgumentOutOfRangeException(nameof(blockBytes), "a flash block holds at least one page, so it cannot be smaller than a page");
        if (entryBytes <= 0) throw new ArgumentOutOfRangeException(nameof(entryBytes), "entry size must be positive");
        if (logBlocks < 0) throw new ArgumentOutOfRangeException(nameof(logBlocks), "log block count cannot be negative");

        long pages = CeilDiv(capacityBytes, pageBytes);
        long blocks = CeilDiv(capacityBytes, blockBytes);

        long pageLevel = Multiply(pages, entryBytes);
        long blockLevel = Multiply(blocks, entryBytes);

        // Hybrid (§44.9): "a small set of per-page mappings in what we'll call
        // the log table, and a larger set of per-block mappings in the data
        // table". The log table is the only per-page memory the strategy adds:
        // one entry per page of every reserved log block.
        long logPages = Multiply(CeilDiv(SaturatingMul(logBlocks, blockBytes), pageBytes), entryBytes);
        long hybrid = Add(blockLevel, logPages);
        return new FtlTableBytes(pageLevel, blockLevel, hybrid);
    }

    /// <summary>
    /// Ceiling division that cannot overflow. <c>(a + b - 1) / b</c> wraps to a
    /// negative number once <c>a</c> approaches <see cref="long.MaxValue"/>, and a
    /// negative count then slips past the saturation helpers below.
    /// </summary>
    private static long CeilDiv(long a, long b) => a / b + (a % b == 0 ? 0 : 1);

    /// <summary>Multiply, saturating rather than wrapping to a negative number.</summary>
    private static long Multiply(long count, int entryBytes) =>
        count <= 0 ? 0
        : count > long.MaxValue / entryBytes ? long.MaxValue
        : count * entryBytes;

    /// <summary>Multiply two longs, saturating rather than wrapping.</summary>
    private static long SaturatingMul(long a, long b) =>
        a <= 0 || b <= 0 ? 0
        : a > long.MaxValue / b ? long.MaxValue
        : a * b;

    /// <summary>Add, saturating rather than wrapping.</summary>
    private static long Add(long a, long b) =>
        a >= long.MaxValue - b ? long.MaxValue : a + b;
}

/// <summary>
/// Flash as the FTL sees it: read / program a page, erase a block (OSEP §44.3).
/// </summary>
/// <remarks>
/// M26's <see cref="Ssd"/> keeps its page state and its own log cursor because it
/// *is* a page-level FTL. This class is the bare media underneath the M32
/// strategies, so all three see the same erase-program rules and the same wear
/// accounting and differ only in how they translate addresses.
/// </remarks>
public sealed class RawFlash
{
    public readonly int Blocks;
    public readonly int PagesPerBlock;
    public int TotalPages => Blocks * PagesPerBlock;

    private readonly SsdPageState[] _states;
    private readonly byte[] _values;
    private readonly int[] _eraseCount;

    /// <summary>Pages programmed since construction (OSEP §44.8 write amplification numerator).</summary>
    public long PagesProgrammed { get; private set; }

    /// <summary>Pages read since construction (GC / read-modify-write cost).</summary>
    public long PagesRead { get; private set; }

    public RawFlash(int blocks, int pagesPerBlock)
    {
        if (blocks < 1) throw new ArgumentOutOfRangeException(nameof(blocks), "a device needs at least one block");
        if (pagesPerBlock < 1) throw new ArgumentOutOfRangeException(nameof(pagesPerBlock), "a block needs at least one page");

        Blocks = blocks;
        PagesPerBlock = pagesPerBlock;
        _states = new SsdPageState[TotalPages];
        _values = new byte[TotalPages];
        _eraseCount = new int[blocks];
        for (int p = 0; p < TotalPages; p++) _states[p] = SsdPageState.Erased;
    }

    public SsdPageState StateOf(int pageIdx)
    {
        if (pageIdx < 0 || pageIdx >= TotalPages) throw new ArgumentOutOfRangeException(nameof(pageIdx));
        return _states[pageIdx];
    }

    public byte ReadPage(int pageIdx)
    {
        if (pageIdx < 0 || pageIdx >= TotalPages) throw new ArgumentOutOfRangeException(nameof(pageIdx));
        if (_states[pageIdx] != SsdPageState.Valid)
            throw new InvalidOperationException($"page {pageIdx} is in state {_states[pageIdx]}; only a Valid page can be read");
        PagesRead++;
        return _values[pageIdx];
    }

    /// <summary>
    /// Program a page (OSEP §44.3). Flash pages cannot be overwritten in place,
    /// so the target must be Erased; a caller overwriting live data has to erase
    /// the whole containing block first.
    /// </summary>
    public void ProgramPage(int pageIdx, byte value)
    {
        if (pageIdx < 0 || pageIdx >= TotalPages) throw new ArgumentOutOfRangeException(nameof(pageIdx));
        if (_states[pageIdx] != SsdPageState.Erased)
            throw new InvalidOperationException($"page {pageIdx} is in state {_states[pageIdx]}; must be Erased before program");
        _states[pageIdx] = SsdPageState.Valid;
        _values[pageIdx] = value;
        PagesProgrammed++;
    }

    /// <summary>Erase a whole block (OSEP §44.3) and bump its wear counter (§44.10).</summary>
    public void EraseBlock(int blockId)
    {
        if (blockId < 0 || blockId >= Blocks) throw new ArgumentOutOfRangeException(nameof(blockId));
        _eraseCount[blockId]++;
        for (int p = 0; p < PagesPerBlock; p++)
        {
            _states[blockId * PagesPerBlock + p] = SsdPageState.Erased;
            _values[blockId * PagesPerBlock + p] = 0;
        }
    }

    public int EraseCount(int blockId) => _eraseCount[blockId];

    public bool IsBlockErased(int blockId)
    {
        for (int p = 0; p < PagesPerBlock; p++)
            if (_states[blockId * PagesPerBlock + p] != SsdPageState.Erased) return false;
        return true;
    }

    public void MarkDead(int pageIdx)
    {
        if (pageIdx < 0 || pageIdx >= TotalPages) throw new ArgumentOutOfRangeException(nameof(pageIdx));
        if (_states[pageIdx] == SsdPageState.Valid) _states[pageIdx] = SsdPageState.Dead;
    }
}

/// <summary>
/// What every M32 FTL exposes, so the comparison route can drive them uniformly.
/// </summary>
public interface IFtlStrategy
{
    string Name { get; }
    void Write(int lba, byte value);
    byte Read(int lba);
    /// <summary>Number of mapping-table entries currently held in memory.</summary>
    int MappingEntries { get; }
    long HostBytesWritten { get; }
    long DataBytesWritten { get; }
    double WriteAmplification => (double)DataBytesWritten / HostBytesWritten;
    /// <summary>Fold outstanding log state into the data blocks. No-op for page-level.</summary>
    void MergeLogBlocks();
}

/// <summary>
/// Shared machinery: allocation of erased space, the I/O tallies, and the
/// workload driver used by the comparison route and the tests.
/// </summary>
public abstract class FtlBase : IFtlStrategy
{
    protected readonly RawFlash Flash;
    private readonly Stack<int> _freePool = new();

    /// <summary>Pages the client asked us to write.</summary>
    public long HostBytesWritten { get; protected set; }

    public abstract string Name { get; }
    public abstract int MappingEntries { get; }

    public long DataBytesWritten => Flash.PagesProgrammed;

    public long PagesReadFromFlash => Flash.PagesRead;

    protected FtlBase(int blocks, int pagesPerBlock)
    {
        Flash = new RawFlash(blocks, pagesPerBlock);
        // Every block starts out erased, so every block starts out allocatable.
        for (int i = 0; i < blocks; i++) _freePool.Push(i);
    }

    protected int Blocks => Flash.Blocks;
    protected int PagesPerBlock => Flash.PagesPerBlock;

    protected int TakeFreeBlock()
    {
        if (_freePool.Count == 0) throw new InvalidOperationException("no erased block free - the device is full");
        return _freePool.Pop();
    }

    protected void ReturnBlock(int blockId)
    {
        if (blockId < 0 || blockId >= Flash.Blocks) throw new ArgumentOutOfRangeException(nameof(blockId));
        _freePool.Push(blockId);
    }

    public abstract void MergeLogBlocks();

    public abstract byte Read(int lba);

    public abstract void Write(int lba, byte value);

    /// <summary>
    /// Drive a workload and return what each FTL cost. Used by the
    /// comparison route so the three strategies face identical traffic.
    /// </summary>
    /// <param name="writes">Number of client writes.</param>
    /// <param name="workload">
    /// "seq" walks logical addresses in order, which is the shape a switch
    /// merge is built for; "random" scatters them, which is what forces
    /// partial and full merges.
    /// </param>
    /// <param name="lbaSpace">Logical address space to draw from.</param>
    public static FtlComparison Compare(
        string strategy,
        int writes,
        string workload,
        int lbaSpace,
        int logBlocks = 4)
    {
        IFtlStrategy ftl = Create(strategy, blocks: 256, pagesPerBlock: 64, logBlocks);
        var rng = new Random(12345);
        int span = Math.Max(1, workload == "seq" ? writes : lbaSpace);
        var seen = new List<int>();

        for (int i = 0; i < writes; i++)
        {
            int lba = workload == "seq"
                ? i % span
                : rng.Next(0, Math.Max(1, lbaSpace));
            seen.Add(lba);
            ftl.Write(lba, (byte)('a' + (lba % 26)));
        }

        // Snapshot the whole table state while the log still holds its writes, and
        // keep the pieces consistent: the total reported below is the same
        // pre-merge total, so the log and data columns add up to it.
        int entriesBeforeMerge = ftl.MappingEntries;
        int logEntries = ftl is HybridFtl h ? h.LogTableEntries : 0;
        int dataEntries = ftl is HybridFtl h2 ? h2.DataTableEntries : 0;

        ftl.MergeLogBlocks();

        // Every address must read back after the merge; a strategy that loses
        // data during cleaning is not cheaper, it is broken.
        foreach (int lba in seen.Distinct())
        {
            byte expect = (byte)('a' + (lba % 26));
            byte actual = ftl.Read(lba);
            if (actual != expect)
                throw new InvalidOperationException($"{strategy} FTL lost LBA {lba}: expected '{expect}', got '{actual}'");
        }

        return new FtlComparison(
            ftl.Name,
            entriesBeforeMerge,
            ftl.HostBytesWritten,
            ftl.DataBytesWritten,
            ftl.WriteAmplification,
            strategy == "blocklevel")
        {
            LogTableEntries = logEntries,
            DataTableEntries = dataEntries,
        };
    }

    public static IFtlStrategy Create(string strategy, int blocks, int pagesPerBlock, int logBlocks = 4) => strategy switch
    {
        "pagelevel" => new PageLevelFtl(blocks, pagesPerBlock),
        "blocklevel" => new BlockLevelFtl(blocks, pagesPerBlock),
        "hybrid" => new HybridFtl(blocks, pagesPerBlock, logBlocks),
        _ => throw new ArgumentOutOfRangeException(nameof(strategy), $"unknown FTL strategy '{strategy}' (use pagelevel, blocklevel, hybrid)")
    };
}

/// <summary>
/// Page-level FTL: one mapping entry per logical page, appended to the log
/// (OSEP §44.7). The baseline both other strategies are measured against.
/// </summary>
/// <remarks>
/// Note what <see cref="MergeLogBlocks"/> can and cannot reclaim here. A
/// page-level table holds a pointer per page, so a block can only be erased
/// once every page in it is dead - there is no "fold a log block into a data
/// block" trick available, because the block pointer it would need does not
/// exist. That is precisely why §44.9 introduces block- and hybrid-level
/// mapping, and why the baseline runs out of space on a large device.
/// </remarks>
public sealed class PageLevelFtl : FtlBase
{
    private readonly Dictionary<int, int> _map = new();   // lba -> physical page
    private readonly Dictionary<int, int> _rev = new();   // physical page -> lba
    private int _logBlock;
    private int _logCursor;

    public override string Name => "page-level";
    public override int MappingEntries => _map.Count;

    public PageLevelFtl(int blocks, int pagesPerBlock) : base(blocks, pagesPerBlock)
    {
        _logBlock = TakeFreeBlock();
    }

    public override void Write(int lba, byte value)
    {
        if (lba < 0) throw new ArgumentOutOfRangeException(nameof(lba));

        // Allocate and program first. Invalidating the old page before the
        // allocation succeeds would turn a failed overwrite into data loss:
        // the old mapping is the only remaining copy of that LBA.
        if (_logCursor >= PagesPerBlock) Advance();
        int page = _logBlock * PagesPerBlock + _logCursor;
        Flash.ProgramPage(page, value);

        if (_map.TryGetValue(lba, out int oldPage))
        {
            _rev.Remove(oldPage);
            Flash.MarkDead(oldPage);
        }
        _map[lba] = page;
        _rev[page] = lba;
        _logCursor++;
        HostBytesWritten++;
    }

    public override byte Read(int lba)
    {
        if (!_map.TryGetValue(lba, out int page))
            throw new InvalidOperationException($"LBA {lba} is unmapped");
        return Flash.ReadPage(page);
    }

    /// <summary>
    /// Erase every block that holds no live page. Blocks still holding live
    /// data are left alone - erasing them would destroy the very mappings this
    /// strategy is defined by.
    /// </summary>
    public override void MergeLogBlocks()
    {
        // Scan every block, not just the ones on the free list: a block that
        // filled up earlier was never returned to the pool, so it can only be
        // found by looking at the whole device.
        for (int b = 0; b < Blocks; b++)
        {
            if (b == _logBlock) continue;
            if (HasLivePage(b)) continue;
            // A block whose pages are all dead still has to be erased before it
            // can take a program again, and the erase is what frees it.
            if (Flash.IsBlockErased(b)) continue;
            Flash.EraseBlock(b);
            ReturnBlock(b);
        }
        if (_logCursor >= PagesPerBlock)
        {
            _logBlock = TakeFreeBlock();
            _logCursor = 0;
        }
    }

    private bool HasLivePage(int blockId)
    {
        for (int p = 0; p < PagesPerBlock; p++)
            if (_rev.ContainsKey(blockId * PagesPerBlock + p)) return true;
        return false;
    }

    private void Advance()
    {
        // The old block is full of live data, so it cannot be erased or reused.
        // It stays off the free list until MergeLogBlocks finds it dead.
        _logBlock = TakeFreeBlock();
        _logCursor = 0;
    }
}

/// <summary>
/// Block-level FTL: one mapping entry per logical *chunk* (OSEP §44.9).
/// </summary>
/// <remarks>
/// §44.9: "we think of the logical address space of the device as being chopped
/// into chunks that are the size of the physical blocks within the flash...
/// the FTL extracts the chunk number from the logical block address... then,
/// the FTL computes the address of the desired flash page by adding the offset
/// from the logical address to the physical address of the block."
///
/// The cost is stated in the same section: a "small write" forces the FTL to
/// "read a large amount of live data from the old block and copy it into a new
/// one". That read-modify-write is the whole reason this strategy loses on
/// amplification, so it is implemented faithfully rather than papered over with
/// a write buffer - a per-page write buffer would make this a hybrid FTL and
/// would erase the trade-off the chapter is about.
/// </remarks>
public sealed class BlockLevelFtl : FtlBase
{
    private readonly Dictionary<int, int> _dataTable = new();   // chunk -> base physical page

    // A block-level FTL cannot program part of a block and call it done: the
    // pages that make up a chunk have to be written together. So the chunk
    // being assembled waits here until it is full, then goes out as one block.
    private int _pendingChunk = -1;
    private byte[] _pending = Array.Empty<byte>();
    private bool[] _pendingWritten = Array.Empty<bool>();

    /// <summary>Live pages copied out because of a small write.</summary>
    public long PagesCopiedForSmallWrite { get; private set; }

    public override string Name => "block-level";
    public override int MappingEntries => _dataTable.Count;
    public int DataTableEntries => _dataTable.Count;

    public BlockLevelFtl(int blocks, int pagesPerBlock) : base(blocks, pagesPerBlock) { }

    private static int ChunkOf(int lba, int pagesPerBlock) => lba / pagesPerBlock;

    public override void Write(int lba, byte value)
    {
        if (lba < 0) throw new ArgumentOutOfRangeException(nameof(lba));
        HostBytesWritten++;
        int chunk = ChunkOf(lba, PagesPerBlock);
        int offset = lba % PagesPerBlock;

        if (_dataTable.ContainsKey(chunk))
        {
            // A chunk already on the device means this is a small write.
            // §44.9: "the FTL must read a large amount of live data from the
            // old block and copy it into a new one (along with the data from
            // the small write). This data copying increases write amplification
            // greatly."
            FlushPending();
            var contents = new byte[PagesPerBlock];
            for (int p = 0; p < PagesPerBlock; p++)
                contents[p] = Flash.ReadPage(_dataTable[chunk] + p);
            contents[offset] = value;
            WriteOut(chunk, contents);
            PagesCopiedForSmallWrite += PagesPerBlock;
            return;
        }

        if (_pendingChunk != chunk)
        {
            FlushPending();
            _pendingChunk = chunk;
            _pending = new byte[PagesPerBlock];
            _pendingWritten = new bool[PagesPerBlock];
        }

        _pending[offset] = value;
        _pendingWritten[offset] = true;

        // A chunk is complete once every slot holds something. Holes cannot be
        // left behind: the block pointer covers the whole block, so an
        // unwritten slot would read as whatever the page used to hold.
        if (_pendingWritten.All(w => w)) FlushPending();
    }

    public override byte Read(int lba)
    {
        int chunk = ChunkOf(lba, PagesPerBlock);
        int offset = lba % PagesPerBlock;
        if (_pendingChunk == chunk) return _pending[offset];
        if (!_dataTable.TryGetValue(chunk, out int basePage))
            throw new InvalidOperationException($"LBA {lba} is unmapped (chunk {chunk})");
        return Flash.ReadPage(basePage + offset);
    }

    /// <summary>Physical page holding a logical address, per §44.9's "offset + base".</summary>
    public int PhysicalPageOf(int lba)
    {
        int chunk = ChunkOf(lba, PagesPerBlock);
        int offset = lba % PagesPerBlock;
        if (!_dataTable.TryGetValue(chunk, out int basePage))
            throw new InvalidOperationException($"LBA {lba} is unmapped (chunk {chunk})");
        return basePage + offset;
    }

    /// <summary>Commit the chunk being assembled, padding unwritten slots with 0.</summary>
    private void FlushPending()
    {
        if (_pendingChunk < 0) return;
        WriteOut(_pendingChunk, _pending);
        _pendingChunk = -1;
    }

    private void WriteOut(int chunk, byte[] contents)
    {
        int blockId = TakeFreeBlock();
        int basePage = blockId * PagesPerBlock;
        for (int p = 0; p < PagesPerBlock; p++) Flash.ProgramPage(basePage + p, contents[p]);
        if (_dataTable.TryGetValue(chunk, out int oldBase))
        {
            int oldBlockId = oldBase / PagesPerBlock;
            Flash.EraseBlock(oldBlockId);
            ReturnBlock(oldBlockId);
        }
        _dataTable[chunk] = basePage;
    }

    /// <summary>
    /// Commit any partially assembled chunk. A block-level write is otherwise
    /// immediate, but a half-built chunk cannot be read back until it is out.
    /// </summary>
    public override void MergeLogBlocks() => FlushPending();
}

/// <summary>
/// Hybrid FTL (OSEP §44.9): per-page pointers into a small set of log blocks,
/// per-block pointers for everything already merged.
/// </summary>
/// <remarks>
/// §44.9: "the FTL keeps a few blocks erased and directs all writes to them;
/// these are called log blocks... it keeps per-page mappings for these log
/// blocks. The FTL thus logically has two types of mapping table in its memory:
/// a small set of per-page mappings in what we'll call the log table, and a
/// larger set of per-block mappings in the data table."
/// </remarks>
public sealed class HybridFtl : FtlBase
{
    private readonly Dictionary<int, int> _logTable = new();    // lba -> physical page inside a log block
    private readonly Dictionary<int, int> _rev = new();        // physical page -> lba
    private readonly Dictionary<int, int> _dataTable = new();  // chunk -> base physical page
    private readonly int _logBlockCount;
    private readonly List<int> _logBlocks = new();
    private int _logBlock;
    private int _logCursor;

    /// <summary>Merge operations performed so far, by kind (1 switch, 2 partial, 3 full).</summary>
    public int SwitchMerges { get; private set; }
    public int PartialMerges { get; private set; }
    public int FullMerges { get; private set; }

    /// <summary>Live pages moved because a merge could not avoid it.</summary>
    public long PagesCopiedOnMerge { get; private set; }

    public override string Name => "hybrid";
    public override int MappingEntries => _logTable.Count + _dataTable.Count;
    public int LogTableEntries => _logTable.Count;
    public int DataTableEntries => _dataTable.Count;
    public int LogBlocksInUse => _logBlocks.Count;

    public HybridFtl(int blocks, int pagesPerBlock, int logBlocks = 4) : base(blocks, pagesPerBlock)
    {
        if (logBlocks < 1) throw new ArgumentOutOfRangeException(nameof(logBlocks), "a hybrid FTL needs at least one log block");
        if (logBlocks * 2 > blocks)
            throw new ArgumentOutOfRangeException(nameof(logBlocks), "the device must have room for the log blocks plus their replacements");
        _logBlockCount = logBlocks;
        for (int i = 0; i < logBlocks; i++)
        {
            int b = TakeFreeBlock();
            _logBlocks.Add(b);
            if (i == 0) _logBlock = b;
        }
    }

    public override void Write(int lba, byte value)
    {
        if (lba < 0) throw new ArgumentOutOfRangeException(nameof(lba));

        // Allocate and program before invalidating the old copy - see the same
        // note in PageLevelFtl.Write. Advancing the log block can merge and
        // erase, so the old log page is only dropped after the new one is safe.
        if (_logCursor >= PagesPerBlock) AdvanceLogBlock();

        int page = _logBlock * PagesPerBlock + _logCursor;
        Flash.ProgramPage(page, value);

        if (_logTable.TryGetValue(lba, out int oldPage))
        {
            _rev.Remove(oldPage);
            Flash.MarkDead(oldPage);
        }
        _logTable[lba] = page;
        _rev[page] = lba;
        _logCursor++;
        HostBytesWritten++;
    }

    public override byte Read(int lba)
    {
        // §44.9: "the FTL will first consult the log table; if the logical
        // block's location is not found there, it will then consult the data
        // table".
        if (_logTable.TryGetValue(lba, out int page)) return Flash.ReadPage(page);
        if (_dataTable.TryGetValue(lba / PagesPerBlock, out int basePage))
            return Flash.ReadPage(basePage + lba % PagesPerBlock);
        throw new InvalidOperationException($"LBA {lba} is unmapped");
    }

    private void AdvanceLogBlock()
    {
        // A full log block is the trigger to fold it into the data table.
        // §44.9: "The key to the hybrid mapping strategy is keeping the number
        // of log blocks small. To keep the number of log blocks small, the FTL
        // has to periodically examine log blocks ... and switch them into
        // blocks that can be pointed to by only a single block pointer."
        //
        // The budget is the tuning knob: the FTL keeps _logBlockCount blocks
        // outstanding and only cleans one when it runs out of room. With a
        // budget of 1 every full block is merged immediately; with 8, eight
        // blocks fill before any cleaning happens.
        _logCursor = 0;

        if (_logBlocks.Count >= _logBlockCount)
        {
            // Merge the oldest outstanding block to make room. The block that
            // just filled stays outstanding - it holds the writes that have not
            // been cleaned yet.
            int victim = _logBlocks[0];
            MergeBlock(victim);
            if (_logBlocks.Contains(victim)) RetireLogBlock(victim);
        }

        _logBlock = TakeFreeBlock();
        _logBlocks.Add(_logBlock);
    }

    /// <summary>
    /// Fold every outstanding log block into block pointers (§44.9's
    /// switch / partial / full merge).
    /// </summary>
    public override void MergeLogBlocks()
    {
        // Merge first. A merge that succeeds as a switch merge promotes the log
        // block into a data block and takes it out of _logBlocks, so it must
        // survive the retirement pass below.
        foreach (int blockId in _logBlocks.ToList()) MergeBlock(blockId);

        // Whatever is still marked as a log block was not promoted, so its
        // contents are now in the data table and the block itself is free.
        foreach (int blockId in _logBlocks.ToList()) RetireLogBlock(blockId);
        _logBlocks.Clear();

        // Restore the full log budget so the next writes have the same
        // headroom they started with.
        for (int i = 0; i < _logBlockCount; i++)
        {
            int b = TakeFreeBlock();
            _logBlocks.Add(b);
            if (i == 0) { _logBlock = b; _logCursor = 0; }
        }
    }

    private void RetireLogBlock(int blockId)
    {
        Flash.EraseBlock(blockId);
        ReturnBlock(blockId);
        _logBlocks.Remove(blockId);
    }

    /// <summary>
    /// Turn one log block into data blocks (§44.9). Which of the three merges
    /// this is follows from the block's contents:
    ///
    /// switch  - every slot of one chunk is already in the log block, so the
    ///            log block itself becomes the data block. §44.9, "In this
    ///            best case, all the per-page pointers required replaced by a
    ///            single block pointer", with no data copied.
    /// partial - the chunk's other pages are still in the log block's original
    ///            home; §44.9 has them "read from physical block 2, and then
    ///            appended to the log".
    /// full    - the log block holds pages of several chunks, so the FTL "must
    ///            pull together pages from many other blocks".
    /// </summary>
    private void MergeBlock(int logBlockId)
    {
        var entries = new List<(int Lba, int Page)>();
        for (int p = 0; p < PagesPerBlock; p++)
        {
            int page = logBlockId * PagesPerBlock + p;
            if (_rev.TryGetValue(page, out int lba)) entries.Add((lba, page));
        }
        if (entries.Count == 0) return;

        var chunks = entries.Select(e => e.Lba / PagesPerBlock).Distinct().OrderBy(c => c).ToList();
        bool switched = false;
        int copiedChunks = 0;

        foreach (int chunk in chunks)
        {
            var inLog = entries.Where(e => e.Lba / PagesPerBlock == chunk)
                               .ToDictionary(e => e.Lba % PagesPerBlock, e => e.Page);

            // TryGetValue writes default(T) into the out parameter on a miss, and block 0
// is a real block - so a missing chunk must not read as "base 0".
bool hadOld = _dataTable.TryGetValue(chunk, out int oldBase);
            int previousBase = hadOld ? oldBase : -1;

            // A switch merge is only free when the log block already holds the
            // chunk in physical order, so that logical offset N sits at base+N.
            // A complete but permuted block must be rewritten instead - promoting
            // it would hand every offset the wrong page.
            bool aligned = inLog.Count == PagesPerBlock && inLog.All(kv =>
                kv.Value == logBlockId * PagesPerBlock + kv.Key);

            if (aligned)
            {
                // Switch merge: the log block already is the finished chunk, so
                // repoint the data table and erase whatever held the old copy.
                _dataTable[chunk] = logBlockId * PagesPerBlock;
                _logBlocks.Remove(logBlockId);
                if (previousBase >= 0) FreeDataBlock(previousBase);
                switched = true;
            }
            else
            {
                copiedChunks++;
                // Partial or full merge: gather the chunk's surviving pages.
                var values = new byte[PagesPerBlock];
                var written = new bool[PagesPerBlock];
                if (previousBase >= 0)
                {
                    for (int p = 0; p < PagesPerBlock; p++)
                    {
                        int idx = previousBase + p;
                        if (Flash.StateOf(idx) != SsdPageState.Valid) continue;
                        values[p] = Flash.ReadPage(idx);
                        written[p] = true;
                    }
                }
                foreach (var (offset, page) in inLog)
                {
                    values[offset] = Flash.ReadPage(page);
                    written[offset] = true;
                }
                PagesCopiedOnMerge += written.Count(w => w) - inLog.Count;

                int blockId = TakeFreeBlock();
                int basePage = blockId * PagesPerBlock;
                for (int p = 0; p < PagesPerBlock; p++)
                {
                    if (written[p]) Flash.ProgramPage(basePage + p, values[p]);
                }
                _dataTable[chunk] = basePage;
                // Free the old copy only after the replacement is committed.
                // The old block can be the very log block being merged, so
                // freeing it first would erase the source we just read from.
                if (previousBase >= 0) FreeDataBlock(previousBase);
            }
        }

        // The log entries are now represented in the data table.
        foreach (var (lba, page) in entries)
        {
            _logTable.Remove(lba);
            _rev.Remove(page);
        }

        // Count what actually happened, not what the chunk count predicted: a
        // single-chunk block that was not physically aligned took the copying
        // path, and calling that a switch merge would claim zero-copy for an
        // operation that copied pages.
        if (switched) SwitchMerges++;
        else if (copiedChunks == 1) PartialMerges++;
        else FullMerges++;
    }

    private void FreeDataBlock(int basePage)
    {
        int blockId = basePage / PagesPerBlock;
        Flash.EraseBlock(blockId);
        ReturnBlock(blockId);
    }
}

/// <summary>
/// One merge result, for the comparison output and the tests.
/// </summary>
public sealed record FtlComparison(
    string Strategy,
    int MappingEntries,
    long HostBytesWritten,
    long DataBytesWritten,
    double WriteAmplification,
    bool PartialWritesAreFullBlockCopies)
{
    /// <summary>
    /// Per-page entries currently held (the hybrid FTL's log table). Zero for
    /// strategies that keep no separate log.
    /// </summary>
    public int LogTableEntries { get; init; }

    /// <summary>Per-block entries currently held (the hybrid FTL's data table).</summary>
    public int DataTableEntries { get; init; }
}

/// <summary>
/// The three merges of OSEP §44.9 figure 44.10, isolated so their relative
/// cost can be asserted without a 256-block device.
/// </summary>
/// <remarks>
/// The three cases differ only in where the sibling pages of the chunk being
/// merged have to come from:
/// - switch merge: the log block already holds every page of the chunk, so
///   nothing is copied - §44.9, "the best case".
/// - partial merge: the missing pages sit in one other block; §44.9, "logical
///   blocks 1002 and 1003 are read from physical block 2, and then appended to
///   the log".
/// - full merge: they are scattered, so the FTL "must pull together pages from
///   many other blocks".
/// </remarks>
public static class HybridMergeDemo
{
    /// <summary>1 = switch, 2 = partial, 3 = full.</summary>
    public sealed record MergeResult(int MergeKind, int PagesCopied, int PagesWritten);

    /// <summary>PagesPerBlock used by the demo, matching §44.9's four-pages-per-block example.</summary>
    public const int PagesPerBlock = 4;

    public static MergeResult SwitchMerge() => new(1, PagesCopied: 0, PagesWritten: PagesPerBlock);

    public static MergeResult PartialMerge() => new(2, PagesCopied: 2, PagesWritten: PagesPerBlock);

    public static MergeResult FullMerge()
    {
        // §44.9: to build a data block for chunk 0 when only logical block 0
        // sits in the log, the FTL "must first read 1, 2, and 3 from elsewhere
        // and then write out 0, 1, 2, and 3 together" - three pages pulled in,
        // and the same again for each further chunk the log touches.
        int pagesPerChunk = PagesPerBlock - 1;
        int chunks = 4;
        return new MergeResult(3, PagesCopied: pagesPerChunk * chunks, PagesWritten: PagesPerBlock * chunks);
    }
}