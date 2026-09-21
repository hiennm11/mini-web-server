using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// State of a single flash page (OSEP §44.3).
/// </summary>
public enum SsdPageState
{
    /// <summary>Block was just manufactured or just erased - ready to program.</summary>
    Erased,
    /// <summary>Page holds valid data.</summary>
    Valid,
    /// <summary>Page holds data that has been overwritten elsewhere (garbage).</summary>
    Dead,
}

/// <summary>
/// SSD simulator (OSEP Ch. 44).
///
/// OSEP §44.0 "CRUX: HOW TO BUILD A FLASH-BASED SSD":
///   "How can we handle the expensive nature of erasing? How can we build
///    a device that lasts a long time, given that repeated overwrite will
///    wear the device out?"
///
/// OSEP §44.3 "Basic Flash Operations":
///   "Read (a page): ... typically quite fast, 10s of microseconds or so,
///    regardless of location on the device."
///   "Erase (a block): ... quite expensive, taking a few milliseconds."
///   "Program (a page): ... usually taking around 100s of microseconds."
///
/// OSEP §44.7 "A Log-Structured FTL":
///   "Upon a write to logical block N, the device appends the write to
///    the next free spot in the currently-being-written-to block; we call
///    this style of writing logging. To allow for subsequent reads of
///    block N, the device keeps a mapping table (in its memory, and
///    persistent, in some form, on the device); this table stores the
///    physical address of each logical block in the system."
///
/// OSEP §44.8 "Garbage Collection":
///   "The basic process is simple: find a block that contains one or more
///    garbage pages, read in the live (non-garbage) pages from that block,
///    write out those live pages to the log, and (finally) reclaim the
///    entire block for use in writing."
///
/// OSEP §44.10 "Wear Leveling":
///   "Because multiple erase/program cycles will wear out a flash block,
///    the FTL should try its best to spread that work across all the
///    blocks of the device evenly."
///
/// OSEP §44.12 "TRIM":
///   "The trim operation takes an address (and possibly a length) and
///    simply informs the device that the block(s) specified by the
///    address (and length) have been deleted; the device thus no longer
///    has to track any information about the given address range."
///
/// Our simulator:
///   - A flash device with `Blocks` blocks x `PagesPerBlock` pages each.
///     Every page is 1 byte (so the smoke trace stays compact; real
///     flash pages are 4 KB+).
///   - Three low-level ops: ReadPage (pageAddr), EraseBlock (blockId),
///     ProgramPage (pageAddr, value).
///   - The FTL exposes a disk-like interface: Read(lba), Write(lba, value).
///     Writes append to the next free page in the log (the current write
///     block) and update the mapping table.
///   - Trim(lba) drops the mapping entry without writing - the underlying
///     page becomes Dead.
///   - CollectGarbage() picks the block with the most dead pages, reads
///     live pages to the log, and erases the block.
///   - EraseCount(blockId) tracks write wear per block.
/// </summary>
public sealed class Ssd
{
    public readonly int Blocks;
    public readonly int PagesPerBlock;
    public int TotalPages => Blocks * PagesPerBlock;

    private readonly SsdPageState[] _pageStates;     // flat: index = block*PagesPerBlock + page
    private readonly byte[] _pageValues;
    private readonly int[] _blockEraseCount;

    /// <summary>LBA -> physical page index (flat).</summary>
    private readonly Dictionary<int, int> _mapping = new();

    /// <summary>Inverse map: physical page -> LBA (for live pages). Dead pages have no entry.</summary>
    private readonly Dictionary<int, int> _reverseMapping = new();

    /// <summary>Index of the next free page in the current log block. When the block fills, advance.</summary>
    private int _logCursor = 0;

    /// <summary>Block currently being written to.</summary>
    private int _currentLogBlock = 0;

    public Ssd(int blocks, int pagesPerBlock)
    {
        if (blocks < 2) throw new ArgumentOutOfRangeException(nameof(blocks), "SSD needs >= 2 blocks (log block + spare for GC)");
        if (pagesPerBlock < 2) throw new ArgumentOutOfRangeException(nameof(pagesPerBlock), "Each block needs >= 2 pages");

        Blocks = blocks;
        PagesPerBlock = pagesPerBlock;
        _pageStates = new SsdPageState[TotalPages];
        _pageValues = new byte[TotalPages];
        _blockEraseCount = new int[blocks];

        // Initially every block is "fresh" (all pages Erased). The first
        // write to a fresh block doesn't need an erase first - the spec
        // models fresh flash as Erased.
        for (int p = 0; p < TotalPages; p++) _pageStates[p] = SsdPageState.Erased;
    }

    // ----- Disk-like interface (FTL) -----

    /// <summary>Client write: log-structured append + mapping update.</summary>
    public void Write(int lba, byte value)
    {
        if (lba < 0) throw new ArgumentOutOfRangeException(nameof(lba));

        // If LBA already exists, mark the old physical page dead so GC can reclaim it.
        if (_mapping.TryGetValue(lba, out var oldPage))
        {
            _reverseMapping.Remove(oldPage);
            _pageStates[oldPage] = SsdPageState.Dead;
        }

        // Allocate the next free page. If the current log block is full,
        // advance to the next Erased block. If none, run GC.
        if (_logCursor >= PagesPerBlock)
        {
            AdvanceLogBlock();
        }
        int pageIdx = _currentLogBlock * PagesPerBlock + _logCursor;
        if (_pageStates[pageIdx] != SsdPageState.Erased)
        {
            // Need to erase - this should not happen in steady state because we advance blocks.
            EraseBlock(_currentLogBlock);
        }

        ProgramPage(pageIdx, value, lba);
        _logCursor++;
    }

    /// <summary>Client read: look up mapping, return the page value.</summary>
    public byte Read(int lba)
    {
        if (!_mapping.TryGetValue(lba, out var pageIdx))
            throw new InvalidOperationException($"LBA {lba} is unmapped (never written or trimmed)");
        if (_pageStates[pageIdx] != SsdPageState.Valid)
            throw new InvalidOperationException($"LBA {lba} -> page {pageIdx} is in state {_pageStates[pageIdx]}");
        return _pageValues[pageIdx];
    }

    /// <summary>Trim hint: drop the LBA mapping. The physical page becomes Dead.</summary>
    public void Trim(int lba)
    {
        if (!_mapping.TryGetValue(lba, out var pageIdx))
            return;  // already gone; no-op
        _mapping.Remove(lba);
        _reverseMapping.Remove(pageIdx);
        _pageStates[pageIdx] = SsdPageState.Dead;
    }

    // ----- Garbage collection -----

    /// <summary>
    /// Run GC: pick the block with the most dead pages, migrate live pages
    /// to the log, erase the block.
    /// </summary>
    public SsdGarbageCollectReport CollectGarbage()
    {
        int target = -1;
        int mostDead = -1;
        for (int b = 0; b < Blocks; b++)
        {
            int dead = CountDeadPages(b);
            if (dead > mostDead)
            {
                mostDead = dead;
                target = b;
            }
        }
        if (target < 0 || mostDead == 0)
            return new SsdGarbageCollectReport(-1, 0, 0, 0);  // nothing to clean

        int liveBefore = CountLivePages(target);
        int deadBefore = CountDeadPages(target);

        // Migrate live pages to the log. Capture (lba, value) before erasing.
        var live = new List<(int lba, byte value)>();
        for (int p = 0; p < PagesPerBlock; p++)
        {
            int pageIdx = target * PagesPerBlock + p;
            if (_pageStates[pageIdx] == SsdPageState.Valid)
            {
                int lba = _reverseMapping[pageIdx];
                live.Add((lba, _pageValues[pageIdx]));
            }
        }
        // Erase the target block first (would be necessary to free the pages).
        // But the migration writes need free pages in the log. To keep the
        // log cursor consistent, we migrate first (which may bump the log
        // block), then erase.
        foreach (var (lba, value) in live)
        {
            Write(lba, value);  // goes through Write() which allocates a free page
        }
        EraseBlock(target);

        return new SsdGarbageCollectReport(target, liveBefore, deadBefore, _blockEraseCount[target]);
    }

    // ----- Wear -----

    /// <summary>Erase count for the given block (OSEP §44.10 wear counter).</summary>
    public int EraseCount(int blockId) => _blockEraseCount[blockId];

    /// <summary>Histogram of erase counts across all blocks (for the /ssd/run wear scenario).</summary>
    public string FormatWearReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== SSD Wear Report (M26 / OSEP §44.10) ===");
        sb.AppendLine($"blocks: {Blocks}   pages/block: {PagesPerBlock}");
        sb.AppendLine();
        sb.AppendLine("block | erase count | valid | dead | erased");
        sb.AppendLine("------|-------------|-------|------|--------");
        int totalErases = 0;
        int maxErases = 0;
        int minErases = int.MaxValue;
        for (int b = 0; b < Blocks; b++)
        {
            int valid = 0, dead = 0, erased = 0;
            for (int p = 0; p < PagesPerBlock; p++)
            {
                switch (_pageStates[b * PagesPerBlock + p])
                {
                    case SsdPageState.Valid: valid++; break;
                    case SsdPageState.Dead: dead++; break;
                    case SsdPageState.Erased: erased++; break;
                }
            }
            int count = _blockEraseCount[b];
            totalErases += count;
            if (count > maxErases) maxErases = count;
            if (count < minErases) minErases = count;
            sb.AppendLine($"  {b,-3} | {count,-11} | {valid,-5} | {dead,-4} | {erased,-4}");
        }
        sb.AppendLine();
        sb.AppendLine($"total erases: {totalErases}   min/max: {minErases}/{maxErases}");
        return sb.ToString();
    }

    /// <summary>Pretty-print the disk layout for the /ssd/run route.</summary>
    public string FormatLayout()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== SSD Layout (M26 / OSEP Ch. 44) ===");
        sb.AppendLine($"blocks: {Blocks}   pages/block: {PagesPerBlock}   total pages: {TotalPages}");
        sb.AppendLine($"current log block: {_currentLogBlock}   log cursor: {_logCursor}");
        sb.AppendLine();

        sb.AppendLine("mapping table (LBA -> page):");
        foreach (var kv in _mapping.OrderBy(kv => kv.Key))
        {
            int blockId = kv.Value / PagesPerBlock;
            int pageInBlock = kv.Value % PagesPerBlock;
            sb.AppendLine($"  LBA {kv.Key} -> block {blockId} page {pageInBlock} = '{DisplayByte(_pageValues[kv.Value])}'");
        }
        if (_mapping.Count == 0) sb.AppendLine("  (empty)");

        sb.AppendLine();
        sb.AppendLine("block grid (V=valid D=dead E=erased):");
        sb.AppendLine("block | pages");
        sb.AppendLine("------|--------------------------------------------------");
        for (int b = 0; b < Blocks; b++)
        {
            var cells = new List<string>();
            for (int p = 0; p < PagesPerBlock; p++)
            {
                var s = _pageStates[b * PagesPerBlock + p];
                char label = s switch
                {
                    SsdPageState.Erased => 'E',
                    SsdPageState.Valid => 'V',
                    SsdPageState.Dead => 'D',
                    _ => '?',
                };
                cells.Add(label.ToString());
            }
            string marker = (b == _currentLogBlock) ? " (log)" : "";
            sb.AppendLine($"  {b,-3}  | [{string.Join(" ", cells)}]{marker}");
        }
        return sb.ToString();
    }

    // ----- Low-level ops (OSEP §44.3) -----

    /// <summary>Read a single page (OSEP §44.3 read).</summary>
    public byte ReadPage(int pageIdx)
    {
        if (pageIdx < 0 || pageIdx >= TotalPages) throw new ArgumentOutOfRangeException(nameof(pageIdx));
        return _pageValues[pageIdx];
    }

    /// <summary>Erase a block (OSEP §44.3 erase): bumps wear count, sets all pages to Erased.</summary>
    public void EraseBlock(int blockId)
    {
        if (blockId < 0 || blockId >= Blocks) throw new ArgumentOutOfRangeException(nameof(blockId));
        _blockEraseCount[blockId]++;
        for (int p = 0; p < PagesPerBlock; p++)
        {
            int pageIdx = blockId * PagesPerBlock + p;
            _pageStates[pageIdx] = SsdPageState.Erased;
            _pageValues[pageIdx] = 0;
            _reverseMapping.Remove(pageIdx);
        }
        // If we erased the current log block, reset the cursor.
        if (blockId == _currentLogBlock)
        {
            _logCursor = 0;
        }
    }

    /// <summary>Program a single page (OSEP §44.3 program): write value, set Valid, update mapping.</summary>
    private void ProgramPage(int pageIdx, byte value, int lba)
    {
        if (_pageStates[pageIdx] != SsdPageState.Erased)
            throw new InvalidOperationException($"page {pageIdx} is in state {_pageStates[pageIdx]}; must be Erased before program");
        _pageStates[pageIdx] = SsdPageState.Valid;
        _pageValues[pageIdx] = value;
        // If we are overwriting an LBA, drop the previous mapping. (The Write()
        // caller already handled this case, but defense in depth.)
        _mapping[lba] = pageIdx;
        _reverseMapping[pageIdx] = lba;
    }

    private void AdvanceLogBlock()
    {
        if (_currentLogBlock + 1 >= Blocks)
            throw new InvalidOperationException("SSD full - run garbage collection first");
        _currentLogBlock++;
        _logCursor = 0;
        // The next block should be Erased (we always erase blocks before
        // returning them to the log pool via GC). If not, erase it.
        if (_pageStates[_currentLogBlock * PagesPerBlock] != SsdPageState.Erased)
        {
            EraseBlock(_currentLogBlock);
        }
    }

    private int CountLivePages(int blockId)
    {
        int live = 0;
        for (int p = 0; p < PagesPerBlock; p++)
            if (_pageStates[blockId * PagesPerBlock + p] == SsdPageState.Valid) live++;
        return live;
    }

    private int CountDeadPages(int blockId)
    {
        int dead = 0;
        for (int p = 0; p < PagesPerBlock; p++)
            if (_pageStates[blockId * PagesPerBlock + p] == SsdPageState.Dead) dead++;
        return dead;
    }

    /// <summary>Count of mapped LBAs.</summary>
    public int MappingSize => _mapping.Count;

    /// <summary>Number of dead pages across the whole device.</summary>
    public int DeadPageCount
    {
        get
        {
            int dead = 0;
            for (int p = 0; p < TotalPages; p++)
                if (_pageStates[p] == SsdPageState.Dead) dead++;
            return dead;
        }
    }

    private static string DisplayByte(byte b) =>
        (char)b >= ' ' && (char)b <= '~' ? ((char)b).ToString() : $"\\x{b:X2}";
}

/// <summary>GC report: which block was cleaned, how many live pages migrated, how many dead pages freed.</summary>
public sealed record SsdGarbageCollectReport(int CleanedBlock, int LivePagesMigrated, int DeadPagesFreed, int EraseCountAfter);
