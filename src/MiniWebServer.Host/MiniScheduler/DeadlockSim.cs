using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// A named mutex resource for the deadlock lab.
///
/// OSEP §32.3 models locks; the repo's lock is a plain <see cref="object"/>
/// guarded by <see cref="Monitor"/> (M5). This wrapper adds the one thing the
/// chapter's techniques need and a bare object does not carry: a stable
/// <see cref="Id"/>, so <see cref="DeadlockSim.AcquireInOrder"/> has something
/// to sort by. OSEP §32.3 TIP "ENFORCE LOCK ORDERING BY LOCK ADDRESS" uses the
/// lock's address for exactly this; an explicit id is the same idea with the
/// ordering made inspectable instead of accidental.
/// </summary>
public sealed class ResourceLock
{
    private readonly object _monitor = new();

    public ResourceLock(int id, string name)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        Id = id;
        Name = name;
    }

    /// <summary>Total order key. Lower is acquired first.</summary>
    public int Id { get; }

    public string Name { get; }

    /// <summary>The monitor to lock. <c>lock (Monitor)</c> guards it.</summary>
    public object Monitor => _monitor;

    public override string ToString() => $"L{Id}";
}

/// <summary>Outcome of one deadlock-lab run.</summary>
public sealed record DeadlockRunResult(
    string Scenario,
    bool Deadlocked,
    string Headline,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Notes);

/// <summary>
/// OSEP Ch. 32 §32.3 "Deadlock Bugs" — prevention and avoidance.
///
/// "Four conditions need to hold for a deadlock to occur [C+71]: mutual
/// exclusion ... hold-and-wait ... no preemption ... circular wait."
/// (§32.3) "If any of these four conditions are not met, deadlock cannot
/// occur. Thus, we first explore techniques to prevent deadlock; each of these
/// strategies seeks to prevent one of the above conditions from arising."
/// (§32.3)
///
/// Each prevention technique below breaks exactly one of those conditions.
/// The lab runs all of them over the same lock-acquisition workload so the
/// difference is visible in one response.
/// </summary>
public static class DeadlockSim
{
    /// <summary>
    /// How long a scenario may run before we call it deadlocked. A run that
    /// finishes well inside this is never misreported.
    /// </summary>
    private const int RunTimeoutMs = 2000;

    /// <summary>Runs one scenario by name.</summary>
    public static DeadlockRunResult Run(string scenario)
    {
        return scenario switch
        {
            "naive" => RunNaive(),
            "ordering" => RunOrdering(),
            "batch" => RunBatch(),
            "preempt" => RunPreempt(),
            "banker" => RunBanker(),
            _ => throw new ArgumentException(
                $"unknown deadlock scenario '{scenario}' " +
                "(use naive, ordering, batch, preempt, banker)", nameof(scenario)),
        };
    }

    public static string Format(DeadlockRunResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.Headline);
        sb.AppendLine();
        foreach (var line in result.Lines)
        {
            sb.AppendLine(line);
        }
        if (result.Notes.Count > 0)
        {
            sb.AppendLine();
            foreach (var note in result.Notes)
            {
                sb.AppendLine(note);
            }
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Runs <paramref name="body"/> with a deadline and reports whether it
    /// finished. Deadlock is expressed as "did not finish", so a stuck run is
    /// data rather than a hung request.
    /// </summary>
    private static bool RunsToCompletion(Action body, out int completedCount, Action? cleanup)
    {
        int completed = 0;
        var workers = new List<Thread>();
        var startGate = new ManualResetEventSlim(false);
        var failures = new List<Exception>();

        void Worker(int id)
        {
            // All threads enter the contention phase together; otherwise the
            // interleaving that produces the deadlock rarely happens and the
            // demo teaches nothing (same lesson as M20's start gate).
            startGate.Wait();
            try
            {
                body();
                Interlocked.Increment(ref completed);
            }
            catch (Exception ex)
            {
                // Swallowed so one failure does not tear down the run, but kept:
                // a worker that threw is "did not complete", which must not be
                // silently indistinguishable from a worker stuck in a deadlock.
                lock (failures) { failures.Add(ex); }
            }
        }

        int threadCount = ThreadCount;
        for (int i = 0; i < threadCount; i++)
        {
            int id = i;
            workers.Add(new Thread(() => Worker(id)) { IsBackground = true, Name = $"dl-{id}" });
        }
        foreach (var t in workers) t.Start();
        startGate.Set();

        // One deadline for the whole set, not per thread: joining each in turn
        // with its own timeout would let a four-thread deadlock take 4x the
        // timeout to even notice, and again after cleanup.
        //
        // The verdict and the completion count are frozen from the same read.
        // Reading them at separate points lets a worker finish in between and
        // report "all threads completed" next to a DEADLOCK verdict.
        var deadline = Environment.TickCount64 + RunTimeoutMs;
        bool anyTimedOut = false;
        foreach (var t in workers)
        {
            int remaining = (int)Math.Max(0, deadline - Environment.TickCount64);
            if (!t.Join(remaining)) anyTimedOut = true;
        }

        bool finished;
        if (!anyTimedOut)
        {
            // Everyone returned on their own. Now, and only now, is the count
            // final: a worker can finish between the join loop and this read,
            // so reading it before the joins produced an occasional
            // "DEADLOCK, completed=1/4" for a run that was simply still settling.
            completedCount = Volatile.Read(ref completed);
            finished = completedCount == workers.Count;
        }
        else
        {
            // Snapshot before teardown: cleanup releases the parked threads, and
            // reporting their post-teardown count alongside a DEADLOCK verdict
            // would claim the deadlock finished.
            completedCount = Volatile.Read(ref completed);
            finished = false;

            cleanup?.Invoke();
            foreach (var t in workers) t.Join(RunTimeoutMs);
        }

        // A worker that threw is a bug in the scenario body, not a deadlock.
        // Surfacing it stops a real failure from being reported as "no
        // deadlock, just fewer completions".
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"deadlock scenario worker threw: {failures[0]}", failures[0]);
        }
        return finished;
    }

    /// <summary>Thread count for the lock scenarios; fixed so runs compare.</summary>
    private static int ThreadCount => 4;

    /// <summary>
    /// Run one preempt attempt, staged so the contended branch is guaranteed to
    /// execute rather than left to chance.
    /// </summary>
    /// <remarks>
    /// Measured: with four threads released together, the contended branch runs
    /// on the first pass (cold thread pool, all four scheduled at once) and
    /// then stops firing — later attempts see the threads already warm and
    /// effectively serialised, so <c>lock(first)</c> queues them and the second
    /// lock is always free by the time the next thread arrives. Relying on that
    /// would make the demo's teaching moment a coin flip, so the first two
    /// threads are parked behind a barrier that is released only once both hold
    /// their first lock. That is the collision the technique is about.
    /// </remarks>
    private static bool StagedPreemptAttempt(
        object firstLock, object secondLock, CountdownEvent bothHoldFirst,
        Action onSuccess, Action onCollision, Func<bool> shouldStop)
    {
        lock (firstLock)
        {
            bothHoldFirst.Signal();

            // Bounded rendezvous, not an unbounded one: if the other collider is
            // never scheduled, an unconditional wait would park this thread for
            // the life of the process and the teardown join would ignore it.
            while (!bothHoldFirst.IsSet)
            {
                if (shouldStop())
                {
                    return false;
                }
                Thread.Sleep(1);
            }

            if (shouldStop())
            {
                return false;
            }

            if (Monitor.TryEnter(secondLock))
            {
                try { onSuccess(); }
                finally { Monitor.Exit(secondLock); }
                return true;
            }

            onCollision();
            return false;
        }
    }

    /// <summary>
    /// Whether a worker is one of the two staged to collide. Threads 0 and 1
    /// take opposite lock orders, which is what makes the collision certain.
    /// </summary>
    private static bool IsCollider(string? threadName)
    {
        char last = threadName is { Length: > 0 } ? threadName[^1] : '9';
        return last is '0' or '1';
    }

    /// <summary>The two locks every scenario uses (OSEP §32.3 figure 32.6).</summary>
    private static (ResourceLock L1, ResourceLock L2) MakeLocks()
        => (new ResourceLock(1, "L1"), new ResourceLock(2, "L2"));

    /// <summary>
    /// Which acquisition order a worker was assigned. Even threads take
    /// (L1, L2), odd threads take (L2, L1) — so every scenario faces callers
    /// that disagree about order. Only a total-ordering rule can reconcile them.
    /// </summary>
    private static bool TakeL1First(string? threadName)
    {
        char last = threadName is { Length: > 0 } ? threadName[^1] : '0';
        return last is '0' or '2';
    }

    // ------------------------------------------------------- scenario: naive

    /// <summary>
    /// OSEP §32.3 figure 32.6 "Simple Deadlock (deadlock.c)": T1 takes L1 then
    /// L2, T2 takes L2 then L1.
    /// </summary>
    /// <remarks>
    /// "Note that if this code runs, deadlock does not necessarily occur;
    /// rather, it may occur" (§32.3) — so the interleaving is staged rather than
    /// hoped for: every thread takes its first lock, meets at a barrier, and
    /// only then reaches for its second. That is precisely the situation the
    /// figure describes ("Thread 1 grabs lock L1 and then a context switch
    /// occurs to Thread 2").
    /// </remarks>
private static DeadlockRunResult RunNaive()
    {
        var (l1, l2) = MakeLocks();
        var gate = new object();
        var freed = new ConditionVariable();
        bool released = false;
        var giveUpAfter = TimeSpan.FromMilliseconds(600);

        // Which threads join the collision is decided by identity, not by a
        // countdown: a thread that skipped the rendezvous because the counter
        // already reached zero would race ahead, possibly grabbing its second
        // lock before the designated partner, and the deadlock would silently
        // not happen. Naming the participants makes that impossible.
        bool IsDesignated(string? name)
        {
            char last = name is { Length: > 0 } ? name[^1] : '9';
            return last is '0' or '1';
        }

        // Both designated threads park here once each holds its first lock.
        int designatedHeld = 0;
        bool bothHolding = false;

        bool Released()
        {
            lock (gate) return released;
        }

        void AcquireOrder(bool takeL1First)
        {
            var first = takeL1First ? l1 : l2;
            var second = takeL1First ? l2 : l1;

            // Nonparticipants wait *before* touching the locks. Waiting while
            // holding one would park a thread on L1 and block the designated
            // holder from ever reaching its rendezvous, so the run would time
            // out without the lock cycle the scenario is meant to show.
            if (!IsDesignated(Thread.CurrentThread.Name))
            {
                while (!Released())
                {
                    lock (gate)
                    {
                        if (released)
                        {
                            return;
                        }
                        freed.Wait(gate, giveUpAfter);
                    }
                }
                return;
            }

            lock (first.Monitor)
            {
                // Announce that a first lock is held, then wait for the partner.
                // The check and the wait both happen under `gate`, so a partner
                // that arrives between them cannot be missed: reading the flag
                // outside the lock and re-reading it inside would let a ready
                // rendezvous look like a cancellation and return instead.
                lock (gate)
                {
                    designatedHeld++;
                    bothHolding = designatedHeld >= 2;
                    while (!bothHolding && !released)
                    {
                        freed.Wait(gate, giveUpAfter);
                        bothHolding = designatedHeld >= 2;
                    }
                    if (released)
                    {
                        return;
                    }
                }

                while (true)
                {
                    // Try the second lock without blocking. A real deadlock.c
                    // would block here forever; polling with a deadline is what
                    // makes the condition observable *and* lets the thread walk
                    // away afterwards, instead of stranding four threads per
                    // request.
                    if (Monitor.TryEnter(second.Monitor))
                    {
                        try { /* acquired both: no cycle */ }
                        finally { Monitor.Exit(second.Monitor); }
                        return;
                    }

                    lock (gate)
                    {
                        if (released)
                        {
                            return;
                        }
                        freed.Wait(gate, giveUpAfter);
                    }
                }
            }
        }

        bool finished = RunsToCompletion(
            () => AcquireOrder(TakeL1First(Thread.CurrentThread.Name)),
            out int completed,
            cleanup: () =>
            {
                lock (gate)
                {
                    released = true;
                    freed.Broadcast();
                }
            });

        var lines = new List<string>
        {
            $"threads={ThreadCount} locks=2 (L1, L2)",
            "acquisition order: threads 0,2 take L1->L2; threads 1,3 take L2->L1",
            $"completed={completed}/{ThreadCount}",
            $"result: {(finished ? "all threads completed" : "DEADLOCK")}",
            "",
            "dependency graph (figure 32.7):",
            "  T0 holds L1, wants L2   L2 held by T1",
            "  T1 holds L2, wants L1   L1 held by T0",
            "  -> a cycle in the wait-for graph; nobody can proceed",
        };

        if (!finished && completed > 0)
        {
            // A partial count here means a thread returned during the timeout
            // window, before the others were released. It is not progress on
            // the cycle - the two threads in it never complete.
            lines.Add("");
            lines.Add($"note: {completed} thread(s) left before the deadline; the cycle itself never resolves.");
        }

        var notes = new List<string>
        {
            "OSEP §32.3: all four Coffman conditions hold here - mutual exclusion (locks are",
            "exclusive), hold-and-wait (each holds one and waits for the other), no preemption",
            "(neither lock can be taken away), and circular wait (T0->T1->T0).",
            "",
            "Note the staging: the textbook says deadlock 'does not necessarily occur' without",
            "staging, because T1 must grab L2 in the window after T0 grabs L1. The barrier is",
            "that window.",
        };

        return new DeadlockRunResult("naive", !finished,
            "=== Deadlock: no technique (M30 / OSEP §32.3, broken) ===", lines, notes);
    }

    // ----------------------------------------------------- scenario: ordering

    /// <summary>
    /// OSEP §32.3 "Prevention → Circular Wait": "Probably the most practical
    /// prevention technique ... is to write your locking code such that you
    /// never induce a circular wait. The most straightforward way to do that
    /// is to provide a total ordering on lock acquisition."
    ///
    /// §32.3 TIP "ENFORCE LOCK ORDERING BY LOCK ADDRESS" is the concrete form:
    /// order by the lock's address, so the caller cannot express the order
    /// wrong. Here it is ordered by <see cref="ResourceLock.Id"/>.
    /// </summary>
    private static DeadlockRunResult RunOrdering()
    {
        var (l1, l2) = MakeLocks();
        int reorderingsNormalised = 0;

        void AcquireInOrder(ResourceLock a, ResourceLock b)
        {
            var first = a.Id <= b.Id ? a : b;
            var second = a.Id <= b.Id ? b : a;
            lock (first.Monitor)
            {
                lock (second.Monitor)
                {
                    Interlocked.Increment(ref reorderingsNormalised);
                }
            }
        }

        // Half the threads ask for (L1, L2), half for (L2, L1): the ordering
        // has to come from the acquisition rule, not from the callers.
        bool finished = RunsToCompletion(
            () => AcquireInOrder(TakeL1First(Thread.CurrentThread.Name) ? l1 : l2,
                                TakeL1First(Thread.CurrentThread.Name) ? l2 : l1),
            out int completed,
            cleanup: null);

        var lines = new List<string>
        {
            $"threads={ThreadCount} locks=2 (L1 id=1, L2 id=2)",
            "callers still pass (L1,L2) or (L2,L1) in opposite directions",
            $"completed={completed}/{ThreadCount}",
            $"result: {(finished ? "all threads completed - no circular wait" : "DEADLOCK (unexpected)")}",
            "",
            "every acquisition ran L1 before L2, because AcquireInOrder sorts by id:",
            "  T0 (L1,L2) -> L1,L2        T1 (L2,L1) -> L1,L2",
            "  T2 (L1,L2) -> L1,L2        T3 (L2,L1) -> L1,L2",
            "a single total order leaves no edge that could close a cycle",
        };

        var notes = new List<string>
        {
            "OSEP §32.3: 'a strict ordering ensures that no cyclical wait arises; hence, no",
            "deadlock.' This breaks exactly one Coffman condition - circular wait - and leaves the",
            "other three intact, which is why it composes with everything else.",
            "",
            "The textbook's tip is to sort by lock address; this sorts by an explicit id so the",
            "order is inspectable. The two arguments are otherwise interchangeable - what matters",
            "is that the order is total and every caller goes through it.",
        };

        return new DeadlockRunResult("ordering", !finished,
            "=== Deadlock: prevention by lock ordering (M30 / OSEP §32.3) ===", lines, notes);
    }

    // ------------------------------------------------------- scenario: batch

    /// <summary>
    /// OSEP §32.3 "Prevention → Hold-and-wait": "The hold-and-wait requirement
    /// for deadlock can be avoided by acquiring all locks at once, atomically."
    /// The chapter's code takes a global <c>prevention</c> lock around the
    /// whole batch; that is reproduced here as <c>Prevention</c>.
    /// </summary>
    /// <remarks>
    /// §32.3's own verdict: "This technique also is likely to decrease
    /// concurrency as all locks must be acquired early on (at once) instead of
    /// when they are truly needed." The route reports serialised acquisitions
    /// so that cost is visible rather than asserted.
    /// </remarks>
    private static DeadlockRunResult RunBatch()
    {
        var (l1, l2) = MakeLocks();
        var prevention = new object();
        int batches = 0, activeInsidePrevention = 0, maxOverlap = 0;

        void AcquireAll(params ResourceLock[] locks)
        {
            lock (prevention)
            {
                // Count entry to the prevention region *before* taking any
                // resource lock. Measured after acquisition the number is 1
                // either way — the resource locks themselves serialise the
                // batch — so it could not tell a working prevention lock from a
                // deleted one.
                int now = Interlocked.Increment(ref activeInsidePrevention);
                int observed = Volatile.Read(ref maxOverlap);
                while (now > observed &&
                       Interlocked.CompareExchange(ref maxOverlap, now, observed) != observed)
                {
                    observed = Volatile.Read(ref maxOverlap);
                }

                // Nobody else can be mid-acquisition, so this batch cannot
                // interleave with another thread's partial set.
                var held = new List<object>();
                try
                {
                    foreach (var l in locks)
                    {
                        Monitor.Enter(l.Monitor);
                        held.Add(l.Monitor);
                    }
                    Interlocked.Increment(ref batches);
                    Thread.Yield();
                }
                finally
                {
                    for (int i = held.Count - 1; i >= 0; i--)
                    {
                        Monitor.Exit(held[i]);
                    }
                    Interlocked.Decrement(ref activeInsidePrevention);
                }
            }
        }

        bool finished = RunsToCompletion(
            () =>
            {
                bool takeL1First = TakeL1First(Thread.CurrentThread.Name);
                AcquireAll(takeL1First ? l1 : l2, takeL1First ? l2 : l1);
            },
            out int completed,
            cleanup: null);

        var lines = new List<string>
        {
            $"threads={ThreadCount} locks=2 (L1, L2)",
            $"completed={completed}/{ThreadCount}",
            $"result: {(finished ? "all threads completed - no hold-and-wait" : "DEADLOCK (unexpected)")}",
            "",
            $"atomic batches completed: {batches}",
            $"max threads inside the prevention lock at once: {maxOverlap} (of {ThreadCount})",
            "the prevention lock admits one thread at a time, so no thread was ever",
            "part-way through a set while waiting for the rest: hold-and-wait has",
            "nowhere to happen, at the cost of serialising every batch",
        };

        var notes = new List<string>
        {
            "OSEP §32.3: 'By first grabbing the lock prevention, this code guarantees that no",
            "untimely thread switch can occur in the midst of lock acquisition'. This breaks",
            "hold-and-wait and leaves circular wait intact - two threads can still want the",
            "same pair, they just cannot hold half of it.",
            "",
            "The chapter also names the cost: acquiring early 'is likely to decrease concurrency'.",
            "Here every batch is serialised through the prevention lock, so the four threads ran",
            "strictly one after another.",
        };

        return new DeadlockRunResult("batch", !finished,
            "=== Deadlock: prevention by atomic batch acquire (M30 / OSEP §32.3) ===", lines, notes);
    }

    // ----------------------------------------------------- scenario: preempt

    /// <summary>
    /// OSEP §32.3 "Prevention → No Preemption": take L1, try L2, and if the
    /// try fails, release L1 and start over.
    /// </summary>
    /// <remarks>
    /// The chapter is careful that this "doesn't really add preemption ... but
    /// rather uses the trylock approach to allow a developer to back out of lock
    /// ownership (i.e., preempt their own ownership)". It then names the new
    /// hazard: livelock, cured by "a random delay before looping back". Both
    /// are reproduced: a scenario without backoff and one with.
    /// </remarks>
    private static DeadlockRunResult RunPreempt()
    {
        var (l1, l2) = MakeLocks();
        int retries = 0, acquisitions = 0, contendedTries = 0;
        // Threads 0 and 1 take opposite locks, so they collide by construction.
        var collidersReady = new CountdownEvent(2);
        // Shared cancellation. The retry loop is unbounded by design (that is
        // the technique), so the run needs a way out that does not depend on the
        // scheduler eventually letting a thread win.
        var stop = new CancellationTokenSource();
        var stopGate = new object();
        bool stopRequested = false;

        bool Stopped()
        {
            lock (stopGate) return stopRequested;
        }

        // Half the threads acquire (L1, L2) and half (L2, L1). This is not
        // decoration: with a uniform order, `lock(first)` simply queues the
        // threads and the second lock is always free by the time the next one
        // reaches it, so the trylock branch never runs. The trylock + backoff
        // technique only does anything when threads want the same locks in
        // different orders - which is exactly the situation it exists for.

        bool finished = RunsToCompletion(
            () =>
            {
                bool takeL1First = TakeL1First(Thread.CurrentThread.Name);
                var first = takeL1First ? l1 : l2;
                var second = takeL1First ? l2 : l1;

                if (IsCollider(Thread.CurrentThread.Name))
                {
                    // The two threads that want opposite locks are held at a
                    // barrier once each holds its first lock. Both then attempt
                    // the second: one wins, the other's trylock fails, so the
                    // backoff path is exercised every run rather than whenever
                    // the scheduler happens to line up.
                    bool got = StagedPreemptAttempt(
                        first.Monitor,
                        second.Monitor,
                        collidersReady,
                        onSuccess: () => Interlocked.Increment(ref acquisitions),
                        onCollision: () =>
                        {
                            Interlocked.Increment(ref contendedTries);
                            Interlocked.Increment(ref retries);
                        },
                        shouldStop: Stopped);

                    if (got)
                    {
                        return;
                    }

                    // Bailed out because the run was cancelled, not because we
                    // lost the race - retrying here would loop forever.
                    if (Stopped())
                    {
                        return;
                    }

                    // Lost the race: back off, then retry the pair. Skipping the
                    // delay here is what turns this into livelock (§32.3).
                    Thread.Sleep(Random.Shared.Next(1, 6));
                    while (!Stopped())
                    {
                        lock (first.Monitor)
                        {
                            if (Monitor.TryEnter(second.Monitor))
                            {
                                try { Interlocked.Increment(ref acquisitions); }
                                finally { Monitor.Exit(second.Monitor); }
                                return;
                            }
                            Interlocked.Increment(ref contendedTries);
                        }
                        Interlocked.Increment(ref retries);
                        Thread.Sleep(Random.Shared.Next(1, 6));
                    }
                }

                // Everyone else just takes both, retrying with backoff.
                while (!Stopped())
                {
                    lock (first.Monitor)
                    {
                        if (Monitor.TryEnter(second.Monitor))
                        {
                            try { Interlocked.Increment(ref acquisitions); }
                            finally { Monitor.Exit(second.Monitor); }
                            return;
                        }
                        Interlocked.Increment(ref contendedTries);
                    }
                    Interlocked.Increment(ref retries);
                    Thread.Sleep(Random.Shared.Next(1, 6));
                }
            },
            out int completed,
            cleanup: () =>
            {
                lock (stopGate) { stopRequested = true; }
                stop.Cancel();
            });

        var lines = new List<string>
        {
            $"threads={ThreadCount} locks=2 (L1, L2); threads 0,2 take L1->L2, threads 1,3 take L2->L1",
            $"completed={completed}/{ThreadCount}",
            $"result: {(finished ? "all threads completed - no deadlock" : "DEADLOCK (unexpected)")}",
            "",
            $"successful acquisitions: {acquisitions}",
            $"failed trylock attempts (backed out, retried): {retries}",
            "",
            "no thread is ever permanently parked: a failed trylock releases the",
            "first lock immediately, so no cycle can be formed. Livelock is the",
            "residual risk, and the random backoff is what prevents it.",
        };

        var notes = new List<string>
        {
            "OSEP §32.3: this 'doesn't really add preemption' - it lets a thread preempt its own",
            "ownership by backing out. What it removes is the wait-while-holding that makes",
            "circular wait possible.",
            "",
            "The chapter warns of livelock: two threads 'repeatedly attempting this sequence and",
            "repeatedly failing to acquire both locks ... progress is not being made'. The random",
            "delay before each retry is the cure the chapter prescribes.",
        };

        return new DeadlockRunResult("preempt", !finished,
            "=== Deadlock: prevention by trylock + backoff (M30 / OSEP §32.3) ===", lines, notes);
    }

    // ------------------------------------------------------ scenario: banker

    /// <summary>
    /// OSEP §32.3 "Deadlock Avoidance via Scheduling" names Dijkstra's Banker's
    /// Algorithm [D64] as the famous example and immediately qualifies it:
    /// "they are only useful in very limited environments, for example, in an
    /// embedded system where one has full knowledge of the entire set of tasks
    /// that must be run and the locks that they need."
    ///
    /// <para><b>Citation boundary.</b> The prevention techniques above are
    /// §32.3 verbatim. The Max/Allocation/Need/Available tables and the
    /// safety-algorithm steps below are <em>not</em> in OSTEP - the chapter
    /// gives avoidance only as a scheduling idea and names Banker's without
    /// restating it. They are Dijkstra 1964 [D64] as presented in the standard
    /// operating-systems literature, and are implemented here for contrast with
    /// the prevention strategies. See ADR 0020.</para>
    /// </summary>
    private static DeadlockRunResult RunBanker()
    {
        // The classic worked example (Silberschatz figure 7.5/7.6 shape).
        // Max:
        int[,] max =
        {
            { 7, 5, 3 },   // T0
            { 3, 2, 2 },   // T1
            { 9, 0, 2 },   // T2
            { 2, 2, 2 },   // T3
            { 4, 3, 3 },   // T4
        };
        // Allocation so far:
        int[,] allocation =
        {
            { 0, 1, 0 },   // T0
            { 2, 0, 0 },   // T1
            { 3, 0, 2 },   // T2
            { 2, 1, 1 },   // T3
            { 0, 0, 2 },   // T4
        };
        int[] available = { 3, 3, 2 };

        var banker = new Banker(available, allocation, max);
        var trace = new List<string>();

        // A request that fits what is free and stays within T4's declared need,
        // but leaves no safe sequence: granting it takes Available to [0,0,2]
        // while T0 still needs [7,4,3] and T2 still needs [6,0,0]. No thread can
        // finish, so the grant would deadlock. This is the case avoidance exists
        // to catch — a request that a naive allocator would happily approve.
        int[] unsafeRequest = { 3, 3, 0 };
        bool unsafeAdmitted = banker.TryRequest(4, unsafeRequest, out string? unsafeReason);
        trace.Add($"T4 requests [{string.Join(",", unsafeRequest)}] -> {(unsafeAdmitted ? "GRANTED" : $"DENIED ({unsafeReason})")}");
        trace.Add($"  (fits Available [3,3,2] and T4's Need [4,3,1], but leaves no safe sequence)");

        // A request that IS safe: T1 asks for its entire remaining need. T1's
        // allocation then equals its Max, so T1 finishes first and returns
        // everything, making the other four reachable.
        int[] safeRequest = { 1, 2, 2 };
        bool safeAdmitted = banker.TryRequest(1, safeRequest, out string? safeReason);
        trace.Add($"T1 requests [{string.Join(",", safeRequest)}] -> {(safeAdmitted ? "GRANTED (state stays safe)" : $"DENIED ({safeReason})")}");

        // Banker must refuse the unsafe request and accept the safe one: that
        // asymmetry is the entire point of avoidance.
        bool correct = safeAdmitted && !unsafeAdmitted;

        var lines = new List<string>
        {
            "threads=5, resource classes=3, initial Available = [3,3,2]",
            "",
            "Allocation:",
            "  T0 [0,1,0]   T1 [2,0,0]   T2 [3,0,2]   T3 [2,1,1]   T4 [0,0,2]",
            "Max:",
            "  T0 [7,5,3]   T1 [3,2,2]   T2 [9,0,2]   T3 [2,2,2]   T4 [4,3,3]",
            "Need = Max - Allocation:",
            "  T0 [7,4,3]   T1 [1,2,2]   T2 [6,0,0]   T3 [0,1,1]   T4 [4,3,1]",
            "",
            string.Join("\n", trace.Select(t => "  " + t)),
            "",
            $"safe sequence after the granted request: [{string.Join(",", banker.FindSafeSequence() ?? Array.Empty<int>())}]",
            $"result: {(correct ? "banker admitted the safe request and refused the unsafe one" : "UNEXPECTED")}",
        };

        var notes = new List<string>
        {
            "Avoidance admits a request only if the resulting state still admits a safe sequence -",
            "an order in which every thread can finish. That is stronger than prevention: nothing",
            "is forbidden, requests are just granted or refused at runtime.",
            "",
            "OSTEP's own verdict on this approach: 'only useful in very limited environments'",
            "(§32.3), because it needs each thread's maximum claim in advance. Real systems do",
            "not know that, which is why the prevention techniques above are what ships.",
            "",
            "The tables and the safety algorithm are Dijkstra 1964 [D64] as given in the standard",
            "OS literature, not OSTEP: §32.3 names Banker's algorithm but does not restate it.",
        };

        return new DeadlockRunResult("banker", !correct,
            "=== Deadlock avoidance: Banker's algorithm (M30 / Dijkstra 1964, OSEP §32.3) ===",
            lines, notes);
    }
}

/// <summary>
/// Dijkstra's Banker's Algorithm [D64] — deadlock <em>avoidance</em>.
///
/// Admission rule: grant a request only if, after granting it, the state still
/// has a **safe sequence**: some order of the threads in which each can obtain
/// its full remaining need from what will be free once the earlier ones finish.
///
/// This is not in OSTEP. OSEP §32.3 names the algorithm as an example of
/// "deadlock avoidance via scheduling" and immediately says it is "only useful
/// in very limited environments"; the tables and steps below come from the
/// standard treatment of [D64]. See ADR 0020.
/// </summary>
public sealed class Banker
{
    private readonly int _threadCount;
    private readonly int _resourceCount;
    private readonly int[,] _max;
    private readonly int[,] _allocation;
    private int[] _available;

    /// <param name="available">Currently free units, per resource class.</param>
    /// <param name="allocation">Units each thread currently holds.</param>
    /// <param name="max">Each thread's declared maximum claim.</param>
    public Banker(int[] available, int[,] allocation, int[,] max)
    {
        _available = (int[])available.Clone();
        _allocation = (int[,])allocation.Clone();
        _max = (int[,])max.Clone();
        _threadCount = allocation.GetLength(0);
        _resourceCount = allocation.GetLength(1);

        for (int t = 0; t < _threadCount; t++)
        {
            for (int r = 0; r < _resourceCount; r++)
            {
                if (_allocation[t, r] > _max[t, r])
                {
                    throw new ArgumentException(
                        $"thread {t} holds more of resource {r} than its declared maximum");
                }
            }
        }
    }

    /// <summary>Units currently free, per resource class.</summary>
    public int[] Available => (int[])_available.Clone();

    /// <summary>Remaining need per thread: Max - Allocation.</summary>
    public int[] NeedOf(int thread)
    {
        var need = new int[_resourceCount];
        for (int r = 0; r < _resourceCount; r++)
        {
            need[r] = _max[thread, r] - _allocation[thread, r];
        }
        return need;
    }

    /// <summary>
    /// Attempts to grant <paramref name="request"/> to
    /// <paramref name="thread"/>. On refusal nothing changes.
    /// </summary>
    /// <returns>
    /// <c>false</c> with a reason when the request exceeds what is free, or
    /// when granting it would leave no safe sequence.
    /// </returns>
    public bool TryRequest(int thread, int[] request, out string? reason)
    {
        if ((uint)thread >= (uint)_threadCount)
        {
            throw new ArgumentOutOfRangeException(nameof(thread));
        }
        if (request.Length != _resourceCount)
        {
            throw new ArgumentException(
                $"request has {request.Length} entries, expected {_resourceCount}", nameof(request));
        }

        for (int r = 0; r < _resourceCount; r++)
        {
            if (request[r] < 0)
            {
                reason = $"negative request for resource {r}";
                return false;
            }
            if (request[r] > _available[r])
            {
                reason = $"Available[{r}]={_available[r]} < request[{r}]={request[r]} (would block on grant)";
                return false;
            }
            if (request[r] > _max[thread, r] - _allocation[thread, r])
            {
                reason = $"request[{r}]={request[r]} exceeds declared Need[{r}]={_max[thread, r] - _allocation[thread, r]}";
                return false;
            }
        }

        // Tentatively grant, then ask whether the result is still safe.
        // Clone before mutating: keeping a reference to _available would alias
        // it, so the undo below would restore the already-modified array and a
        // refused request would silently corrupt the state.
        var savedAvailable = (int[])_available.Clone();
        var savedRow = new int[_resourceCount];
        for (int r = 0; r < _resourceCount; r++)
        {
            savedRow[r] = _allocation[thread, r];
            _available[r] -= request[r];
            _allocation[thread, r] += request[r];
        }

        var sequence = TryFindSafeSequence();
        if (sequence is null)
        {
            // Undo: a refused request must leave the state untouched.
            _available = savedAvailable;
            for (int r = 0; r < _resourceCount; r++)
            {
                _allocation[thread, r] = savedRow[r];
            }
            reason = "no safe sequence after the grant (would deadlock)";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// The current safe sequence, or <c>null</c> if the state is unsafe.
    /// </summary>
    public int[]? FindSafeSequence() => TryFindSafeSequence();

    private int[]? TryFindSafeSequence()
    {
        var work = (int[])_available.Clone();
        var finished = new bool[_threadCount];
        var order = new List<int>(_threadCount);

        // Repeat until no further thread can be satisfied: that is the whole
        // algorithm. Each pass grants a thread its full remaining need, adds
        // the need back to what will be free, and marks it finished.
        for (int pass = 0; pass < _threadCount; pass++)
        {
            int progressed = -1;
            for (int t = 0; t < _threadCount; t++)
            {
                if (finished[t]) continue;
                bool canFinish = true;
                for (int r = 0; r < _resourceCount; r++)
                {
                    if (work[r] < _max[t, r] - _allocation[t, r])
                    {
                        canFinish = false;
                        break;
                    }
                }
                if (!canFinish) continue;

                for (int r = 0; r < _resourceCount; r++)
                {
                    // Release everything the thread holds, not just its
                    // remaining Need. At this point it has been given everything
                    // it asked for, so its Allocation equals its Max; adding
                    // Need alone would leave work short and reject every
                    // grantable request.
                    work[r] += _allocation[t, r];
                }
                finished[t] = true;
                order.Add(t);
                progressed = t;
                break;
            }
            if (progressed < 0)
            {
                return null;
            }
        }

        return order.ToArray();
    }
}