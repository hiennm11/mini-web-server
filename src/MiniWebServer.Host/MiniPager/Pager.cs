using System.Collections.Generic;

namespace MiniWebServer.Host.MiniPager;

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
/// </summary>
public sealed class Pager
{
    private readonly Dictionary<int, PageTable> _pageTables = new();
    private readonly List<TraceEvent> _trace = new();
    private int _step;

    public PhysicalMemory Memory { get; }
    public int NumFramesAllocated { get; private set; }

    public IReadOnlyList<TraceEvent> Trace => _trace;
    public IEnumerable<PageTable> AllPageTables => _pageTables.Values;

    public Pager(int numFrames)
    {
        Memory = new PhysicalMemory(numFrames);
    }

    /// <summary>Create a page table for a new process.</summary>
    public PageTable CreateProcess(int pid)
    {
        if (_pageTables.ContainsKey(pid))
            throw new InvalidOperationException($"pid {pid} already exists");
        var pt = new PageTable(pid);
        _pageTables[pid] = pt;
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
    /// </summary>
    public TranslateOutcome Translate(int pid, int vaValue, out int pa)
    {
        pa = -1;
        _step++;

        if (!_pageTables.TryGetValue(pid, out var pt))
        {
            var ev = new TraceEvent(_step, pid, $"0x{vaValue:X8}", null,
                TranslateOutcome.OutOfRange, $"unknown pid {pid}");
            _trace.Add(ev);
            return TranslateOutcome.OutOfRange;
        }

        var va = new VirtualAddress(vaValue);
        if (va.Vpn < 0 || va.Vpn >= pt.Length)
        {
            var ev = new TraceEvent(_step, pid, va.ToString(), null,
                TranslateOutcome.OutOfRange, $"vpn {va.Vpn} out of range");
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
        var pte = pt.Get(va.Vpn);
        if (!pte.Valid)
        {
            var ev = new TraceEvent(_step, pid, va.ToString(), null,
                TranslateOutcome.PageFault, $"vpn {va.Vpn} not mapped (no swap in slice 14.1)");
            _trace.Add(ev);
            return TranslateOutcome.PageFault;
        }

        // OSEP §19.1 line 18: TLB_Insert after successful page-table walk.
        Tlb?.Insert(pid, va.Vpn, pte.FrameNo);

        pa = pte.FrameNo * VirtualAddress.PAGE_SIZE + va.Offset;
        var paFromPt = new PhysicalAddress(pa);
        var ptHit = new TraceEvent(_step, pid, va.ToString(), paFromPt.ToString(),
            TranslateOutcome.Hit, $"pt-walk frame={pte.FrameNo}");
        _trace.Add(ptHit);
        return TranslateOutcome.Hit;
    }

    /// <summary>Map <paramref name="vpn"/> of <paramref name="pid"/> to <paramref name="frameNo"/>.</summary>
    public void Map(int pid, int vpn, int frameNo)
    {
        if (!_pageTables.TryGetValue(pid, out var pt))
            throw new InvalidOperationException($"pid {pid} not found");
        if (frameNo < 0 || frameNo >= Memory.NumFrames)
            throw new ArgumentOutOfRangeException(nameof(frameNo));
        var pte = new Pte { Valid = true, FrameNo = frameNo };
        pt.Set(vpn, pte);
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
        return new PagerStats(_step, hits, faults, oor, _pageTables.Count, _trace.Count);
    }
}

public sealed record PagerStats(int TotalAccesses, int Hits, int Faults, int OutOfRange, int Processes, int TraceEvents);
