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
    /// Translate <paramref name="va"/> for the given process.
    /// Returns the outcome; if <see cref="TranslateOutcome.Hit"/>
    /// the <paramref name="pa"/> parameter is set.
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

        var pte = pt.Get(va.Vpn);
        if (!pte.Valid)
        {
            var ev = new TraceEvent(_step, pid, va.ToString(), null,
                TranslateOutcome.PageFault, $"vpn {va.Vpn} not mapped (no swap in slice 14.1)");
            _trace.Add(ev);
            return TranslateOutcome.PageFault;
        }

        pa = pte.FrameNo * VirtualAddress.PAGE_SIZE + va.Offset;
        var paStruct = new PhysicalAddress(pa);
        var hit = new TraceEvent(_step, pid, va.ToString(), paStruct.ToString(),
            TranslateOutcome.Hit, $"frame={pte.FrameNo}");
        _trace.Add(hit);
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
        return new PagerStats(_step, hits, faults, oor, _pageTables.Count);
    }
}

public sealed record PagerStats(int TotalAccesses, int Hits, int Faults, int OutOfRange, int Processes);
