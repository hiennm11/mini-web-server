namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// A simulated job for the user-space scheduler. Holds the burst
/// profile (how much CPU the job still needs), the job's current
/// state, and the queue level (for MLFQ). Immutable per-tick from
/// the scheduler's view; the scheduler mutates BurstRemaining and
/// CurrentQueue as it runs the job.
///
/// OSEP §8.2 / §8.4 "Job" abstraction.
/// Slice 13.2.1: Tickets + Stride + Pass for proportional-share schedulers.
/// </summary>
public sealed class Job
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int BurstTotal { get; init; }
    public int BurstRemaining { get; set; }
    public int CurrentQueue { get; set; }
    public JobState State { get; set; } = JobState.Pending;

    /// <summary>
    /// True if the job yields CPU before exhausting its time slice
    /// (an "interactive" hint for MLFQ). When false, the scheduler
    /// demotes the job to lower queue. When true, the job stays in
    /// its current queue.
    /// </summary>
    public bool YieldsEarly { get; init; }

    /// <summary>Slice 13.2.1: Lottery tickets (OSEP §9.1) — share of CPU.</summary>
    public int Tickets { get; init; } = 100;

    /// <summary>Slice 13.2.1: Stride = STRIDE_CONST / Tickets (OSEP §9.3).</summary>
    public int Stride { get; set; }

    /// <summary>Slice 13.2.1: Stride pass counter (OSEP §9.3, increments by Stride per run).</summary>
    public long Pass { get; set; }

    /// <summary>Slice 13.3.1: Last CPU the job ran on (OSEP §10.3 cache affinity).</summary>
    public int LastCpu { get; set; } = -1;

    /// <summary>Slice 13.3.1: Number of times this job migrated between CPUs.</summary>
    public int MigratedCount { get; set; }

    public Job(int id, string name, int burstTotal, bool yieldsEarly = false, int tickets = 100)
    {
        Id = id;
        Name = name;
        BurstTotal = burstTotal;
        BurstRemaining = burstTotal;
        CurrentQueue = 0;  // MLFQ: all new jobs enter at the top
        YieldsEarly = yieldsEarly;
        Tickets = tickets;
        Stride = 10000 / Math.Max(1, tickets);  // OSEP §9.3
        Pass = 0;
    }

    public override string ToString() => $"J{Id}({Name},rem={BurstRemaining},tix={Tickets})";
}

public enum JobState
{
    Pending,    // not yet admitted to the run queue
    Ready,      // in some run queue, waiting for CPU
    Running,    // currently holding the CPU
    Done,       // BurstRemaining == 0
}
