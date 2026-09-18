using System.Collections.Generic;
using System.Text;

namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Built-in synthetic workloads for the pager demos. Mirror the
/// access patterns OSEP §18 uses to illustrate paging: a single
/// process reading sequentially, two processes sharing frames,
/// and a random-access workload that exercises the page table.
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
        // Then re-touch pid 1's pages to test hits
        for (int i = 0; i < 4; i++)
        {
            list.Add(new MemoryAccess(1, (i << 12) | 0, AccessKind.Read));
        }
        return list;
    }
}

/// <summary>Run a workload against a pager pre-populated with mappings.</summary>
public static class PagerRunner
{
    /// <summary>
    /// Set up the pager with two processes and pre-map 4 pages each
    /// (pid 1 -> frames 0-3, pid 2 -> frames 4-7). Then run the
    /// workload. Returns the formatted trace plus summary stats.
    /// </summary>
    public static string RunSingle(IEnumerable<MemoryAccess> accesses, int numFrames = 16)
    {
        var pager = new Pager(numFrames);
        var pt = pager.CreateProcess(1);
        // Pre-map 4 pages (vpn 0-3) of pid 1 to frames 0-3.
        for (int i = 0; i < 4; i++) pager.Map(1, i, i);
        return RunInternal(pager, accesses);
    }

    public static string RunTwoOverlap(IEnumerable<MemoryAccess> accesses, int numFrames = 16)
    {
        var pager = new Pager(numFrames);
        pager.CreateProcess(1);
        pager.CreateProcess(2);
        for (int i = 0; i < 4; i++) pager.Map(1, i, i);
        for (int i = 0; i < 4; i++) pager.Map(2, i, 4 + i);
        return RunInternal(pager, accesses);
    }

    private static string RunInternal(Pager pager, IEnumerable<MemoryAccess> accesses)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Pager run: {pager.Memory.NumFrames} frames ({pager.Memory.SizeBytes} bytes physical) ===");
        sb.AppendLine();
        foreach (var a in accesses)
        {
            pager.Translate(a.Pid, a.VirtualAddressValue, out _);
        }
        // Print all trace events
        foreach (var ev in pager.Trace)
        {
            sb.AppendLine(ev.Format());
        }
        var s = pager.Stats();
        sb.AppendLine();
        sb.AppendLine($"=== stats: accesses={s.TotalAccesses} hits={s.Hits} faults={s.Faults} outofrange={s.OutOfRange} processes={s.Processes} ===");
        return sb.ToString();
    }
}
