using System.Collections.Generic;

namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Page-table lookup strategy. The Pager uses one of these per process.
///
/// Slice 14.1: <see cref="LinearLookup"/> — flat array indexed by VPN.
/// Slice 17.1: <see cref="TwoLevelLookup"/> — page directory + page tables.
/// </summary>
public interface IPageTableLookup
{
    /// <summary>Translate a VPN to a frame number. Returns false if no valid mapping.</summary>
    bool TryTranslate(int vpn, out int frameNo);

    /// <summary>Set a VPN → frame mapping. Allocates inner pages as needed.</summary>
    void Map(int vpn, int frameNo);

    /// <summary>How many bytes the page table uses (PGD + PT pages, or flat array).</summary>
    int MemoryBytes { get; }

    /// <summary>How many entries (PTEs + PDEs) are populated.</summary>
    int PopulatedEntries { get; }

    /// <summary>Total entries the page table can hold (1 << 20 for 32-bit VA).</summary>
    int Capacity { get; }
}

/// <summary>
/// Linear page table lookup (slice 14.1).
/// OSEP §18.3 "Linear Page Table".
/// </summary>
public sealed class LinearLookup : IPageTableLookup
{
    private readonly Pte[] _ptes;
    public int Capacity => _ptes.Length;
    public int MemoryBytes => _ptes.Length * System.Runtime.InteropServices.Marshal.SizeOf<Pte>();
    public int PopulatedEntries
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _ptes.Length; i++) if (_ptes[i].Valid) n++;
            return n;
        }
    }

    public LinearLookup(int numPages = 1 << 20)
    {
        _ptes = new Pte[numPages];
    }

    public bool TryTranslate(int vpn, out int frameNo)
    {
        frameNo = -1;
        if (vpn < 0 || vpn >= _ptes.Length) return false;
        if (!_ptes[vpn].Valid) return false;
        frameNo = _ptes[vpn].FrameNo;
        return true;
    }

    public void Map(int vpn, int frameNo)
    {
        if (vpn < 0 || vpn >= _ptes.Length)
            throw new ArgumentOutOfRangeException(nameof(vpn));
        if (frameNo < 0) throw new ArgumentOutOfRangeException(nameof(frameNo));
        _ptes[vpn] = new Pte { Valid = true, FrameNo = frameNo };
    }
}

/// <summary>
/// Two-level page table lookup (slice 17.1).
///
/// OSEP §20.1 "A Two-Level Page Table":
///   - 32-bit VA + 4 KB page: top 10 bits = PGD index, next 10 bits = PT index,
///     bottom 12 bits = offset.
///   - 1024 PGD entries, each pointing to a 1024-entry PT page.
///   - Sparse address spaces allocate only the PT pages they need.
///
/// In this simulator, the "PT pages" are just arrays in memory — we
/// don't store them in <see cref="PhysicalMemory"/> (the simulator
/// already has the frame table; storing PT pages there would be
/// realistic but complicates allocation).
/// </summary>
public sealed class TwoLevelLookup : IPageTableLookup
{
    public const int PgdBits = 10;
    public const int PtBits = 10;
    public const int OffsetBits = 12;
    public const int PgdSize = 1 << PgdBits;  // 1024
    public const int PtSize = 1 << PtBits;    // 1024
    public const int VpnBits = PgdBits + PtBits;  // 20

    private readonly PageDirectory _pgd;
    private readonly Dictionary<int, InnerPageTable> _pts = new();

    public int Capacity => PgdSize * PtSize;
    public int MemoryBytes
    {
        get
        {
            // PGD: 1024 PDEs × 8 bytes (Valid + long) = 8 KB
            // Each populated PT: 1024 PTEs × ~16 bytes = 16 KB
            int bytes = PgdSize * System.Runtime.InteropServices.Marshal.SizeOf<PageDirectoryEntry>();
            bytes += _pts.Count * (PtSize * System.Runtime.InteropServices.Marshal.SizeOf<PageTableEntry>());
            return bytes;
        }
    }
    public int PopulatedEntries
    {
        get
        {
            int n = _pgd.PopulatedCount();  // each is 1 PDE entry
            foreach (var pt in _pts.Values) n += pt.MappedCount();
            return n;
        }
    }

    public TwoLevelLookup()
    {
        _pgd = new PageDirectory(PgdSize);
    }

    /// <summary>Decompose VPN into PGD index and PT index.</summary>
    public static (int pgdIndex, int ptIndex) Decompose(int vpn)
    {
        int pgd = vpn >> PtBits;
        int pt = vpn & (PtSize - 1);
        return (pgd, pt);
    }

    public bool TryTranslate(int vpn, out int frameNo)
    {
        frameNo = -1;
        if (vpn < 0 || vpn >= Capacity) return false;
        var (pgd, pt) = Decompose(vpn);
        var pde = _pgd.Get(pgd);
        if (!pde.Valid) return false;
        if (!_pts.TryGetValue(pgd, out var inner)) return false;
        var pte = inner.Get(pt);
        if (!pte.Valid) return false;
        frameNo = pte.FrameNo;
        return true;
    }

    public void Map(int vpn, int frameNo)
    {
        if (vpn < 0 || vpn >= Capacity)
            throw new ArgumentOutOfRangeException(nameof(vpn));
        if (frameNo < 0) throw new ArgumentOutOfRangeException(nameof(frameNo));
        var (pgd, pt) = Decompose(vpn);
        if (!_pts.TryGetValue(pgd, out var inner))
        {
            inner = new InnerPageTable(PtSize);
            _pts[pgd] = inner;
            _pgd.Set(pgd, new PageDirectoryEntry { Valid = true, PageTableFrame = pgd });
        }
        inner.Set(pt, new PageTableEntry { Valid = true, FrameNo = frameNo });
    }
}

/// <summary>
/// Multi-process page-table manager. Holds a <see cref="PageTable"/>
/// per process and a shared <see cref="PhysicalMemory"/>. Slice 14.1
/// has no replacement policy and no swap — every page fault is
/// fatal (the simulation records it and continues, but the OS
/// would crash on a real fault).
///
/// OSEP §18.7 "Putting It Together: Memory Access" — the
/// translation pipeline:
///   1. Compute VPN from VA.
///   2. Look up PTE in the page table.
///   3. If valid, compute PA = PTE.Frame * PAGE_SIZE + offset.
///   4. If not valid, page fault.
///
/// Slice 17.1 (multi-level page tables): the per-process page-table
/// structure is now an <see cref="IPageTableLookup"/> — either
/// <see cref="LinearLookup"/> (M14.1) or <see cref="TwoLevelLookup"/> (M17.1).
/// </summary>
public sealed class Pager
{
    private readonly Dictionary<int, IPageTableLookup> _lookups = new();
    private readonly List<TraceEvent> _trace = new();
    private int _step;

    public PhysicalMemory Memory { get; }
    public int NumFramesAllocated { get; private set; }

    public IReadOnlyList<TraceEvent> Trace => _trace;
    public IEnumerable<IPageTableLookup> AllPageTables => _lookups.Values;

    public Pager(int numFrames)
    {
        Memory = new PhysicalMemory(numFrames);
    }

    /// <summary>Create a page table for a new process. Default is linear; pass <c>twoLevel: true</c> for 2-level.</summary>
    public IPageTableLookup CreateProcess(int pid, bool twoLevel = false)
    {
        if (_lookups.ContainsKey(pid))
            throw new InvalidOperationException($"pid {pid} already exists");
        IPageTableLookup pt = twoLevel
            ? new TwoLevelLookup()
            : new LinearLookup();
        _lookups[pid] = pt;
        return pt;
    }

    /// <summary>
    /// Optional TLB. When set, Translate() consults the TLB before the
    /// page table (OSEP §19.1 "TLB Basic Algorithm"). When null, the
    /// pager behaves as in slice 14.1 (page-table only).
    /// </summary>
    public Tlb? Tlb { get; set; }

    /// <summary>
    /// Translate <paramref name="va"/> for the given process.
    /// Returns the outcome; if <see cref="TranslateOutcome.Hit"/>
    /// the <paramref name="pa"/> parameter is set.
    ///
    /// Slice 16.1 (TLB): if <see cref="Tlb"/> is non-null, the TLB is
    /// consulted first. On a TLB hit, we skip the page-table walk entirely
    /// (OSEP §19.1 line 6). On a TLB miss, we walk the page table and
    /// then TLB_Insert() the result (OSEP §19.1 line 18).
    ///
    /// Slice 17.1 (multi-level): the page-table walk now goes through
    /// <see cref="IPageTableLookup.TryTranslate"/>, which dispatches to
    /// either <see cref="LinearLookup"/> (M14.1) or <see cref="TwoLevelLookup"/>
    /// (M17.1) depending on what was created.
    /// </summary>
    public TranslateOutcome Translate(int pid, int vaValue, out int pa)
    {
        pa = -1;
        _step++;

        if (!_lookups.TryGetValue(pid, out var pt))
        {
            var ev = new TraceEvent(_step, pid, $"0x{vaValue:X8}", null,
                TranslateOutcome.OutOfRange, $"unknown pid {pid}");
            _trace.Add(ev);
            return TranslateOutcome.OutOfRange;
        }

        var va = new VirtualAddress(vaValue);
        if (va.Vpn < 0 || va.Vpn >= pt.Capacity)
        {
            var ev = new TraceEvent(_step, pid, va.ToString(), null,
                TranslateOutcome.OutOfRange, $"vpn {va.Vpn} out of range (capacity={pt.Capacity})");
            _trace.Add(ev);
            return TranslateOutcome.OutOfRange;
        }

        // Slice 16.1: TLB lookup first (OSEP §19.1 Figure 19.1 line 2).
        if (Tlb is not null && Tlb.Lookup(pid, va.Vpn, out int tlbPfn))
        {
            pa = tlbPfn * VirtualAddress.PAGE_SIZE + va.Offset;
            var paFromTlb = new PhysicalAddress(pa);
            var tlbHit = new TraceEvent(_step, pid, va.ToString(), paFromTlb.ToString(),
                TranslateOutcome.Hit, $"tlb-hit frame={tlbPfn}");
            _trace.Add(tlbHit);
            return TranslateOutcome.Hit;
        }

        // TLB miss -> walk page table (OSEP §19.1 line 12).
        // Slice 17.1: dispatches to LinearLookup or TwoLevelLookup.
        if (!pt.TryTranslate(va.Vpn, out int pteFrameNo))
        {
            var ev = new TraceEvent(_step, pid, va.ToString(), null,
                TranslateOutcome.PageFault, $"vpn {va.Vpn} not mapped (no swap in slice 14.1)");
            _trace.Add(ev);
            return TranslateOutcome.PageFault;
        }

        // OSEP §19.1 line 18: TLB_Insert after successful page-table walk.
        Tlb?.Insert(pid, va.Vpn, pteFrameNo);

        pa = pteFrameNo * VirtualAddress.PAGE_SIZE + va.Offset;
        var paFromPt = new PhysicalAddress(pa);
        var ptLabel = pt is TwoLevelLookup ? $"pt-walk-2lvl frame={pteFrameNo}" : $"pt-walk frame={pteFrameNo}";
        var ptHit = new TraceEvent(_step, pid, va.ToString(), paFromPt.ToString(),
            TranslateOutcome.Hit, ptLabel);
        _trace.Add(ptHit);
        return TranslateOutcome.Hit;
    }

    /// <summary>Map <paramref name="vpn"/> of <paramref name="pid"/> to <paramref name="frameNo"/>.</summary>
    public void Map(int pid, int vpn, int frameNo)
    {
        if (!_lookups.TryGetValue(pid, out var pt))
            throw new InvalidOperationException($"pid {pid} not found");
        if (frameNo < 0 || frameNo >= Memory.NumFrames)
            throw new ArgumentOutOfRangeException(nameof(frameNo));
        pt.Map(vpn, frameNo);
        Memory.ZeroFrame(frameNo);
        if (frameNo >= NumFramesAllocated) NumFramesAllocated = frameNo + 1;
    }

    public PagerStats Stats()
    {
        int hits = 0, faults = 0, oor = 0;
        foreach (var ev in _trace)
        {
            switch (ev.Outcome)
            {
                case TranslateOutcome.Hit: hits++; break;
                case TranslateOutcome.PageFault: faults++; break;
                case TranslateOutcome.OutOfRange: oor++; break;
            }
        }
        return new PagerStats(_step, hits, faults, oor, _lookups.Count, _trace.Count);
    }
}

public sealed record PagerStats(int TotalAccesses, int Hits, int Faults, int OutOfRange, int Processes, int TraceEvents);
