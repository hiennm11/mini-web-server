using System.Collections.Generic;
using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// One row of the per-CPU timeline. After the simulator finishes,
/// each row contains the ordered list of "who ran on this CPU at
/// each tick", with empty cells for idle ticks.
///
/// OSEP Ch. 10 timeline representation.
/// </summary>
public sealed class CpuTimelineRow
{
    public int CpuId { get; init; }
    public List<string> Cells { get; } = new();  // one entry per tick; "" = idle

    public string Format()
    {
        var sb = new StringBuilder();
        sb.Append($"CPU {CpuId,-2} |");
        foreach (var c in Cells) sb.Append($" {c,3} |");
        return sb.ToString();
    }
}

/// <summary>
/// Multi-CPU scheduler (OSEP Ch. 10).
///
/// OSEP §10.4 Single-Queue Multiprocessor Scheduling (SQMS):
///   "The most basic approach is to simply reuse the basic framework
///    for single processor scheduling, by putting all jobs that need
///    to be scheduled into a single queue ... However, SQMS has obvious
///    shortcomings. The first problem is a lack of scalability ... The
///    second main problem with SQMS is cache affinity."
///
/// OSEP §10.5 Multi-Queue Multiprocessor Scheduling (MQMS):
///   "Some systems opt for multiple queues, e.g., one per CPU. ... When
///    a job enters the system, it is placed on exactly one scheduling
///    queue, according to some heuristic ... it is scheduled essentially
///    independently, thus avoiding the problems of information sharing
///    and synchronization found in the single-queue approach."
///
/// OSEP §10.5 Work stealing:
///   "One basic approach is to use a technique known as work stealing.
///    With a work-stealing approach, a (source) queue that is low on jobs
///    will occasionally peek at another (target) queue ... If the target
///    queue is (notably) more full than the source queue, the source
///    will 'steal' one or more jobs from the target to help balance load."
///
/// Our implementation:
///   - N CPUs, one Tick() = one global time step. At each tick, every CPU
///     picks one job to run (or idles if its queue is empty).
///   - Three modes: SQMS, MQMS, MQMS-WorkStealing.
///   - Each job records LastCpu (the CPU it last ran on). Work-stealing
///     prefers to keep jobs on the same CPU (cache affinity, OSEP §10.3).
///   - Assignment: initial round-robin into MQMS queues; SQMS uses one
///     global queue.
/// </summary>
public enum MultiCpuMode
{
    /// <summary>Single-queue multiprocessor scheduling (OSEP §10.4).</summary>
    Sqms,

    /// <summary>Multi-queue multiprocessor scheduling (OSEP §10.5).</summary>
    Mqms,

    /// <summary>Multi-queue + work stealing (OSEP §10.5).</summary>
    MqmsWorkStealing,
}

public sealed class MultiCpuScheduler
{
    private readonly List<Job> _allJobs;
    private readonly Queue<Job> _globalQueue;       // SQMS
    private readonly List<Queue<Job>> _perCpuQueue;  // MQMS
    private readonly int _cpuCount;
    private readonly MultiCpuMode _mode;
    private readonly int _workStealPeekInterval;
    private int _tick;
    private readonly Random _rng;

    public MultiCpuScheduler(
        IEnumerable<Job> jobs,
        int cpuCount,
        MultiCpuMode mode,
        int workStealPeekInterval = 5,
        int seed = 42)
    {
        if (cpuCount < 1) throw new ArgumentOutOfRangeException(nameof(cpuCount));
        _allJobs = new List<Job>(jobs);
        _cpuCount = cpuCount;
        _mode = mode;
        _workStealPeekInterval = workStealPeekInterval;
        _rng = new Random(seed);

        _globalQueue = new Queue<Job>();
        _perCpuQueue = new List<Queue<Job>>(cpuCount);
        for (int i = 0; i < cpuCount; i++) _perCpuQueue.Add(new Queue<Job>());

        // Distribute jobs initially. SQMS: all in one queue. MQMS: round-robin.
        foreach (var j in _allJobs)
        {
            j.LastCpu = -1;
            j.MigratedCount = 0;
            if (_mode == MultiCpuMode.Sqms)
            {
                _globalQueue.Enqueue(j);
            }
            else
            {
                int cpu = _allJobs.IndexOf(j) % cpuCount;
                _perCpuQueue[cpu].Enqueue(j);
                j.LastCpu = cpu;
            }
        }
    }

    public IReadOnlyList<Job> Jobs => _allJobs;
    public int CpuCount => _cpuCount;
    public MultiCpuMode Mode => _mode;

    /// <summary>
    /// One tick: every CPU picks a job (or idles). For MQMS work-stealing,
    /// work-steal happens once every `workStealPeekInterval` ticks at
    /// each CPU that finds its own queue empty.
    /// </summary>
    public List<(int Cpu, Job? Job)> Tick()
    {
        _tick++;
        var dispatched = new List<(int, Job?)>();

        for (int cpu = 0; cpu < _cpuCount; cpu++)
        {
            Job? picked = null;

            if (_mode == MultiCpuMode.Sqms)
            {
                // OSEP §10.4: take from front of the global queue.
                if (_globalQueue.Count > 0) picked = _globalQueue.Dequeue();
            }
            else
            {
                // MQMS (§10.5): try own queue first.
                if (_perCpuQueue[cpu].Count > 0)
                {
                    picked = _perCpuQueue[cpu].Dequeue();
                }
                else if (_mode == MultiCpuMode.MqmsWorkStealing && _tick % _workStealPeekInterval == 0)
                {
                    // Work stealing: peek at peer queues; steal from fullest.
                    int srcCpu = -1;
                    int maxLen = 1;  // only steal if peer has at least 2
                    for (int other = 0; other < _cpuCount; other++)
                    {
                        if (other == cpu) continue;
                        int len = _perCpuQueue[other].Count;
                        if (len > maxLen)
                        {
                            maxLen = len;
                            srcCpu = other;
                        }
                    }
                    if (srcCpu >= 0)
                    {
                        picked = _perCpuQueue[srcCpu].Dequeue();
                        if (picked is not null)
                        {
                            picked.LastCpu = cpu;
                            picked.MigratedCount++;
                        }
                    }
                }
            }

            if (picked is null)
            {
                dispatched.Add((cpu, null));  // idle
                continue;
            }

            picked.BurstRemaining--;
            picked.LastCpu = cpu;
            if (picked.BurstRemaining == 0)
            {
                picked.State = JobState.Done;
            }
            else
            {
                // Re-enqueue at the tail (simple round-robin within queue).
                if (_mode == MultiCpuMode.Sqms) _globalQueue.Enqueue(picked);
                else _perCpuQueue[cpu].Enqueue(picked);
            }
            dispatched.Add((cpu, picked));
        }
        return dispatched;
    }

    /// <summary>
    /// Run the simulator until all jobs are done or `maxTicks` reached.
    /// Returns a formatted report including the per-CPU timeline
    /// (OSEP §10.4 "possible job schedule across CPUs" diagram).
    /// </summary>
    public string Run(int maxTicks)
    {
        var timeline = new List<CpuTimelineRow>();
        for (int c = 0; c < _cpuCount; c++)
            timeline.Add(new CpuTimelineRow { CpuId = c });

        bool allDone = false;
        int tickUsed = 0;
        for (int t = 0; t < maxTicks; t++)
        {
            tickUsed = t + 1;
            var dispatched = Tick();
            for (int c = 0; c < _cpuCount; c++)
            {
                var (cpu, job) = dispatched[c];
                timeline[c].Cells.Add(job?.Name ?? ".");
            }
            // Check if all done.
            allDone = true;
            foreach (var j in _allJobs) if (j.BurstRemaining > 0) { allDone = false; break; }
            if (allDone) break;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"=== Multi-CPU scheduler (M13.3 / OSEP Ch. 10) ===");
        sb.AppendLine($"mode: {_mode}  cpus: {_cpuCount}  jobs: {_allJobs.Count}");
        if (_mode == MultiCpuMode.MqmsWorkStealing)
            sb.AppendLine($"work-steal peek interval: every {_workStealPeekInterval} ticks");
        sb.AppendLine();
        sb.AppendLine("jobs:");
        foreach (var j in _allJobs)
            sb.AppendLine($"  J{j.Id} {j.Name}: burst={j.BurstTotal} (last-cpu={j.LastCpu}, migrations={j.MigratedCount})");
        sb.AppendLine();

        sb.AppendLine($"=== timeline ({tickUsed} ticks) ===");
        // Header
        sb.Append("tick    |");
        for (int t = 0; t < tickUsed; t++) sb.Append($" {t,3} |");
        sb.AppendLine();
        foreach (var row in timeline) sb.AppendLine(row.Format());
        sb.AppendLine();

        sb.AppendLine("=== final state ===");
        int totalBurst = 0;
        foreach (var j in _allJobs)
        {
            int ran = j.BurstTotal - j.BurstRemaining;
            totalBurst += j.BurstTotal;
            double pct = j.BurstTotal > 0 ? 100.0 * ran / j.BurstTotal : 0;
            sb.AppendLine($"  J{j.Id} {j.Name}: ran={ran}/{j.BurstTotal} ({pct:F1}%) last-cpu={j.LastCpu} migrations={j.MigratedCount}");
        }
        // Total ticks used vs total work units. With N CPUs and no idle, ticks used = ceil(totalBurst / N).
        int minIdealTicks = (int)Math.Ceiling((double)totalBurst / _cpuCount);
        double utilization = minIdealTicks > 0 ? 100.0 * minIdealTicks / tickUsed : 0;
        sb.AppendLine();
        sb.AppendLine($"=== metrics ===");
        sb.AppendLine($"total work units: {totalBurst}");
        sb.AppendLine($"cpus: {_cpuCount}");
        sb.AppendLine($"ideal ticks (perfect parallelism): {minIdealTicks}");
        sb.AppendLine($"actual ticks used: {tickUsed}");
        sb.AppendLine($"parallel efficiency: {utilization:F1}%");
        return sb.ToString();
    }
}
