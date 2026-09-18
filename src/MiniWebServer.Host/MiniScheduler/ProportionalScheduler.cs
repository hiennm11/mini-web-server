using System.Collections.Generic;
using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// One line of scheduler trace. Simpler than <see cref="TraceEvent"/>
/// (which was designed for MLFQ) — proportional-share schedulers
/// don't have queues or boost events, so a flat string per tick
/// is enough.
/// </summary>
public sealed class SimTraceLine
{
    public int Tick { get; init; }
    public int JobId { get; init; }
    public string JobName { get; init; } = "";
    public string Detail { get; init; } = "";

    public string Format() => $"t={Tick,4}  J{JobId}({JobName})  {Detail}";
}

/// <summary>
/// Stride scheduling (OSEP §9.3 Waldspurger 1995).
///
/// OSEP §9.3 "Stride Scheduling":
///   "Each job in the system has a stride, which is inverse in proportion
///    to the number of tickets it has. ... every time a process runs, we
///    will increment a counter for it (called its pass value) by its
///    stride to track its global progress. The scheduler then uses the
///    stride and pass to determine which process should run next. The
///    basic idea is simple: at any given time, pick the process to run
///    that has the lowest pass value so far; when you run a process,
///    increment its pass counter by its stride."
///
/// OSEP §9.4:
///   "stride = large_constant / tickets"
///
/// Our implementation uses STRIDE_CONST = 10_000. OSEP §9.4 uses 10_000
/// in the worked example.
///
/// OSEP §9.6 "Comparing Stride and Lottery":
///   "Stride scheduling gets them exactly right at the end of each
///    scheduling cycle."
/// </summary>
public sealed class StrideScheduler
{
    private readonly List<Job> _jobs;
    private readonly List<SimTraceLine> _trace = new();
    private int _tick;

    public StrideScheduler(IEnumerable<Job> jobs)
    {
        _jobs = new List<Job>(jobs);
    }

    public IReadOnlyList<SimTraceLine> Trace => _trace;
    public IReadOnlyList<Job> Jobs => _jobs;

    /// <summary>One tick of the scheduler. Picks lowest-pass job, runs it, increments its pass.</summary>
    public SimTraceLine Tick()
    {
        _tick++;
        // Pick job with lowest Pass (OSEP §9.6 Algorithm).
        Job? picked = null;
        long lowestPass = long.MaxValue;
        foreach (var j in _jobs)
        {
            if (j.BurstRemaining <= 0) continue;
            if (j.Pass < lowestPass)
            {
                lowestPass = j.Pass;
                picked = j;
            }
        }
        if (picked is null)
        {
            return new SimTraceLine { Tick = _tick, JobId = 0, JobName = "", Detail = "all jobs complete" };
        }

        picked.BurstRemaining--;
        long prevPass = picked.Pass;
        picked.Pass += picked.Stride;
        var ev = new SimTraceLine
        {
            Tick = _tick,
            JobId = picked.Id,
            JobName = picked.Name,
            Detail = $"pass {prevPass}+stride {picked.Stride}={picked.Pass}, rem={picked.BurstRemaining}",
        };
        _trace.Add(ev);
        if (picked.BurstRemaining == 0) picked.State = JobState.Done;
        return ev;
    }

    /// <summary>Run N ticks. Returns the trace as a formatted string with proportional-share analysis.</summary>
    public string Run(int ticks)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Stride scheduler (M13.2 / OSEP §9.3) ===");
        sb.AppendLine($"jobs: {_jobs.Count}");
        foreach (var j in _jobs)
            sb.AppendLine($"  J{j.Id} {j.Name}: tickets={j.Tickets} stride={j.Stride} burst={j.BurstTotal}");
        sb.AppendLine();
        for (int i = 0; i < ticks; i++) Tick();
        foreach (var ev in _trace) sb.AppendLine(ev.Format());
        sb.AppendLine();
        sb.AppendLine("=== final state ===");
        foreach (var j in _jobs)
        {
            int ran = j.BurstTotal - j.BurstRemaining;
            double pct = j.BurstTotal > 0 ? 100.0 * ran / j.BurstTotal : 0;
            sb.AppendLine($"  J{j.Id} {j.Name}: ran={ran}/{j.BurstTotal} ({pct:F1}%) pass={j.Pass}");
        }
        int totalRan = 0;
        foreach (var j in _jobs) totalRan += j.BurstTotal - j.BurstRemaining;
        sb.AppendLine();
        sb.AppendLine($"=== ticket share vs actual share (total ticks={totalRan}) ===");
        int totalTix = 0;
        foreach (var j in _jobs) totalTix += j.Tickets;
        foreach (var j in _jobs)
        {
            double tixShare = 100.0 * j.Tickets / totalTix;
            int ran = j.BurstTotal - j.BurstRemaining;
            double actualShare = totalRan > 0 ? 100.0 * ran / totalRan : 0;
            sb.AppendLine($"  J{j.Id} {j.Name}: ticket-share={tixShare:F1}%, actual-share={actualShare:F1}%");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Lottery scheduling (OSEP §9.1-9.3 Waldspurger &amp; Weihl 1994).
///
/// OSEP §9.1 "Lottery":
///   "The scheduler must know how many total tickets there are...
///    The scheduler then picks a winning ticket, which is a number
///    from 0 to [total-1]."
///
/// OSEP §9.3 "Implementation" (Figure 9.1):
///   "To make a scheduling decision, we first have to pick a random
///    number (the winner) from the total number of tickets ... Then, we
///    simply traverse the list, with a simple counter used to help us
///    find the winner."
///
/// OSEP §9.4 fairness study:
///   "when the job length is not very long, average fairness can be
///    quite low. Only as the jobs run for a significant number of time
///    slices does the lottery scheduler approach the desired fair outcome."
/// </summary>
public sealed class LotteryScheduler
{
    private readonly List<Job> _jobs;
    private readonly List<SimTraceLine> _trace = new();
    private readonly Random _rng;
    private int _tick;

    public LotteryScheduler(IEnumerable<Job> jobs, int seed = 42)
    {
        _jobs = new List<Job>(jobs);
        _rng = new Random(seed);
    }

    public IReadOnlyList<SimTraceLine> Trace => _trace;
    public IReadOnlyList<Job> Jobs => _jobs;

    public SimTraceLine Tick()
    {
        _tick++;
        int totalTix = 0;
        foreach (var j in _jobs) if (j.BurstRemaining > 0) totalTix += j.Tickets;
        if (totalTix == 0)
        {
            return new SimTraceLine { Tick = _tick, JobId = 0, JobName = "", Detail = "all jobs complete" };
        }
        // OSEP §9.3 Figure 9.1: pick winner ticket, walk list.
        int winner = _rng.Next(totalTix);
        int counter = 0;
        Job? picked = null;
        foreach (var j in _jobs)
        {
            if (j.BurstRemaining <= 0) continue;
            counter += j.Tickets;
            if (counter > winner) { picked = j; break; }
        }
        if (picked is null) picked = _jobs[0];  // fallback

        picked.BurstRemaining--;
        var ev = new SimTraceLine
        {
            Tick = _tick,
            JobId = picked.Id,
            JobName = picked.Name,
            Detail = $"winner ticket={winner}, rem={picked.BurstRemaining}",
        };
        _trace.Add(ev);
        if (picked.BurstRemaining == 0) picked.State = JobState.Done;
        return ev;
    }

    public string Run(int ticks)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Lottery scheduler (M13.2 / OSEP §9.1) ===");
        sb.AppendLine($"jobs: {_jobs.Count}");
        foreach (var j in _jobs)
            sb.AppendLine($"  J{j.Id} {j.Name}: tickets={j.Tickets} burst={j.BurstTotal}");
        sb.AppendLine();
        for (int i = 0; i < ticks; i++) Tick();
        foreach (var ev in _trace) sb.AppendLine(ev.Format());
        sb.AppendLine();
        sb.AppendLine("=== final state ===");
        foreach (var j in _jobs)
        {
            int ran = j.BurstTotal - j.BurstRemaining;
            double pct = j.BurstTotal > 0 ? 100.0 * ran / j.BurstTotal : 0;
            sb.AppendLine($"  J{j.Id} {j.Name}: ran={ran}/{j.BurstTotal} ({pct:F1}%)");
        }
        int totalRan = 0;
        foreach (var j in _jobs) totalRan += j.BurstTotal - j.BurstRemaining;
        sb.AppendLine();
        sb.AppendLine($"=== ticket share vs actual share (total ticks={totalRan}) ===");
        int totalTix = 0;
        foreach (var j in _jobs) totalTix += j.Tickets;
        foreach (var j in _jobs)
        {
            double tixShare = 100.0 * j.Tickets / totalTix;
            int ran = j.BurstTotal - j.BurstRemaining;
            double actualShare = totalRan > 0 ? 100.0 * ran / totalRan : 0;
            sb.AppendLine($"  J{j.Id} {j.Name}: ticket-share={tixShare:F1}%, actual-share={actualShare:F1}%");
        }
        return sb.ToString();
    }
}
