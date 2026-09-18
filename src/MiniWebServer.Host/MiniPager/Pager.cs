using System.Collections.Generic;

namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Page-table lookup result. OSEP §21.2 "The Present Bit" — four states:
///   - Hit: VPN → FrameNo, in physical memory, writable.
///   - HitReadOnly: VPN → FrameNo, in physical memory, COW-shared (slice 19.1).
///     Caller's write will trigger copy-on-write.
///   - InSwap: VPN was evicted; SwapSlot is the swap location.
///   - Miss: no valid mapping at all.
/// </summary>
public enum LookupResult
{
    Hit,
    HitReadOnly,
    InSwap,
    Miss,
}

/// <summary>
/// Page-table lookup strategy. The Pager uses one of these per process.
///
/// Slice 14.1: <see cref="LinearLookup"/> — flat array indexed by VPN.
/// Slice 17.1: <see cref="TwoLevelLookup"/> — page directory + page tables.
/// Slice 18.1: both lookups can return <see cref="LookupResult.InSwap"/>
/// for evicted pages (see <see cref="SwappablePte"/>).
/// Slice 19.1: COW support (see <see cref="CowPte"/>) — LinearLookup
/// can return <see cref="LookupResult.HitReadOnly"/> to trigger COW.
/// </summary>
public interface IPageTableLookup
{
    /// <summary>
    /// Translate a VPN. Returns Hit (with frameNo), InSwap (with swapSlot),
    /// Miss, or HitReadOnly (with frameNo — caller's write will COW).
    /// </summary>
    LookupResult TryLookup(int vpn, out int frameNo, out int swapSlot);

    /// <summary>Set a VPN → frame mapping. Allocates inner pages as needed.</summary>
    void Map(int vpn, int frameNo);

    /// <summary>
    /// Mark the VPN as in-memory with the given frame. Used after a
    /// swap-in (M18) loads a page from swap into a frame.
    /// </summary>
    void RestoreFromSwap(int vpn, int frameNo);

    /// <summary>
    /// Mark the VPN as evicted to the given swap slot. The frame is
    /// now free for reuse.
    /// </summary>
    void EvictToSwap(int vpn, int swapSlot);

    /// <summary>Slice 19.1: mark a VPN as sharing a frame (COW).</summary>
    void ShareFrame(int vpn, int frameNo);

    /// <summary>Slice 19.1: clear the COW flag for a VPN (after private copy).</summary>
    void UnshareFrame(int vpn);

    /// <summary>Slice 19.1: is this VPN currently in COW mode?</summary>
    bool IsCowShared(int vpn);

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
/// Slice 18.1: extended to support swap (PTEs can be in memory or in swap).
/// Slice 19.1: extended with COW support (ReadOnly bit + tracking).
/// </summary>
public sealed class LinearLookup : IPageTableLookup
{
    private readonly SwappablePte[] _ptes;
    public int Capacity => _ptes.Length;
    public int MemoryBytes => _ptes.Length * System.Runtime.InteropServices.Marshal.SizeOf<SwappablePte>();
    public int PopulatedEntries
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _ptes.Length; i++) if (_ptes[i].Valid || _ptes[i].InSwap) n++;
            return n;
        }
    }

    public LinearLookup(int numPages = 1 << 20)
    {
        _ptes = new SwappablePte[numPages];
    }

    public LookupResult TryLookup(int vpn, out int frameNo, out int swapSlot)
    {
        frameNo = -1;
        swapSlot = -1;
        if (vpn < 0 || vpn >= _ptes.Length) return LookupResult.Miss;
        var pte = _ptes[vpn];
        if (pte.Valid)
        {
            frameNo = pte.FrameNo;
            // Slice 19.1: ReadOnly flag means COW-shared.
            return pte.ReadOnly ? LookupResult.HitReadOnly : LookupResult.Hit;
        }
        if (pte.InSwap) { swapSlot = pte.SwapSlot; return LookupResult.InSwap; }
        return LookupResult.Miss;
    }

    public void Map(int vpn, int frameNo)
    {
        if (vpn < 0 || vpn >= _ptes.Length)
            throw new ArgumentOutOfRangeException(nameof(vpn));
        if (frameNo < 0) throw new ArgumentOutOfRangeException(nameof(frameNo));
        _ptes[vpn] = new SwappablePte { Valid = true, FrameNo = frameNo };
    }

    public void RestoreFromSwap(int vpn, int frameNo)
    {
        if (vpn < 0 || vpn >= _ptes.Length)
            throw new ArgumentOutOfRangeException(nameof(vpn));
        _ptes[vpn] = new SwappablePte { Valid = true, FrameNo = frameNo };
    }

    public void EvictToSwap(int vpn, int swapSlot)
    {
        if (vpn < 0 || vpn >= _ptes.Length)
            throw new ArgumentOutOfRangeException(nameof(vpn));
        if (swapSlot < 0) throw new ArgumentOutOfRangeException(nameof(swapSlot));
        _ptes[vpn] = new SwappablePte { InSwap = true, SwapSlot = swapSlot };
    }

    /// <summary>Slice 19.1: mark this VPN as COW-shared with another VPN.</summary>
    public void ShareFrame(int vpn, int frameNo)
    {
        if (vpn < 0 || vpn >= _ptes.Length)
            throw new ArgumentOutOfRangeException(nameof(vpn));
        if (frameNo < 0) throw new ArgumentOutOfRangeException(nameof(frameNo));
        var pte = _ptes[vpn];
        _ptes[vpn] = new SwappablePte
        {
            Valid = true,
            FrameNo = frameNo,
            Dirty = pte.Dirty,
            Referenced = pte.Referenced,
            ReadOnly = true,  // slice 19.1: COW marker
        };
    }

    public void UnshareFrame(int vpn)
    {
        var pte = _ptes[vpn];
        if (pte.Valid)
            _ptes[vpn] = new SwappablePte { Valid = true, FrameNo = pte.FrameNo, Dirty = pte.Dirty };
    }

    public bool IsCowShared(int vpn)
    {
        if (vpn < 0 || vpn >= _ptes.Length) return false;
        var pte = _ptes[vpn];
        return pte.Valid && pte.ReadOnly;
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
/// Slice 18.1: PTEs can be in memory or in swap.
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
            int bytes = PgdSize * System.Runtime.InteropServices.Marshal.SizeOf<PageDirectoryEntry>();
            bytes += _pts.Count * (PtSize * System.Runtime.InteropServices.Marshal.SizeOf<PageTableEntry>());
            return bytes;
        }
    }
    public int PopulatedEntries
    {
        get
        {
            int n = _pgd.PopulatedCount();
            foreach (var pt in _pts.Values) n += pt.MappedCount();
            return n;
        }
    }

    public TwoLevelLookup()
    {
        _pgd = new PageDirectory(PgdSize);
    }

    public static (int pgdIndex, int ptIndex) Decompose(int vpn)
    {
        int pgd = vpn >> PtBits;
        int pt = vpn & (PtSize - 1);
        return (pgd, pt);
    }

    public LookupResult TryLookup(int vpn, out int frameNo, out int swapSlot)
    {
        frameNo = -1;
        swapSlot = -1;
        if (vpn < 0 || vpn >= Capacity) return LookupResult.Miss;
        var (pgd, pt) = Decompose(vpn);
        var pde = _pgd.Get(pgd);
        if (!pde.Valid) return LookupResult.Miss;
        if (!_pts.TryGetValue(pgd, out var inner)) return LookupResult.Miss;
        var pte = inner.Get(pt);
        if (pte.Valid) { frameNo = pte.FrameNo; return LookupResult.Hit; }
        // PTE has Valid bit; InSwap semantics would require extending
        // PageTableEntry. For M18 we use LinearLookup for swap demos.
        return LookupResult.Miss;
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

    public void RestoreFromSwap(int vpn, int frameNo)
    {
        var (pgd, pt) = Decompose(vpn);
        if (!_pts.TryGetValue(pgd, out var inner))
            throw new InvalidOperationException($"no PT for pgd {pgd}");
        inner.Set(pt, new PageTableEntry { Valid = true, FrameNo = frameNo });
    }

    public void EvictToSwap(int vpn, int swapSlot)
    {
        var (pgd, pt) = Decompose(vpn);
        if (!_pts.TryGetValue(pgd, out var inner))
            throw new InvalidOperationException($"no PT for pgd {pgd}");
        inner.Set(pt, PageTableEntry.Empty);
    }

    // Slice 19.1 COW stubs — TwoLevelLookup doesn't yet support COW
    // (PTEs are PageTableEntry which only has Valid bit). Documented as
    // deferred in m19 overview.
    public void ShareFrame(int vpn, int frameNo)
        => throw new NotSupportedException("TwoLevelLookup does not yet support COW; use LinearLookup for COW demos");

    public void UnshareFrame(int vpn)
        => throw new NotSupportedException("TwoLevelLookup does not yet support COW; use LinearLookup for COW demos");

    public bool IsCowShared(int vpn) => false;
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
    private readonly FrameInfo[] _frames;
    private int _step;

    public PhysicalMemory Memory { get; }
    public Swap Swap { get; }
    public IEvictionPolicy Eviction { get; set; } = new FifoEviction();
    public int NumFramesAllocated { get; private set; }
    public int Evictions { get; private set; }
    public int SwapIns { get; private set; }

    public IReadOnlyList<TraceEvent> Trace => _trace;
    public IEnumerable<IPageTableLookup> AllPageTables => _lookups.Values;

    public Pager(int numFrames)
    {
        Memory = new PhysicalMemory(numFrames);
        Swap = new Swap();
        _frames = new FrameInfo[numFrames];
        for (int i = 0; i < numFrames; i++) _frames[i] = new FrameInfo { FrameNo = i };
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

    /// <summary>Reset all frame allocations (clear all memory). Used for repeatable smoke runs.</summary>
    public void ResetFrames()
    {
        for (int i = 0; i < _frames.Length; i++) _frames[i] = new FrameInfo { FrameNo = i };
        NumFramesAllocated = 0;
        Evictions = 0;
        SwapIns = 0;
    }

    /// <summary>
    /// Translate <paramref name="va"/> for the given process.
    /// Returns the outcome; if <see cref="TranslateOutcome.Hit"/>
    /// the <paramref name="pa"/> parameter is set.
    ///
    /// Slice 16.1 (TLB): TLB consulted first (OSEP §19.1 line 6).
    /// Slice 17.1 (multi-level): page-table walk dispatches to
    /// <see cref="IPageTableLookup.TryLookup"/>.
    /// Slice 18.1 (replacement): if <see cref="LookupResult.InSwap"/>
    /// is returned, we bring the page back from swap (need a free
    /// frame; if none, evict one). If <see cref="LookupResult.Miss"/>
    /// is returned AND the VPN was never mapped (not just evicted),
    /// we report a hard page fault — the simulator cannot allocate
    /// from disk on first touch.
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

        // Slice 16.1: TLB lookup first (OSEP §19.1 line 2).
        if (Tlb is not null && Tlb.Lookup(pid, va.Vpn, out int tlbPfn))
        {
            // Update eviction-policy LRU recency (M18).
            Eviction.OnAccess(_frames, tlbPfn, _step);
            pa = tlbPfn * VirtualAddress.PAGE_SIZE + va.Offset;
            var paFromTlb = new PhysicalAddress(pa);
            var tlbHit = new TraceEvent(_step, pid, va.ToString(), paFromTlb.ToString(),
                TranslateOutcome.Hit, $"tlb-hit frame={tlbPfn}");
            _trace.Add(tlbHit);
            return TranslateOutcome.Hit;
        }

        // TLB miss -> walk page table.
        var result = pt.TryLookup(va.Vpn, out int pteFrameNo, out int swapSlot);

        if (result == LookupResult.Miss)
        {
            // OSEP §21.4 "Page-Fault Control Flow" step 3-7: this is a
            // first-touch (the page is unmapped). In a real OS this
            // loads from the executable file. Our simulator treats
            // this as "zero the page" and allocates a frame (which may
            // trigger eviction if memory is full).
            Map(pid, va.Vpn, frameNo: -1);
            // Re-do the lookup (the map just installed the PTE).
            result = pt.TryLookup(va.Vpn, out pteFrameNo, out swapSlot);
            if (result != LookupResult.Hit)
            {
                var ev = new TraceEvent(_step, pid, va.ToString(), null,
                    TranslateOutcome.PageFault, $"vpn {va.Vpn} map-after-fault failed");
                _trace.Add(ev);
                return TranslateOutcome.PageFault;
            }
            var firstTouch = new TraceEvent(_step, pid, va.ToString(), new PhysicalAddress(pteFrameNo * VirtualAddress.PAGE_SIZE + va.Offset).ToString(),
                TranslateOutcome.Hit, $"first-touch frame={pteFrameNo}");
            _trace.Add(firstTouch);
            Tlb?.Insert(pid, va.Vpn, pteFrameNo);
            pa = pteFrameNo * VirtualAddress.PAGE_SIZE + va.Offset;
            return TranslateOutcome.Hit;
        }

        if (result == LookupResult.InSwap)
        {
            // OSEP §21.4 "Page-Fault Control Flow" step 9: read page
            // back from swap into a free frame.
            int freeFrame = FindFreeFrame();
            if (freeFrame < 0)
            {
                // OSEP §22.5: pick a victim to evict.
                freeFrame = Eviction.PickVictim(_frames);
                EvictFrame(freeFrame);
            }
            // OSEP §21.4 step 8: read from swap.
            var frameBuf = new byte[VirtualAddress.PAGE_SIZE];
            Swap.ReadIn(swapSlot, frameBuf);
            Swap.Free(swapSlot);
            Memory.WriteBytes(freeFrame * VirtualAddress.PAGE_SIZE, frameBuf);
            pt.RestoreFromSwap(va.Vpn, freeFrame);
            _frames[freeFrame].OwnerPid = pid;
            _frames[freeFrame].Vpn = va.Vpn;
            Eviction.OnAllocate(_frames, freeFrame, _step);
            SwapIns++;
            Tlb?.Insert(pid, va.Vpn, freeFrame);
            pteFrameNo = freeFrame;
        }
        else
        {
            // Already in memory. Update eviction-policy LRU recency.
            Eviction.OnAccess(_frames, pteFrameNo, _step);
            // OnAllocate just records InsertedTick for FIFO; for LRU
            // we already set LastUsedTick in OnAccess. No-op here.
        }

        // OSEP §19.1 line 18: TLB_Insert.
        Tlb?.Insert(pid, va.Vpn, pteFrameNo);

        pa = pteFrameNo * VirtualAddress.PAGE_SIZE + va.Offset;
        var paStruct = new PhysicalAddress(pa);
        var label = result == LookupResult.InSwap
            ? $"swap-in frame={pteFrameNo}"
            : (pt is TwoLevelLookup ? $"pt-walk-2lvl frame={pteFrameNo}" : $"pt-walk frame={pteFrameNo}");
        var ev2 = new TraceEvent(_step, pid, va.ToString(), paStruct.ToString(),
            TranslateOutcome.Hit, label);
        _trace.Add(ev2);
        return TranslateOutcome.Hit;
    }

    private int FindFreeFrame()
    {
        for (int i = 0; i < _frames.Length; i++)
            if (!_frames[i].Allocated) return i;
        return -1;
    }

    /// <summary>
    /// OSEP §22.5 "Pick the victim" + §21.4 "If there is no free frame":
    /// write the victim's frame contents to swap, mark the frame free,
    /// mark the victim's PTE as in-swap.
    /// </summary>
    private void EvictFrame(int frameNo)
    {
        var victim = _frames[frameNo];
        if (!victim.Allocated) return; // shouldn't happen
        // Save the frame's bytes to swap.
        var buf = new byte[VirtualAddress.PAGE_SIZE];
        Memory.ReadBytes(frameNo * VirtualAddress.PAGE_SIZE, buf, VirtualAddress.PAGE_SIZE);
        int slot = Swap.WriteOut(buf);
        // Mark the victim's PTE as in-swap.
        if (_lookups.TryGetValue(victim.OwnerPid, out var pt))
        {
            pt.EvictToSwap(victim.Vpn, slot);
        }
        // Flush the TLB entry for the evicted page.
        if (Tlb is not null) Tlb.Flush();
        // Free the frame.
        victim.OwnerPid = -1;
        victim.Vpn = -1;
        victim.Allocated = false;
        Memory.ZeroFrame(frameNo);
        Evictions++;
    }

    /// <summary>
    /// Map <paramref name="vpn"/> of <paramref name="pid"/> to a physical frame.
    /// If <paramref name="frameNo"/> is -1, the pager picks a free frame (or evicts one if none free).
    /// If the VPN is already in swap, the swap contents are loaded into the new frame.
    /// </summary>
    public void Map(int pid, int vpn, int frameNo = -1)
    {
        if (!_lookups.TryGetValue(pid, out var pt))
            throw new InvalidOperationException($"pid {pid} not found");
        if (vpn < 0 || vpn >= pt.Capacity)
            throw new ArgumentOutOfRangeException(nameof(vpn));

        int chosenFrame;
        if (frameNo >= 0)
        {
            chosenFrame = frameNo;
        }
        else
        {
            chosenFrame = FindFreeFrame();
            if (chosenFrame < 0)
            {
                chosenFrame = Eviction.PickVictim(_frames);
                EvictFrame(chosenFrame);
            }
        }

        // OSEP §21.4 step 7-9: if the VPN is in swap, read it back into
        // the frame instead of zeroing the frame.
        var lookup = pt.TryLookup(vpn, out _, out int swapSlot);
        if (lookup == LookupResult.InSwap)
        {
            var frameBuf = new byte[VirtualAddress.PAGE_SIZE];
            Swap.ReadIn(swapSlot, frameBuf);
            Swap.Free(swapSlot);
            Memory.WriteBytes(chosenFrame * VirtualAddress.PAGE_SIZE, frameBuf);
            pt.RestoreFromSwap(vpn, chosenFrame);
            SwapIns++;
        }
        else
        {
            // First-touch: zero the frame.
            pt.Map(vpn, chosenFrame);
        }

        _frames[chosenFrame].OwnerPid = pid;
        _frames[chosenFrame].Vpn = vpn;
        _frames[chosenFrame].Allocated = true;
        Eviction.OnAllocate(_frames, chosenFrame, _step);
        if (chosenFrame >= NumFramesAllocated) NumFramesAllocated = chosenFrame + 1;
    }

    /// <summary>
    /// Slice 19.1: copy-on-write fork. Make <paramref name="destVpn"/> in
    /// <paramref name="destPid"/> share the same physical frame as
    /// <paramref name="sourceVpn"/> in <paramref name="sourcePid"/>.
    /// The page is marked ReadOnly in both PTEs; the first write to
    /// either will trigger a copy.
    ///
    /// OSEP §23.1 VMS "Other Neat Tricks":
    ///   "when the OS needs to copy a page from one address space to
    ///    another, instead of copying it, it can map it into the
    ///    target address space and mark it read-only in both address
    ///    spaces."
    /// </summary>
    public void ShareFrame(int sourcePid, int sourceVpn, int destPid, int destVpn)
    {
        if (!_lookups.TryGetValue(sourcePid, out var srcPt))
            throw new InvalidOperationException($"source pid {sourcePid} not found");
        if (!_lookups.TryGetValue(destPid, out var dstPt))
            throw new InvalidOperationException($"dest pid {destPid} not found");
        var srcLookup = srcPt.TryLookup(sourceVpn, out int srcFrame, out _);
        if (srcLookup != LookupResult.Hit && srcLookup != LookupResult.HitReadOnly)
            throw new InvalidOperationException($"source vpn {sourceVpn} not in memory (lookup={srcLookup})");
        // Mark source as ReadOnly (already-writable) AND install the dest
        // as a shared, ReadOnly PTE pointing to the same frame.
        srcPt.ShareFrame(sourceVpn, srcFrame);  // marks source ReadOnly
        dstPt.ShareFrame(destVpn, srcFrame);
        // Update frame owner to track the most recent sharee (frame
        // ownership becomes ambiguous; for simplicity, destPid becomes
        // the primary owner).
        _frames[srcFrame].OwnerPid = destPid;
        _frames[srcFrame].Vpn = destVpn;
    }

    /// <summary>
    /// Slice 19.1: write <paramref name="data"/> to the physical page
    /// backing <paramref name="pid"/>'s <paramref name="vpn"/>.
    /// Triggers copy-on-write if the page is shared (OSEP §23.1).
    /// </summary>
    public bool Write(int pid, int vpn, byte[] data)
    {
        if (data.Length != VirtualAddress.PAGE_SIZE)
            throw new ArgumentException($"data must be {VirtualAddress.PAGE_SIZE} bytes", nameof(data));
        if (!_lookups.TryGetValue(pid, out var pt))
            throw new InvalidOperationException($"pid {pid} not found");

        var lookup = pt.TryLookup(vpn, out int frame, out _);
        if (lookup == LookupResult.Miss || lookup == LookupResult.InSwap)
        {
            // Lazy first-touch / swap-in.
            Map(pid, vpn, frameNo: -1);
            lookup = pt.TryLookup(vpn, out frame, out _);
            if (lookup != LookupResult.Hit && lookup != LookupResult.HitReadOnly)
                throw new InvalidOperationException($"write to unmapped vpn {vpn}");
        }

        // OSEP §23.1: if the page is ReadOnly (shared), we must COW
        // before writing. Allocate a new frame, copy the old contents
        // into it, update this process's PTE to point to the new frame
        // (marked writable), then write.
        if (lookup == LookupResult.HitReadOnly || pt.IsCowShared(vpn))
        {
            // Find a free frame (or evict one).
            int newFrame = FindFreeFrame();
            if (newFrame < 0)
            {
                newFrame = Eviction.PickVictim(_frames);
                EvictFrame(newFrame);
            }
            // Copy old contents into the new frame.
            var oldBuf = new byte[VirtualAddress.PAGE_SIZE];
            Memory.ReadBytes(frame * VirtualAddress.PAGE_SIZE, oldBuf, VirtualAddress.PAGE_SIZE);
            Memory.WriteBytes(newFrame * VirtualAddress.PAGE_SIZE, oldBuf);
            // Update this PTE to point to the new frame (writable).
            pt.UnshareFrame(vpn);
            pt.Map(vpn, newFrame);  // sets Valid + FrameNo, no ReadOnly
            // The other PTE (the partner) keeps the old frame and stays ReadOnly.
            _frames[newFrame].OwnerPid = pid;
            _frames[newFrame].Vpn = vpn;
            _frames[newFrame].Allocated = true;
            Eviction.OnAllocate(_frames, newFrame, _step);
            frame = newFrame;
            Evictions++;  // count COW as an eviction-like event
        }

        // OSEP §23.1 write to the now-private page.
        Memory.WriteBytes(frame * VirtualAddress.PAGE_SIZE, data);
        var ev = new TraceEvent(_step, pid, $"0x{vpn * VirtualAddress.PAGE_SIZE:X8}", $"0x{frame * VirtualAddress.PAGE_SIZE:X8}",
            TranslateOutcome.Hit, $"write frame={frame}");
        _trace.Add(ev);
        return true;
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
