using System.Collections.Generic;
using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// Built-in synthetic workloads for the scheduler demos. These are
/// the classic OSEP §8.6 workloads used to illustrate how MLFQ
/// behaves with a mix of CPU-bound and interactive jobs.
///
/// Each workload is a small DSL — a list of (name, burst, yields)
/// tuples that gets expanded into Jobs.
/// </summary>
public static class Workloads
{
    /// <summary>
    /// One long CPU-bound job and one short interactive job.
    /// Without MLFQ the long job starves the short one.
    /// </summary>
    public static List<Job> TwoJobs()
    {
        return new List<Job>
        {
            new Job(1, "cpu-bound", burstTotal: 100, yieldsEarly: false),
            new Job(2, "interactive", burstTotal: 10, yieldsEarly: true),
        };
    }

    /// <summary>
    /// Two long CPU-bound jobs that compete for the CPU. MLFQ
    /// should keep them at roughly the same priority.
    /// </summary>
    public static List<Job> TwoCpuBound()
    {
        return new List<Job>
        {
            new Job(1, "A", burstTotal: 50, yieldsEarly: false),
            new Job(2, "B", burstTotal: 50, yieldsEarly: false),
        };
    }

    /// <summary>
    /// Mix of one long CPU-bound and several short interactive jobs
    /// arriving at different times. The classic MLFQ stress test.
    /// </summary>
    public static List<Job> MixedWorkload()
    {
        return new List<Job>
        {
            new Job(1, "cpu-A", burstTotal: 80, yieldsEarly: false),
            new Job(2, "inter-B", burstTotal: 8, yieldsEarly: true),
            new Job(3, "inter-C", burstTotal: 6, yieldsEarly: true),
            new Job(4, "cpu-D", burstTotal: 80, yieldsEarly: false),
            new Job(5, "inter-E", burstTotal: 4, yieldsEarly: true),
        };
    }
}

/// <summary>
/// Helper to run a workload through a scheduler and return the
/// trace as plain text. Used by the HTTP route and the smokes.
/// </summary>
public static class SchedulerRunner
{
    public static string RunMlfq(IEnumerable<Job> jobs, int totalTicks, int numQueues = 4,
        int[]? sliceTicks = null, int boostEvery = 50)
    {
        var mlfq = new Mlfq(numQueues, sliceTicks, boostEvery);
        foreach (var j in jobs) mlfq.Enqueue(j);

        var sb = new StringBuilder();
        sb.AppendLine($"=== MLFQ run: {numQueues} queues, slices=[{string.Join(",", mlfq.SliceTicks)}], boost every {boostEvery} ticks ===");
        sb.AppendLine();
        // Snapshot the trace length before each tick so we can print
        // all events generated this tick (boost + dispatch + running/
        // preempt/finish/idle) in the order they occurred.
        int traceStart = 0;
        for (int i = 0; i < totalTicks; i++)
        {
            mlfq.Tick();
            while (traceStart < mlfq.Trace.Count)
            {
                sb.AppendLine(mlfq.Trace[traceStart].Format());
                traceStart++;
            }
        }
        var s = mlfq.Stats();
        sb.AppendLine();
        sb.AppendLine($"=== stats: ticks={s.TotalTicks} finished={s.Finished} ready={s.StillReady} running={s.StillRunning} trace_events={s.TraceEvents} ===");
        return sb.ToString();
    }
}
