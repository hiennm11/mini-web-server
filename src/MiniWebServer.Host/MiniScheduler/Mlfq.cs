using System.Collections.Generic;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// Multi-Level Feedback Queue (MLFQ) scheduler. OSEP Ch. 8.
///
/// Rules implemented:
///   R1: If Priority(A) > Priority(B), A runs.
///   R2: If Priority(A) == Priority(B), round-robin.
///   R3: When a job enters the system, it goes to the top queue.
///   R4: Once a job uses up its time slice at a queue level, its
///       priority is reduced (demoted) to the next lower queue.
///       (For yielding jobs the slice is the "allotment" rather
///       than a single time slice; we approximate this by giving
///       yielding jobs the next queue's slice and demoting only
///       when their full slice is used.)
///   R5: After some time period S, move all jobs to the topmost
///       queue. This prevents starvation.
///
/// Configurable: number of queues, slice length per queue
/// (typically doubling per level: 10, 20, 40, 80 ticks), and
/// the boost period.
///
/// Slice 13.1: trace-only simulation, no real CPU. Each Tick()
/// advances simulated time by 1 and returns a TraceEvent.
/// </summary>
public sealed class Mlfq
{
    private readonly int _numQueues;
    private readonly int[] _sliceTicks;  // ticks allowed per queue level
    private readonly int _boostEvery;
    private readonly Queue<Job>[] _queues;
    private readonly List<TraceEvent> _trace = new();

    private Job? _current;
    private int _currentSliceLeft;
    private int _ticksSinceBoost;
    private int _tick;

    public IReadOnlyList<TraceEvent> Trace => _trace;

    public Mlfq(int numQueues = 4, int[]? sliceTicks = null, int boostEveryTicks = 50)
    {
        if (numQueues < 1) throw new ArgumentException("numQueues >= 1", nameof(numQueues));
        _numQueues = numQueues;
        _sliceTicks = sliceTicks ?? DefaultSlices(numQueues);
        if (_sliceTicks.Length != numQueues)
            throw new ArgumentException($"sliceTicks.Length must equal numQueues ({numQueues})", nameof(sliceTicks));
        _boostEvery = boostEveryTicks;
        _queues = new Queue<Job>[numQueues];
        for (int i = 0; i < numQueues; i++) _queues[i] = new Queue<Job>();
    }

    private static int[] DefaultSlices(int n)
    {
        // Doubling slices: 10, 20, 40, 80, ... (OSEP §8.2 default)
        var s = new int[n];
        for (int i = 0; i < n; i++) s[i] = 10 * (1 << i);
        return s;
    }

    public int NumQueues => _numQueues;
    public int[] SliceTicks => (int[])_sliceTicks.Clone();
    public int BoostEvery => _boostEvery;
    public int CurrentTick => _tick;

    /// <summary>Enqueue a new job at the top queue (R3).</summary>
    public void Enqueue(Job job)
    {
        job.CurrentQueue = 0;
        job.State = JobState.Ready;
        _queues[0].Enqueue(job);
    }

    /// <summary>Advance simulated time by one tick.</summary>
    public TraceEvent Tick()
    {
        _tick++;

        // R5: periodic priority boost
        if (_boostEvery > 0 && _ticksSinceBoost >= _boostEvery)
        {
            Boost();
            _ticksSinceBoost = 0;
        }

        // Dispatch a new job if the current one is done or empty
        if (_current == null || _current.State == JobState.Done)
        {
            Dispatch();
        }

        if (_current == null)
        {
            _ticksSinceBoost++;
            var idle = new TraceEvent(_tick, "idle", null, null, null, null, "no runnable job");
            _trace.Add(idle);
            return idle;
        }

        // Run current job for one tick
        _current.BurstRemaining--;
        _currentSliceLeft--;
        _ticksSinceBoost++;

        if (_current.BurstRemaining == 0)
        {
            // Job done
            _current.State = JobState.Done;
            var ev = new TraceEvent(_tick, "finish", _current.Id, _current.Name,
                _current.CurrentQueue, 0, "burst complete");
            _current = null;
            _currentSliceLeft = 0;
            _trace.Add(ev);
            return ev;
        }

        if (_currentSliceLeft == 0)
        {
            // Time slice expired — R4: demote or stay
            int fromQ = _current.CurrentQueue;
            int toQ = fromQ;
            if (!_current.YieldsEarly)
            {
                toQ = Math.Min(fromQ + 1, _numQueues - 1);
            }
            _current.CurrentQueue = toQ;
            _current.State = JobState.Ready;
            _queues[toQ].Enqueue(_current);
            var ev = new TraceEvent(_tick, "preempt", _current.Id, _current.Name,
                fromQ, _current.BurstRemaining,
                toQ > fromQ ? $"slice used → demote q{fromQ}→q{toQ}" : $"slice used → stay q{toQ}");
            _current = null;
            _currentSliceLeft = 0;
            _trace.Add(ev);
            return ev;
        }

        // Still running
        var runEv = new TraceEvent(_tick, "running", _current.Id, _current.Name,
            _current.CurrentQueue, _current.BurstRemaining,
            $"slice left {_currentSliceLeft}");
        _trace.Add(runEv);
        return runEv;
    }

    private void Dispatch()
    {
        for (int q = 0; q < _numQueues; q++)
        {
            if (_queues[q].Count > 0)
            {
                _current = _queues[q].Dequeue();
                _current.State = JobState.Running;
                _currentSliceLeft = _sliceTicks[q];
                _trace.Add(new TraceEvent(_tick, "dispatch", _current.Id, _current.Name,
                    q, _current.BurstRemaining, $"slice={_sliceTicks[q]}"));
                return;
            }
        }
        _current = null;
        _currentSliceLeft = 0;
    }

    private void Boost()
    {
        int boosted = 0;
        // Move all ready jobs in lower queues to the top queue
        for (int q = 1; q < _numQueues; q++)
        {
            while (_queues[q].Count > 0)
            {
                var j = _queues[q].Dequeue();
                j.CurrentQueue = 0;
                _queues[0].Enqueue(j);
                boosted++;
            }
        }
        // If a currently-running job is below the top, also boost it
        if (_current != null && _current.CurrentQueue > 0)
        {
            _current.CurrentQueue = 0;
            _currentSliceLeft = _sliceTicks[0];
            boosted++;
        }
        _trace.Add(new TraceEvent(_tick, "boost", null, null, null, null,
            $"moved {boosted} job(s) to q0"));
    }

    /// <summary>Summary stats for the run.</summary>
    public MlfqStats Stats()
    {
        int finished = 0;
        int stillReady = 0;
        int stillRunning = 0;
        foreach (var q in _queues) stillReady += q.Count;
        foreach (var ev in _trace)
        {
            if (ev.Action == "finish") finished++;
        }
        if (_current != null && _current.State == JobState.Running) stillRunning = 1;
        return new MlfqStats(_tick, finished, stillReady, stillRunning, _trace.Count);
    }
}

public sealed record MlfqStats(int TotalTicks, int Finished, int StillReady, int StillRunning, int TraceEvents);
