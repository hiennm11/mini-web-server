using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// One philosopher's trace row for the dining-philosophers demo.
///
/// OSEP §31.6 "The Dining Philosophers".
/// </summary>
public sealed class PhilosopherStats
{
    public int Id { get; init; }
    public int EatCount { get; set; }
    public int ThinkCount { get; set; }
    public bool Deadlocked { get; set; }   // didn't eat any in time

    public string Format() =>
        $"P{Id}: ate={EatCount} thought={ThinkCount} {(Deadlocked ? "(DEADLOCK)" : "")}";
}

/// <summary>
/// Dining philosophers (OSEP §31.6 Dijkstra 1971).
///
/// OSEP §31.6 "The Dining Philosophers":
///   "Assume there are five 'philosophers' sitting around a table.
///    Between each pair of philosophers is a single fork (and thus,
///    five total). The philosophers each have times where they think,
///    and don't need any forks, and times where they eat. In order to
///    eat, a philosopher needs two forks, both the one on their left
///    and the one on their right."
///
/// OSEP §31.6 "Broken Solution":
///   "If each philosopher happens to grab the fork on their left before
///    any philosopher can grab the fork on their right, each will be
///    stuck holding one fork and waiting for another, forever."
///
/// OSEP §31.6 "A Solution: Breaking The Dependency":
///   "Let's assume that philosopher 4 (the highest numbered one) gets
///    the forks in a *different* order than the others ... Because the
///    last philosopher tries to grab right before left, there is no
///    situation where each philosopher grabs one fork and is stuck
///    waiting for another; the cycle of waiting is broken."
///
/// Our implementation:
///   - Real OS threads (`Thread`), one per philosopher.
///   - Forks are `SemaphoreSlim(1, 1)` (counting semaphores from OSEP §31.1).
///   - Two modes: `Broken` (left-then-right for everyone) and `Fixed`
///     (last philosopher grabs right-then-left).
///   - Each philosopher thinks for `_thinkMs` ms, eats for `_eatMs` ms,
///     then repeats. After `maxSeconds` elapses, the simulation stops.
///   - Eat count + think count per philosopher.
///   - All threads wait on a `ManualResetEventSlim` start gate so they
///     enter the contention phase simultaneously (otherwise threads
///     start at slightly different times and the deadlock is rare).
/// </summary>
public enum DiningMode
{
    /// <summary>Everyone grabs left-then-right (deadlocks per OSEP §31.6).</summary>
    Broken,

    /// <summary>Last philosopher grabs right-then-left (Dijkstra's fix).</summary>
    Fixed,
}

public sealed class DiningPhilosophers
{
    private readonly int _count;
    private readonly DiningMode _mode;
    private readonly int _maxSeconds;
    private readonly int _thinkMs;
    private readonly int _eatMs;
    private readonly Random _rng;
    private readonly SemaphoreSlim[] _forks;
    private readonly int[] _heldBy;          // _heldBy[f] = philosopher holding fork f, or -1
    private readonly Thread[] _threads;
    private readonly PhilosopherStats[] _stats;
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _startGate = new(false);

    public DiningPhilosophers(
        int count,
        DiningMode mode,
        int maxSeconds,
        int thinkMs = 50,
        int eatMs = 25,
        int seed = 42)
    {
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(count));
        _count = count;
        _mode = mode;
        _maxSeconds = Math.Max(1, maxSeconds);
        _thinkMs = Math.Max(0, thinkMs);
        _eatMs = Math.Max(0, eatMs);
        _rng = new Random(seed);
        _forks = new SemaphoreSlim[count];
        _heldBy = new int[count];
        for (int i = 0; i < count; i++)
        {
            _forks[i] = new SemaphoreSlim(1, 1);
            _heldBy[i] = -1;
        }
        _threads = new Thread[count];
        _stats = new PhilosopherStats[count];
        for (int i = 0; i < count; i++) _stats[i] = new PhilosopherStats { Id = i };
    }

    /// <summary>Run the simulation. Blocks until maxSeconds elapsed or all done.</summary>
    public PhilosopherStats[] Run()
    {
        // Start all threads. Each pauses at _startGate.Wait() until released.
        for (int i = 0; i < _count; i++)
        {
            int id = i;
            _threads[i] = new Thread(() => PhilosopherLoop(id))
            {
                IsBackground = true,
                Name = $"Philosopher-{id}",
            };
            _threads[i].Start();
        }

        // Brief delay to ensure all threads have reached the gate.
        Thread.Sleep(100);
        // Open the gate: all philosophers begin competing simultaneously.
        _startGate.Set();

        // Wait for the configured duration.
        Thread.Sleep(_maxSeconds * 1000);
        _cts.Cancel();

        // Give threads a brief window to notice cancellation and clean up.
        for (int i = 0; i < _count; i++)
        {
            _threads[i].Join(TimeSpan.FromSeconds(2));
        }

        // Heuristic for deadlock: in Broken mode, if a philosopher didn't eat any
        // time but did think at least once, it likely got stuck holding one fork.
        int deadlockedCount = 0;
        foreach (var s in _stats)
        {
            if (_mode == DiningMode.Broken && s.EatCount == 0 && s.ThinkCount > 0)
            {
                s.Deadlocked = true;
                deadlockedCount++;
            }
        }
        return _stats;
    }

    /// <summary>
    /// Infinite loop: think, get forks, eat, put forks.
    /// OSEP §31.6 basic loop.
    /// </summary>
    private void PhilosopherLoop(int p)
    {
        // Wait for the start gate so all philosophers begin simultaneously.
        try { _startGate.Wait(_cts.Token); }
        catch (OperationCanceledException) { return; }

        while (!_cts.IsCancellationRequested)
        {
            Think(p);
            if (!GetForks(p)) return;  // cancelled or couldn't get both forks
            Eat(p);
            PutForks(p);
        }
    }

    private void Think(int p)
    {
        if (_thinkMs > 0) Thread.Sleep(_rng.Next(_thinkMs, _thinkMs * 2 + 1));
        _stats[p].ThinkCount++;
    }

    private void Eat(int p)
    {
        if (_eatMs > 0) Thread.Sleep(_rng.Next(_eatMs, _eatMs * 2 + 1));
        _stats[p].EatCount++;
    }

    private static int Left(int p, int count)  => p;
    private static int Right(int p, int count) => (p + 1) % count;

    /// <summary>
    /// Acquire two forks. Returns false if cancellation interrupted before
    /// both forks were obtained (the caller should stop and release what
    /// it has).
    /// </summary>
    private bool GetForks(int p)
    {
        int left = Left(p, _count);
        int right = Right(p, _count);

        // OSEP §31.6 "Broken Solution" — everyone left-then-right.
        if (_mode == DiningMode.Broken)
        {
            if (!TryAcquire(p, left)) return false;
            if (!TryAcquire(p, right))
            {
                ReleaseHeld(p);
                return false;
            }
        }
        else
        {
            // OSEP §31.6 "A Solution: Breaking The Dependency" — last philosopher
            // grabs right-then-left; everyone else grabs left-then-right.
            if (p == _count - 1)
            {
                if (!TryAcquire(p, right)) return false;
                if (!TryAcquire(p, left))
                {
                    ReleaseHeld(p);
                    return false;
                }
            }
            else
            {
                if (!TryAcquire(p, left)) return false;
                if (!TryAcquire(p, right))
                {
                    ReleaseHeld(p);
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Wait indefinitely on a fork; cancellation token breaks us out.
    /// Returns true if the fork was acquired, false if cancelled.
    /// </summary>
    private bool TryAcquire(int p, int fork)
    {
        try
        {
            _forks[fork].Wait(_cts.Token);
            _heldBy[fork] = p;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (SemaphoreFullException) { return false; /* shouldn't happen */ }
    }

    /// <summary>Release every fork currently held by this philosopher.</summary>
    private void ReleaseHeld(int p)
    {
        for (int f = 0; f < _count; f++)
        {
            if (_heldBy[f] == p)
            {
                try { _forks[f].Release(); } catch (SemaphoreFullException) { }
                _heldBy[f] = -1;
            }
        }
    }

    private void PutForks(int p)
    {
        ReleaseHeld(p);
    }

    public string FormatReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Dining Philosophers (M20 / OSEP §31.6) ===");
        sb.AppendLine($"mode: {_mode}  philosophers: {_count}  duration: {_maxSeconds}s");
        sb.AppendLine($"think: {_thinkMs}-{_thinkMs * 2}ms  eat: {_eatMs}-{_eatMs * 2}ms");
        sb.AppendLine();
        sb.AppendLine($"=== philosopher stats ===");
        int totalEat = 0, totalThink = 0;
        int deadlockedCount = 0;
        foreach (var s in _stats)
        {
            sb.AppendLine("  " + s.Format());
            totalEat += s.EatCount;
            totalThink += s.ThinkCount;
            if (s.Deadlocked) deadlockedCount++;
        }
        sb.AppendLine();
        sb.AppendLine($"=== summary ===");
        sb.AppendLine($"  total eats: {totalEat}");
        sb.AppendLine($"  total thinks: {totalThink}");
        sb.AppendLine($"  philosophers deadlocked: {deadlockedCount}/{_count}");
        if (_mode == DiningMode.Broken && deadlockedCount == _count)
        {
            sb.AppendLine();
            sb.AppendLine("  *** DEADLOCK DETECTED ***");
            sb.AppendLine("  All philosophers are stuck holding one fork and waiting for another.");
            sb.AppendLine("  This is the OSEP §31.6 'Broken Solution' — everyone grabs left then right.");
        }
        else if (_mode == DiningMode.Fixed && deadlockedCount == 0)
        {
            sb.AppendLine();
            sb.AppendLine("  *** NO DEADLOCK ***");
            sb.AppendLine("  All philosophers managed to eat at least once.");
            sb.AppendLine("  This is OSEP §31.6 'Breaking The Dependency' — the last philosopher");
            sb.AppendLine("  grabs right then left, breaking the cycle of waiting.");
        }
        return sb.ToString();
    }
}
