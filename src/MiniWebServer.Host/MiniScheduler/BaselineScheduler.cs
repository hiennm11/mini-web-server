namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// OSTEP Ch. 7 §7.1-§7.7: the scheduling baselines that §8-§10 are improvements
/// over. Slice 35.1.
/// </summary>
/// <remarks>
/// <para>
/// The chapter relaxes five workload assumptions in order, and each policy
/// exists because one of them broke. Recording which assumption a policy needs
/// is the point of this slice: an MLFQ is not "better than FIFO", it is better
/// because it stopped assuming the run-time is known (§7.9).
/// </para>
/// <para>
/// The simulator is discrete-time and knows each job's length, exactly as
/// §7.1 assumes. That assumption is unrealistic and the chapter says so — "the
/// run-time of each job is known. We said many of these assumptions were
/// unrealistic ... it might bother you that the run-time of each job is known:
/// this would make the scheduler omniscient" — but removing it is §7.9's job,
/// and §7.9's answer is §8, which this repo already has.
/// </para>
/// </remarks>
public enum BaselinePolicy
{
    /// <summary>§7.3 FIFO / FCFS. Non-preemptive; no knowledge of run-time.</summary>
    Fifo,

    /// <summary>§7.4 SJF. Non-preemptive; needs run-time known.</summary>
    Sjf,

    /// <summary>§7.5 STCF (PSJF). Preemptive; re-decides on every arrival.</summary>
    Stcf,

    /// <summary>§7.7 Round Robin / time-slicing. Preemptive; optimizes response.</summary>
    RoundRobin,
}

/// <summary>
/// One job in a Ch. 7 workload. Distinct from <see cref="Job"/>: that model
/// exists for MLFQ's queue levels and proportional-share tickets, and it has no
/// arrival time. Response time (§7.6) is defined against arrival, so the
/// baseline workload cannot be expressed without one.
/// </summary>
public sealed class BaselineJob
{
    public string Name { get; init; }
    public int Length { get; init; }

    /// <summary>§7.1 assumption 2, relaxed in §7.4-§7.5.</summary>
    public int Arrival { get; init; }

    public int Remaining { get; set; }
    public int FirstRunAt { get; set; } = -1;
    public int CompletionAt { get; set; } = -1;

    internal BaselineJob(string name, int length, int arrival)
    {
        Name = name;
        Length = length;
        Arrival = arrival;
        Remaining = length;
    }
}

/// <summary>Workload builders for §7's worked examples (slice 35.1).</summary>
public static class BaselineWorkload
{
    /// <summary>
    /// §7.1's five assumptions are: equal run-times, simultaneous arrival,
    /// run-to-completion, CPU-only, and known run-times. The first two are
    /// parameters here; the rest are the simulator's model.
    /// </summary>
    public static List<BaselineJob> FromLengths(
        int[] lengths, int[] arrivals, string[]? names = null)
    {
        if (lengths.Length != arrivals.Length)
            throw new ArgumentException("each length needs an arrival time");
        if (lengths.Length == 0)
            throw new ArgumentException("a workload needs at least one job");

        var jobs = new List<BaselineJob>(lengths.Length);
        for (int i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] < 1) throw new ArgumentException("a job needs at least 1 unit of CPU");
            if (arrivals[i] < 0) throw new ArgumentException("arrival time cannot be negative");
            string name = names is not null && i < names.Length ? names[i] : ((char)('A' + i)).ToString();
            jobs.Add(new BaselineJob(name, lengths[i], arrivals[i]));
        }
        return jobs;
    }

    /// <summary>Figure 7.1: three jobs of 10s, all arriving together.</summary>
    public static List<BaselineJob> EqualLengths(int n, int length = 10) =>
        FromLengths(Enumerable.Repeat(length, n).ToArray(), new int[n],
            Enumerable.Range(0, n).Select(i => ((char)('A' + i)).ToString()).ToArray());

    /// <summary>Figure 7.2: one long job in front of two short ones.</summary>
    public static List<BaselineJob> Convoy(int longest = 100, int shortest = 10) =>
        FromLengths(new[] { longest, shortest, shortest }, new[] { 0, 0, 0 },
            new[] { "A", "B", "C" });

    /// <summary>Figure 7.4: A=100 arrives at 0; B and C=10 arrive at 10.</summary>
    public static List<BaselineJob> LateArrivals(int longest = 100, int shortest = 10, int arrival = 10) =>
        FromLengths(new[] { longest, shortest, shortest }, new[] { 0, arrival, arrival },
            new[] { "A", "B", "C" });

    /// <summary>Figure 7.6: three jobs of 5s, the response-time comparison case.</summary>
    public static List<BaselineJob> ResponseTimeCase() =>
        FromLengths(new[] { 5, 5, 5 }, new[] { 0, 0, 0 }, new[] { "A", "B", "C" });

    /// <summary>
    /// The named workloads the route accepts. Lives here rather than in the
    /// route so the comparison table and the requested run are built from one
    /// definition — two switches over the same names would drift, and the
    /// comparison would silently compare different workloads.
    /// </summary>
    public static List<BaselineJob> FromName(string workload) => workload switch
    {
        "convoy" => Convoy(),
        "latearrivals" => LateArrivals(),
        "equal" => EqualLengths(3),
        "response" => ResponseTimeCase(),
        _ => new List<BaselineJob>(),
    };

    /// <summary>The workload names <see cref="FromName"/> accepts.</summary>
    public static readonly string[] Names = { "convoy", "latearrivals", "equal", "response" };
}

/// <summary>
/// The metrics §7 defines, and one trace to show how they were produced.
/// </summary>
public sealed class ScheduleResult
{
    /// <summary>§7.2 equation 7.1: completion time minus arrival time.</summary>
    public required double AvgTurnaround { get; init; }

    /// <summary>§7.6 equation 7.2: first run minus arrival time.</summary>
    public required double AvgResponse { get; init; }

    /// <summary>Time a job spent runnable but not running. Not in Ch. 7's text.</summary>
    public required double AvgWait { get; init; }

    public required IReadOnlyList<double> Turnaround { get; init; }
    public required IReadOnlyList<double> Response { get; init; }

    /// <summary>
    /// Arrival time per job, keyed by name. <see cref="Turnaround"/> and
    /// <see cref="Response"/> are positional and ordered by input, which is not
    /// the order jobs execute in — a caller rendering one row per job needs to
    /// join by identity, and indexing a positional list by a name-derived index
    /// silently pairs the wrong numbers.
    /// </summary>
    public required IReadOnlyDictionary<string, int> Arrivals { get; init; }

    /// <summary>Job names in the order they finished.</summary>
    public required string CompletionOrder { get; init; }

    /// <summary>Per-tick trace as (time, job name or "-"), for the Gantt display.</summary>
    public required IReadOnlyList<(int Time, string Job)> Trace { get; init; }

    /// <summary>Total ticks the run took, including idle time before the last arrival.</summary>
    public required int Makespan { get; init; }
}

/// <summary>
/// Runs a Ch. 7 policy over a workload. Discrete time, one tick per unit of
/// CPU. Slice 35.1.
/// </summary>
public static class BaselineScheduler
{
    public static ScheduleResult Run(BaselinePolicy policy, List<BaselineJob> jobs,
        int quantum = 1)
    {
        if (jobs.Count == 0) throw new ArgumentException("a workload needs at least one job");
        if (quantum < 1) throw new ArgumentOutOfRangeException(nameof(quantum),
            "a time slice is at least one tick (§7.7: the quantum must be a multiple of the timer period)");
        if (policy != BaselinePolicy.RoundRobin && quantum != 1)
            throw new ArgumentException("only Round Robin uses a time slice; the other policies run to completion");

        // Clone so a run never mutates the caller's workload. The tests build a
        // fresh one per run today, but a caller comparing two policies on one
        // workload would otherwise get a second, silently wrong answer.
        //
        // Two lists, deliberately. `admitted` drains into the run queue, so it
        // is empty once the simulation ends; the metrics need every job, not
        // only the ones that happened to be waiting. Keeping the drained list
        // for `Summarize` is what made `Average()` throw on an empty sequence.
        var all = jobs.Select(j => new BaselineJob(j.Name, j.Length, j.Arrival)).ToList();
        var admitted = all.OrderBy(j => j.Arrival).ThenBy(j => j.Name, StringComparer.Ordinal).ToList();

        var trace = new List<(int, string)>();
        var runQueue = new Queue<BaselineJob>();
        BaselineJob? running = null;
        int sliceLeft = 0;

        // Every tick either runs a job or burns CPU on an idle machine waiting
        // for the next arrival. The idle case matters: §7.6's response time is
        // measured from arrival, so a machine with nothing to run is not the
        // same as a machine that has not started the job yet.
        int now = 0;
        // A workload's tick count bounds *memory*, not just time: `Trace` records one
        // entry per tick, so a job of length int.MaxValue needs ~2 billion
        // entries and throws OutOfMemoryException long before the guard would
        // fire. The horizon is therefore checked against a documented cap
        // rather than against int.MaxValue, and the cap is far below it — the
        // longest workload in this repo is a few hundred ticks, and a trace of
        // millions of entries is unreadable in a route response anyway.
        const int MaxTicks = 1_000_000;

        // Upper bound on iterations: every job's CPU plus the idle span before the
        // last arrival, plus one for the final completion tick. `Max` on an
        // empty sequence throws, so seed it from the first job rather than
        // relying on the caller to have left work in `admitted` — the loop below
        // drains it.
        //
        // Computed in long. In int, a single job of length 2_147_483_647 arriving
        // at 0 makes the bound wrap negative, and the first iteration then fails
        // the guard with "simulation failed to terminate" — rejecting a workload
        // whose answer would have fit exactly in int. `Sum` overflows the same
        // way on two large jobs.
        long guardLimit = admitted.Sum(j => (long)j.Length)
                        + admitted.Select(j => (long)j.Arrival).DefaultIfEmpty(0).Max()
                        + 1;
        if (guardLimit > MaxTicks)
            throw new ArgumentOutOfRangeException(nameof(jobs),
                $"the run needs about {guardLimit} ticks but the simulator's horizon is {MaxTicks}; " +
                "the per-tick trace costs one entry each");
        long guard = 0;

        // Termination is "some job has not completed", not "some job is still
        // waiting to arrive". The admission loop drains `admitted` into the run
        // queue, so after the first tick `admitted` is empty while work is
        // still outstanding - testing `admitted` would exit after one job
        // started and leave the rest with CompletionAt = -1.
        while (all.Any(j => j.CompletionAt < 0))
        {
            if (++guard > guardLimit)
                throw new InvalidOperationException("simulation failed to terminate");

            // Admit everything that has arrived.
            bool admittedAny = false;
            while (admitted.Count > 0 && admitted[0].Arrival <= now && admitted[0].CompletionAt < 0)
            {
                runQueue.Enqueue(admitted[0]);
                admitted.RemoveAt(0);
                admittedAny = true;
            }

            if (running is null && runQueue.Count > 0)
            {
                running = Dequeue(policy, runQueue);
                sliceLeft = quantum;
                if (running.FirstRunAt < 0) running.FirstRunAt = now;
            }
            else if (admittedAny && policy == BaselinePolicy.Stcf)
            {
                // §7.5: "Any time a new job enters the system, the STCF
                // scheduler determines which of the remaining jobs (including
                // the new job) has the least time left, and schedules that one."
                //
                // This is the whole difference from SJF, and it only exists on
                // the arrival edge: SJF and FIFO are non-preemptive (§7.4's
                // ASIDE), so they keep the running job to completion no matter
                // what shows up. Only STCF re-decides, and only because a new
                // job appeared — not every tick, which would make it identical
                // to RR under a quantum of 1.
                BaselineJob incumbent = running!;
                BaselineJob challenger = Dequeue(policy, runQueue);
                if (challenger.Remaining < incumbent.Remaining)
                {
                    runQueue.Enqueue(incumbent);   // preempt: incumbent goes back
                    running = challenger;
                    if (challenger.FirstRunAt < 0) challenger.FirstRunAt = now;
                }
                else
                {
                    runQueue.Enqueue(challenger);
                }
            }

            if (running is null)
            {
                // Nothing runnable. The only way to get here with work
                // outstanding is: the run queue is empty and no further
                // arrival has happened yet. So the clock jumps to the next
                // arrival, and the skipped ticks are idle CPU.
                //
                // Both sources must be consulted. The admission loop above
                // drains `admitted`, so by the time a later job arrives the
                // earlier ones are already in `runQueue` and not in `admitted`
                // any more — looking in one list alone either throws or picks
                // a time that has already passed.
                if (runQueue.Count > 0)
                    throw new InvalidOperationException("a runnable job was left unscheduled");

                int next = admitted.Count > 0 ? admitted[0].Arrival : -1;
                if (next < 0)
                    throw new InvalidOperationException("no runnable job and none waiting to arrive");
                if (next <= now) next = now + 1;

                trace.Add((now, "-"));
                now = next;
                continue;
            }

            running.Remaining--;
            trace.Add((now, running.Name));
            now++;

            if (running.Remaining <= 0)
            {
                running.CompletionAt = now;
                running = null;
            }
            else if (policy == BaselinePolicy.RoundRobin && --sliceLeft <= 0)
            {
                // §7.7: the slice expired, so this job goes to the back.
                runQueue.Enqueue(running);
                running = null;
            }
        }

        return Summarize(policy, all, trace, now);
    }

    private static BaselineJob Dequeue(BaselinePolicy policy, Queue<BaselineJob> runQueue)
    {
        // FIFO and SJF both choose from a queue, but SJF reorders it first.
        // §7.1 says all jobs arrive together, so "the run queue" is the whole
        // workload at every decision point for the non-preemptive policies.
        if (policy != BaselinePolicy.Sjf && policy != BaselinePolicy.Stcf)
            return runQueue.Dequeue();

        var ordered = runQueue.OrderBy(j => j.Remaining).ThenBy(j => j.Name).ToList();
        runQueue.Clear();
        foreach (var j in ordered) runQueue.Enqueue(j);
        return runQueue.Dequeue();
    }

    private static ScheduleResult Summarize(
        BaselinePolicy policy, List<BaselineJob> jobs,
        List<(int, string)> trace, int makespan)
    {
        var turnaround = jobs.Select(j => (double)(j.CompletionAt - j.Arrival)).ToList();
        // A job that never ran has no first-run time. Every policy here runs
        // everything to completion, so this cannot happen; a defensive -1 would
        // hide a bug rather than report it.
        var response = jobs.Select(j => (double)(j.FirstRunAt - j.Arrival)).ToList();
        var wait = jobs.Select(j => (double)((j.CompletionAt - j.Arrival) - j.Length)).ToList();

        var order = jobs.Where(j => j.CompletionAt >= 0)
            .OrderBy(j => j.CompletionAt)
            .ThenBy(j => j.Name)
            .Select(j => j.Name);

        return new ScheduleResult
        {
            AvgTurnaround = turnaround.Average(),
            AvgResponse = response.Average(),
            AvgWait = wait.Average(),
            Turnaround = turnaround,
            Response = response,
            Arrivals = jobs.ToDictionary(j => j.Name, j => j.Arrival),
            CompletionOrder = string.Concat(order),
            Trace = trace,
            Makespan = makespan,
        };
    }
}