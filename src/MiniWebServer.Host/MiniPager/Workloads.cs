using System.Collections.Generic;
using System.Text;

namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Built-in synthetic workloads for the pager demos. Mirror the
/// access patterns OSEP §18 + §19 use to illustrate paging and TLB
/// behavior.
/// </summary>
public static class Workloads
{
    /// <summary>One process, 16 sequential page accesses spanning 4 pages.</summary>
    public static List<MemoryAccess> SequentialSingleProcess()
    {
        var list = new List<MemoryAccess>();
        for (int i = 0; i < 16; i++)
        {
            list.Add(new MemoryAccess(Pid: 1, VirtualAddressValue: i * 256, Kind: AccessKind.Read));
        }
        return list;
    }

    /// <summary>One process, random access across 8 distinct pages (repeats allowed).</summary>
    public static List<MemoryAccess> RandomSingleProcess(int seed = 42)
    {
        var rng = new Random(seed);
        var list = new List<MemoryAccess>();
        for (int i = 0; i < 20; i++)
        {
            int vpn = rng.Next(0, 8);
            int off = rng.Next(0, VirtualAddress.PAGE_SIZE);
            list.Add(new MemoryAccess(Pid: 1, VirtualAddressValue: (vpn << 12) | off, Kind: AccessKind.Read));
        }
        return list;
    }

    /// <summary>Two processes, each touching 4 distinct pages, mostly overlapping.</summary>
    public static List<MemoryAccess> TwoProcessesOverlap()
    {
        var list = new List<MemoryAccess>();
        for (int i = 0; i < 4; i++)
        {
            list.Add(new MemoryAccess(1, (i << 12) | 0, AccessKind.Read));
            list.Add(new MemoryAccess(2, (i << 12) | 0, AccessKind.Read));
        }
        for (int i = 0; i < 4; i++)
        {
            list.Add(new MemoryAccess(1, (i << 12) | 0, AccessKind.Read));
        }
        return list;
    }

    /// <summary>
    /// OSEP §19.2 "Example: Accessing An Array" — the canonical TLB
    /// stress test. 10 array elements, 4 bytes each, starting at VA
    /// 100. With 16-byte pages (OSEP's example), the array spans
    /// across 3 pages (4 ints on VPN=6, 4 on VPN=7, 2 on VPN=8) and
    /// gets 70% hit rate.
    ///
    /// We map this onto our 4 KB pages by using a larger element size
    /// (so each element lives on its own page, like the OSEP example).
    /// To get realistic TLB hit rates, the workload RE-ACCESSES the
    /// same pages multiple times. With 10 elements spread across 10
    /// pages and a TLB of 4 slots, the first 4 access miss, then
    /// Random replacement evicts, so subsequent accesses also mostly
    /// miss unless we re-walk.
    /// </summary>
    public static List<MemoryAccess> ArrayAccessOsep(int numElements = 10, int elementSize = 4096, int seed = 42)
    {
        // 10 elements, each on its own page (elementSize = page size).
        var rng = new Random(seed);
        var list = new List<MemoryAccess>();

        // OSEP §19.2 pattern: "an int array, 10 elements, 4 bytes each,
        // at VA 100". In OSEP's tiny example (16-byte pages), the array
        // spans 3 pages. In our 4 KB page setup, we make each element
        // live on its own page to reproduce the cross-page pattern.
        // First, access each page once (cold misses; TLB fills up).
        var coldAccesses = new List<MemoryAccess>();
        for (int i = 0; i < numElements; i++)
        {
            coldAccesses.Add(new MemoryAccess(
                Pid: 1,
                VirtualAddressValue: 0x100 + i * elementSize,
                Kind: AccessKind.Read));
        }
        list.AddRange(coldAccesses);

        // Then, re-access a small random subset (the "hot" set).
        // With Random TLB replacement and the hot set smaller than TLB
        // capacity, the second pass should hit more often than miss.
        int hotSize = Math.Min(4, numElements);  // hot set = TLB size
        var hotIndices = new HashSet<int>();
        for (int i = 0; i < hotSize; i++) hotIndices.Add(rng.Next(numElements));
        for (int r = 0; r < 20; r++)  // 20 re-accesses
        {
            int idx = rng.Next(numElements);
            if (hotIndices.Contains(idx))
            {
                list.Add(new MemoryAccess(
                    Pid: 1,
                    VirtualAddressValue: 0x100 + idx * elementSize,
                    Kind: AccessKind.Read));
            }
        }
        return list;
    }

    /// <summary>
    /// OSEP §22.6 "Workload Examples" — accesses MORE pages than physical
    /// memory. This forces evictions and demonstrates the replacement
    /// policy in action. With numFrames=4 and numPages=10, the workload
    /// cycles through 10 pages sequentially, evicting one per access
    /// after the first 4.
    /// </summary>
    public static List<MemoryAccess> ExceedsMemory(int numPages = 10, int passes = 3, int seed = 42)
    {
        var list = new List<MemoryAccess>();
        for (int p = 0; p < passes; p++)
        {
            for (int vpn = 0; vpn < numPages; vpn++)
            {
                list.Add(new MemoryAccess(
                    Pid: 1,
                    VirtualAddressValue: vpn * VirtualAddress.PAGE_SIZE,
                    Kind: AccessKind.Read));
            }
        }
        return list;
    }

    /// <summary>
    /// Slice 19.1: copy-on-write fork scenario.
    /// Process 1 maps VPN=0 to a frame.
    /// Process 2 then "forks" — process 2's VPN=0 is COW-shared with P1's VPN=0.
    /// Both processes read the page (no COW fires on read).
    /// Then process 1 writes — COW fires, allocates a new frame.
    /// Process 2 still sees the original page.
    /// </summary>
    public static (List<MemoryAccess> reads, int pid1, int pid2, int sharedVpn, int writeStep) CowScenario()
    {
        var reads = new List<MemoryAccess>();
        // Initial: P1 maps VPN=0.
        // P2 forks: P2's VPN=0 = COW-shared with P1's VPN=0.
        // Sequence: P1 reads, P2 reads, P1 writes (COW fires), P2 reads.
        reads.Add(new MemoryAccess(Pid: 1, VirtualAddressValue: 0, Kind: AccessKind.Read));
        reads.Add(new MemoryAccess(Pid: 2, VirtualAddressValue: 0, Kind: AccessKind.Read));
        // P1 writes at step 2 — COW triggers (this is a "write step" not
        // a read; the workload helper just returns the reads).
        // P2 reads after the write — should still see the original data.
        reads.Add(new MemoryAccess(Pid: 2, VirtualAddressValue: 0, Kind: AccessKind.Read));
        return (reads, 1, 2, 0, 2);
    }
}

/// <summary>Run a workload against a pager pre-populated with mappings.</summary>
public static class PagerRunner
{
    /// <summary>
    /// Set up the pager with one process and pre-map 4 pages (vpn 0-3)
    /// to frames 0-3. Then run the workload and emit the trace.
    /// </summary>
    public static string RunSingle(IEnumerable<MemoryAccess> accesses, int numFrames = 16, int tlbCapacity = 0, bool twoLevel = false, string policy = "fifo")
    {
        var pager = new Pager(numFrames);
        pager.CreateProcess(1, twoLevel);
        pager.Eviction = MakePolicy(policy);
        for (int i = 0; i < 4; i++) pager.Map(1, i, i);
        return RunInternal(pager, accesses, tlbCapacity);
    }

    public static string RunTwoOverlap(IEnumerable<MemoryAccess> accesses, int numFrames = 16, int tlbCapacity = 0, bool twoLevel = false, string policy = "fifo")
    {
        var pager = new Pager(numFrames);
        pager.CreateProcess(1, twoLevel);
        pager.CreateProcess(2, twoLevel);
        pager.Eviction = MakePolicy(policy);
        for (int i = 0; i < 4; i++) pager.Map(1, i, i);
        for (int i = 0; i < 4; i++) pager.Map(2, i, 4 + i);
        return RunInternal(pager, accesses, tlbCapacity);
    }

    /// <summary>
    /// Slice 18.1 workload: pre-map ALL pages the workload will touch
    /// (no initial eviction needed; evictions happen during the workload
    /// as physical memory fills up).
    /// </summary>
    public static string RunReplacement(IEnumerable<MemoryAccess> accesses, int numFrames = 4, int tlbCapacity = 0, string policy = "fifo")
    {
        var pager = new Pager(numFrames);
        pager.CreateProcess(1);
        pager.Eviction = MakePolicy(policy);
        // No pre-map — let the workload allocate frames dynamically via
        // Pager.Map(vpn=-1). The first numFrames accesses map to free
        // frames; subsequent accesses trigger evictions.
        return RunInternal(pager, accesses, tlbCapacity);
    }

    /// <summary>
    /// Slice 19.1 COW fork scenario. Demonstrates OSEP §23.1 "Other Neat
    /// Tricks": when a process forks, the OS doesn't copy the address
    /// space; instead, it shares all the pages as copy-on-write.
    /// The first write triggers a copy.
    ///
    /// Steps:
    ///   1. P1 maps VPN=0 (first-touch Map).
    ///   2. P1 reads VPN=0 (Hit).
    ///   3. P2 forks: P2's VPN=0 is COW-shared with P1's VPN=0.
    ///   4. P2 reads VPN=0 (HitReadOnly — shared).
    ///   5. P1 writes VPN=0 → COW fires: allocate new frame, copy
    ///      old contents, mark P1's PTE as writable.
    ///   6. P2 reads VPN=0 again (Hit — but on P2's original frame,
    ///      NOT P1's new private frame).
    /// </summary>
    public static string RunCow(int numFrames = 4)
    {
        var pager = new Pager(numFrames);
        pager.CreateProcess(1);
        pager.CreateProcess(2);

        var sb = new StringBuilder();
        sb.AppendLine($"=== COW fork scenario (M19 / OSEP §23.1) ===");
        sb.AppendLine($"numFrames={numFrames}");
        sb.AppendLine();

        // Step 1: P1 maps VPN=0.
        pager.Map(1, 0, frameNo: -1);
        sb.AppendLine($"step 1: P1 first-touch map VPN=0 (frame={(pager.AllPageTables.OfType<LinearLookup>().First().TryLookup(0, out int fn, out _) == LookupResult.Hit ? fn : -1)})");

        // Read by P1 to ensure frame is populated.
        pager.Translate(1, 0, out _);
        sb.AppendLine("step 2: P1 reads VPN=0 → Hit");

        // Step 3: P2 forks — share the frame.
        pager.ShareFrame(sourcePid: 1, sourceVpn: 0, destPid: 2, destVpn: 0);
        sb.AppendLine("step 3: P2 fork — ShareFrame(P1,0) → (P2,0) COW-shared");

        // Step 4: P2 reads VPN=0 — should be HitReadOnly.
        pager.Translate(2, 0, out _);
        sb.AppendLine("step 4: P2 reads VPN=0 → HitReadOnly (page is shared)");

        // Step 5: P1 writes — triggers COW.
        var writeData = new byte[VirtualAddress.PAGE_SIZE];
        for (int i = 0; i < writeData.Length; i++) writeData[i] = 0x42;
        pager.Write(1, 0, writeData);
        var pt1 = pager.AllPageTables.OfType<LinearLookup>().First();
        pt1.TryLookup(0, out int frame1After, out _);
        var pt2 = pager.AllPageTables.OfType<LinearLookup>().Last();
        pt2.TryLookup(0, out int frame2After, out _);
        sb.AppendLine($"step 5: P1 writes VPN=0 → COW fires; P1.frame={frame1After}, P2.frame={frame2After}");
        sb.AppendLine($"        (frames should differ — P1 got a fresh private frame, P2 keeps the original)");

        // Step 6: P2 reads again — should still see the original frame.
        pager.Translate(2, 0, out _);
        sb.AppendLine("step 6: P2 reads VPN=0 → Hit on original frame");

        // Dump the trace.
        foreach (var ev in pager.Trace)
        {
            sb.AppendLine(ev.Format());
        }
        var s = pager.Stats();
        sb.AppendLine();
        sb.AppendLine($"=== stats: accesses={s.TotalAccesses} hits={s.Hits} faults={s.Faults} outofrange={s.OutOfRange}");
        sb.AppendLine($"=== Replacement stats: evictions={pager.Evictions} swap_ins={pager.SwapIns}");
        return sb.ToString();
    }

    private static IEvictionPolicy MakePolicy(string policy) => policy switch
    {
        "lru" => new LruEviction(),
        "random" => new RandomEviction(),
        _ => new FifoEviction(),
    };

    private static string RunInternal(Pager pager, IEnumerable<MemoryAccess> accesses, int tlbCapacity)
    {
        if (tlbCapacity > 0)
        {
            pager.Tlb = new Tlb(tlbCapacity);
        }

        // Memory savings of 2-level vs linear (OSEP §20.4).
        long linearBytes = 0, twoLevelBytes = 0;
        foreach (var pt in pager.AllPageTables)
        {
            if (pt is TwoLevelLookup tl)
            {
                twoLevelBytes += tl.MemoryBytes;
                linearBytes += (long)tl.Capacity * System.Runtime.InteropServices.Marshal.SizeOf<Pte>();
            }
            else
            {
                linearBytes += pt.MemoryBytes;
            }
        }

        var sb = new StringBuilder();
        var ptMode = pager.AllPageTables.FirstOrDefault() is TwoLevelLookup ? "2-level" : "linear";
        sb.AppendLine($"=== Pager run: {pager.Memory.NumFrames} frames ({pager.Memory.SizeBytes} bytes physical)"
                       + (pager.Tlb is not null ? $", TLB capacity={pager.Tlb.Capacity}" : ", TLB=disabled")
                       + $", PT mode={ptMode}, eviction={pager.Eviction.Name}");
        sb.AppendLine();
        foreach (var a in accesses)
        {
            pager.Translate(a.Pid, a.VirtualAddressValue, out _);
        }
        foreach (var ev in pager.Trace)
        {
            sb.AppendLine(ev.Format());
        }
        var s = pager.Stats();
        var tlbStats = pager.Tlb?.Stats();
        sb.AppendLine();
        sb.AppendLine($"=== stats: accesses={s.TotalAccesses} hits={s.Hits} faults={s.Faults} outofrange={s.OutOfRange} trace_events={s.TraceEvents}");
        if (tlbStats is not null)
        {
            sb.AppendLine($"=== TLB stats: hits={tlbStats.Hits} misses={tlbStats.Misses} evictions={tlbStats.Evictions} hit_rate={tlbStats.HitRate:F3}");
        }
        sb.AppendLine($"=== Replacement stats: evictions={pager.Evictions} swap_ins={pager.SwapIns}");
        sb.AppendLine($"=== Swap usage: {pager.Swap.UsedSlots} of {pager.Swap.Capacity} slots");
        if (pager.AllPageTables.FirstOrDefault() is TwoLevelLookup)
        {
            sb.AppendLine($"=== PT memory: 2-level used {twoLevelBytes:N0} bytes vs linear would use {linearBytes:N0} bytes (saved {linearBytes - twoLevelBytes:N0})");
        }
        return sb.ToString();
    }
}
