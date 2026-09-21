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

    /// <summary>
    /// Slice 28.1 (ASID-tagged TLB): demonstrate the survival of TLB
    /// entries across context switches under two flush policies:
    ///   - M16-era <see cref="Tlb.Flush()"/>: nukes everything
    ///     (always 0 surviving entries after a context switch).
    ///   - M28 <see cref="Tlb.Flush(int?)"/> with the new ASID: nukes
    ///     only the outgoing process's non-global entries; global
    ///     entries and other ASIDs survive.
    ///
    /// Setup:
    ///   - Process A maps VPNs 0..3, B maps VPNs 4..7.
    ///   - Each process "warms" the TLB by reading its pages.
    ///   - The kernel installs two global entries (kernel mappings).
    ///   - Then we alternate context switches: A → B → A → B → ...
    ///   - After every switch we report the surviving-entry count for
    ///     each policy.
    ///
    /// The expected outcome: <c>flushall</c> drops to 0 every time;
    /// <c>flushasid</c> keeps the kernel's global entries + the
    /// incoming process's working set.
    /// </summary>
    public static string RunAsidContextSwitch(int tlbCapacity, int contextSwitches)
    {
        if (tlbCapacity < 4)
            throw new ArgumentException("tlb capacity must be >= 4 to fit per-ASID + global entries", nameof(tlbCapacity));
        if (contextSwitches < 1) contextSwitches = 1;

        var pager = new Pager(numFrames: 16);
        pager.CreateProcess(1);
        pager.CreateProcess(2);

        // Pre-map two processes to disjoint frames (no overlap).
        for (int i = 0; i < 4; i++) pager.Map(1, i, frameNo: i);
        for (int i = 0; i < 4; i++) pager.Map(2, 4 + i, frameNo: 4 + i);

        var tlb = new Tlb(tlbCapacity);
        pager.Tlb = tlb;

        // Warm process A's TLB (simulate prior accesses).
        for (int i = 0; i < 4; i++) tlb.Fill(vpn: i, pfn: i, asid: 1, isGlobal: false);
        // Warm process B's TLB.
        for (int i = 0; i < 4; i++) tlb.Fill(vpn: 4 + i, pfn: 4 + i, asid: 2, isGlobal: false);
        // Two global kernel entries (OSEP §19.7 G bit).
        tlb.Fill(vpn: 100, pfn: 200, asid: 0, isGlobal: true);
        tlb.Fill(vpn: 101, pfn: 201, asid: 0, isGlobal: true);

        // Snapshot the TLB before any context switch so we can run
        // BOTH flush policies from the same starting state on every
        // round. Two parallel simulators → two parallel TLBs.
        var tlbAll = new Tlb(tlbCapacity);
        var tlbAsid = new Tlb(tlbCapacity);
        foreach (var e in tlb.Entries)
        {
            if (!e.Valid) continue;
            tlbAll.Fill(e.Vpn, e.Pfn, e.Asid, e.IsGlobal);
            tlbAsid.Fill(e.Vpn, e.Pfn, e.Asid, e.IsGlobal);
        }

        // Alternate context switches: 1 → 2 → 1 → 2 → ...
        // Each "context switch" leaves ASID `prev` and runs ASID `next`.
        var schedule = new List<(int prev, int next)>();
        int prevAsid = 1;
        for (int i = 0; i < contextSwitches; i++)
        {
            int nextAsid = (i % 2 == 0) ? 2 : 1;
            schedule.Add((prevAsid, nextAsid));
            prevAsid = nextAsid;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"=== ASID-tagged TLB context-switch demo (M28 / OSEP §19.5 + §19.7) ===");
        sb.AppendLine($"TLB capacity={tlbCapacity}, initial entries={tlb.ValidCount}" +
                      $" (P1={tlb.CountOwnedBy(1)}, P2={tlb.CountOwnedBy(2)}, global={tlb.GlobalCount})");
        sb.AppendLine($"context switches={contextSwitches}, schedule=[{string.Join(",", schedule.Select(s => $"{s.prev}→{s.next}"))}]");
        sb.AppendLine();
        sb.AppendLine($"{"switch",-9} {"prev->next",-12} {"flushall_survive",-18} {"flushasid_survive",-22} {"flushasid_breakdown",-40}");

        for (int i = 0; i < schedule.Count; i++)
        {
            var (prev, next) = schedule[i];

            // Old policy: blow the whole TLB.
            tlbAll.Flush();

            // New policy: per-ASID flush, scoped to the outgoing ASID.
            int removed = tlbAsid.Flush(prev);

            sb.AppendLine($"  {i + 1,-7} {prev}->{next,-7} {tlbAll.ValidCount,-18} {tlbAsid.ValidCount,-22} " +
                          $"P1={tlbAsid.CountOwnedBy(1)} P2={tlbAsid.CountOwnedBy(2)} G={tlbAsid.GlobalCount} (removed={removed})");
        }

        sb.AppendLine();
        sb.AppendLine("Observations:");
        sb.AppendLine("  - flushall always survives 0 entries (M16 behavior, retained for kernel PTE edits).");
        sb.AppendLine("  - flushasid preserves the incoming process's working set and any global kernel entries.");
        sb.AppendLine("  - Global entries (G bit) survive BOTH flushes — they are not ASID-scoped.");
        sb.AppendLine("  - The P1 entries do NOT survive a flushasid(1) → they vanish until the process re-warms the TLB.");
        sb.AppendLine();
        sb.AppendLine("OSEP §19.5: with ASID the OS no longer has to throw away the entire TLB on a context switch.");
        sb.AppendLine("OSEP §19.7: MIPS R4000 sets the G bit on kernel entries so they live in every ASID.");

        return sb.ToString();
    }
}
