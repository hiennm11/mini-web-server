namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// A simulated job for the user-space scheduler. Holds the burst
/// profile (how much CPU the job still needs), the job's current
/// state, and the queue level (for MLFQ). Immutable per-tick from
/// the scheduler's view; the scheduler mutates BurstRemaining and
/// CurrentQueue as it runs the job.
///
/// OSEP §4.2 / §8.2 "Job" abstraction.
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
    /// demotes the job on every slice expiry. When true, the job
    /// stays in its current queue.
    /// </summary>
    public bool YieldsEarly { get; init; }

    public Job(int id, string name, int burstTotal, bool yieldsEarly = false)
    {
        Id = id;
        Name = name;
        BurstTotal = burstTotal;
        BurstRemaining = burstTotal;
        CurrentQueue = 0;  // MLFQ: all new jobs enter at the top
        YieldsEarly = yieldsEarly;
    }

    public override string ToString() => $"J{Id}({Name},rem={BurstRemaining})";
}

public enum JobState
{
    Pending,    // not yet admitted to the run queue
    Ready,      // in some run queue, waiting for CPU
    Running,    // currently holding the CPU
    Done,       // BurstRemaining == 0
}
