using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>Outcome of one condition-variable demo.</summary>
public sealed record CvDemoResult(
    string Scenario,
    bool Completed,
    string Headline,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Notes);

/// <summary>
/// The four OSEP Ch. 30 scenarios, run side by side so the difference between
/// a broken program and its fixed replacement is visible in one response.
///
/// Every broken case hangs on purpose. The demos therefore run each scenario
/// on its own background thread with a bounded join, and report a timeout as
/// the result rather than wedging the server.
/// </summary>
public static class ConditionVariableDemos
{
    /// <summary>
    /// How long a demo gets before we declare it stuck. Generous enough that a
    /// healthy run never trips it, short enough to keep an HTTP response quick.
    /// </summary>
    private const int DemoTimeoutMs = 4000;

    /// <summary>Routes every demo, each with its own timeout.</summary>
    public static CvDemoResult Run(string scenario)
    {
        return scenario switch
        {
            "lost-wakeup" => RunLostWakeup(),
            "single-cv" => RunSingleCv(),
            "two-cv" => RunTwoCv(),
            "covering-condition" => RunCoveringCondition(),
            _ => throw new ArgumentException(
                $"unknown cv scenario '{scenario}' (use lost-wakeup, single-cv, two-cv, covering-condition)",
                nameof(scenario)),
        };
    }

    /// <summary>Formats a result as the plain text the /cv/run route returns.</summary>
    public static string Format(CvDemoResult result)
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

    /// <summary>
    /// Runs <paramref name="body"/> on a worker thread and joins with a
    /// deadline. A body that never returns is the OSTEP §30.1/§30.2 bug, so a
    /// timeout is data, not a test failure.
    /// </summary>
    /// <remarks>
    /// A timed join ends the *caller's* wait, not the worker. Because
    /// <see cref="RunBounded"/> is called per HTTP request, leaving workers
    /// parked would leak a native thread and its stack on every request — so
    /// <paramref name="body"/> is responsible for parking nothing once it is
    /// told to stop; every scenario sets a shutdown predicate under its own gate
    /// and broadcasts before returning.
    /// </remarks>
    private static bool RunBounded(ThreadStart body, out Exception? failure)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { captured = ex; }
        })
        {
            IsBackground = true,
            Name = "cv-demo",
        };
        thread.Start();
        bool finished = thread.Join(DemoTimeoutMs);
        failure = captured;
        return finished;
    }

    /// <summary>
    /// §30.1 "Thread Trace: The Lost-Wakeup Bug" (Figure 30.4).
    ///
    /// A child finishes and signals, but the parent was never asleep on the
    /// condition, so the signal is dropped and the parent waits forever:
    /// "the child will signal, but there is no thread asleep on the condition.
    /// When the parent runs, it will simply call wait and be stuck" (§30.1).
    ///
    /// The fixed variant keeps OSEP's <c>done</c> state variable: "you should
    /// appreciate the importance of the state variable done; it records the value
    /// the threads are interested in knowing. The sleeping, waking, and locking
    /// all are built around it." (§30.1)
    /// </summary>
    private static CvDemoResult RunLostWakeup()
    {
        // --- broken: no state variable, so the signal is lost ---
        // The parked thread uses a bare Wait with no predicate, so there is
        // nothing to un-block it once the observation is recorded — it needs an
        // explicit broadcast, or it would hold a thread for the process lifetime.
        var brokenGate = new object();
        var brokenCv = new ConditionVariable();
        bool brokenChildRan = false;

        bool brokenCompleted = RunBounded(() =>
        {
            var child = new Thread(() =>
            {
                lock (brokenGate)
                {
                    brokenChildRan = true;
                    brokenCv.Signal();
                }
            })
            { IsBackground = true };
            child.Start();
            child.Join();

            lock (brokenGate)
            {
                if (brokenChildRan)
                {
                    // Broken form waits unconditionally. The signal already
                    // happened, so this parks until we abandon it below.
                    brokenCv.Wait(brokenGate);
                }
            }
        }, out _);

        if (!brokenCompleted)
        {
            lock (brokenGate)
            {
                brokenCv.Broadcast();
            }
        }

        // --- fixed: predicate + while, so the join never sleeps at all ---
        bool fixedCompleted = RunBounded(() =>
        {
            var gate = new object();
            var cv = new ConditionVariable();
            bool done = false;

            var child = new Thread(() =>
            {
                lock (gate)
                {
                    done = true;
                    cv.Signal();
                }
            })
            { IsBackground = true };
            child.Start();

            lock (gate)
            {
                cv.WaitWhile(gate, () => !done);
            }
        }, out _);

        var lines = new List<string>
        {
            $"broken (no state variable)          : {(brokenCompleted ? "completed" : "HUNG - lost the signal, parent waits forever")}",
            $"fixed  (predicate + while loop)     : {(fixedCompleted ? "completed" : "HUNG")}",
            "",
            "broken trace:",
            "  child : lock; done = 1; signal; unlock   <- nobody asleep, signal dropped",
            "  parent: lock; wait(); ...                 <- parks forever, never re-checked",
            "",
            "fixed trace:",
            "  child : lock; done = 1; signal; unlock",
            "  parent: lock; while (!done) wait();       <- done is already 1, so it never waits",
        };

        var notes = new List<string>
        {
            "OSEP §30.1: without the state variable the signal can land before anyone waits,",
            "and nothing ever wakes the parent. The state variable, not the CV, is what carries",
            "the fact across the window.",
            "The fixed parent never even parks in this interleaving - that is the point of the while.",
        };

        return new CvDemoResult(
            "lost-wakeup",
            fixedCompleted && !brokenCompleted,
            "=== Condition variable: lost-wakeup bug (M29 / OSEP §30.1) ===",
            lines,
            notes);
    }

    /// <summary>
    /// §30.2 "Better, But Still Broken: While, Not If" (Figure 30.10).
    ///
    /// The textbook's second problem: one CV shared by producers and consumers.
    /// A consumer that empties the buffer signals the single CV, and wakes
    /// whichever thread is at the head of that one queue — often another
    /// consumer. "A consumer should not wake other consumers, only producers,
    /// and vice-versa." (§30.2)
    ///
    /// This is why the shared CV cannot be <c>Monitor.Pulse</c>: with one
    /// monitor behind both roles there is no way to express "wake a producer"
    /// at all. <see cref="ConditionVariable"/> has its own queue per instance, so
    /// <c>empty</c> and <c>fill</c> are genuinely separable — which is what
    /// <see cref="RunTwoCv"/> relies on.
    /// </summary>
    private static CvDemoResult RunSingleCv()
    {
        // Figure 30.11's trace needs one producer, two consumers and a
        // single-slot buffer, with the consumers parked *before* the producer
        // runs. Anything looser is a coin flip: usually a consumer's signal
        // happens to reach the producer and the program limps along, which
        // would not teach the bug. The order is staged so the wrong wakeup is
        // forced.
        const int capacity = 1;
        const int total = 4;

        var buffer = new Queue<int>(capacity);
        var gate = new object();
        var cv = new ConditionVariable();
        int produced = 0, consumed = 0;
        bool abandon = false;

        // Stage 1: both consumers park on the empty buffer (figure 30.11, c1-c3).
        var consumers = new List<Thread>();
        for (int c = 0; c < 2; c++)
        {
            int consumerId = c;
            consumers.Add(new Thread(() =>
            {
                lock (gate)
                {
                    // while, not if (§30.2 figure 30.10) - correct, and still
                    // not enough: there is only one condition to wait on.
                    while (buffer.Count == 0 && produced < total && !abandon)
                    {
                        cv.Wait(gate);
                    }
                    if (buffer.Count == 0 || abandon)
                    {
                        return;
                    }
                    buffer.Dequeue();
                    Interlocked.Increment(ref consumed);
                    // Meant for a producer. The single queue hands it to
                    // whichever waiter is at the head - a consumer (§30.2).
                    cv.Signal();
                }
            })
            { IsBackground = true, Name = $"consumer-{consumerId}" });
        }
        foreach (var t in consumers) t.Start();
        SpinWait.SpinUntil(() => cv.WaitingCount == 2, DemoTimeoutMs);
        int parkedBefore = cv.WaitingCount;

        // Stage 2: the producer enqueues one item and signals (waking one
        // consumer), then parks because the single slot is full. It holds the
        // gate for the whole loop, so it is queued as a waiter before either
        // consumer can act — which is the figure-30.11 ordering.
        var producer = new Thread(() =>
        {
            lock (gate)
            {
                for (int i = 0; i < total && !abandon; i++)
                {
                    while (buffer.Count == capacity && !abandon)
                    {
                        cv.Wait(gate);
                    }
                    if (abandon)
                    {
                        return;
                    }
                    buffer.Enqueue(i);
                    produced++;
                    cv.Signal();
                }
            }
        })
        { IsBackground = true, Name = "producer" };
        producer.Start();

        bool finished = RunBounded(() =>
        {
            producer.Join();
            foreach (var t in consumers) t.Join();
        }, out _);

        int stillParked = cv.WaitingCount;
        int aliveAfterTimeout = producer.IsAlive ? 1 : 0;
        aliveAfterTimeout += consumers.Count(t => t.IsAlive);

        // Release the participants before returning: a timed join ends our wait,
        // not theirs, and this runs per HTTP request.
        if (!finished)
        {
            lock (gate)
            {
                abandon = true;
                cv.Broadcast();
            }
            producer.Join(DemoTimeoutMs);
            foreach (var t in consumers) t.Join(DemoTimeoutMs);
        }

        bool correct = finished && produced == total && consumed == total;

        var lines = new List<string>
        {
            $"producers=1 consumers=2 buffer capacity={capacity} items={total}",
            "condition variables in use: 1 (shared by both roles)",
            "",
            $"consumers parked before the producer ran: {parkedBefore}",
            $"produced={produced} consumed={consumed} still_pending={produced - consumed}",
            $"threads parked on the single CV: {stillParked}",
            $"result: {(finished ? "all threads joined" : $"HUNG - {aliveAfterTimeout} thread(s) still parked with {produced}/{total} items moved")}",
            "",
            "trace (figure 30.11):",
            "  c1,c2  both find the buffer empty -> park on the single CV",
            "  p1     puts one item in, signals -> wakes c1, buffer full -> parks",
            "  c1     wakes, consumes, signals -> wakes c2 (WRONG: a producer needed the wake)",
            "  c2     wakes, buffer empty, re-checks while -> parks again",
            "        p1 is still parked and nobody will ever signal it again: the",
            "        only thread that could have released it spent its signal on c2.",
        };

        var notes = new List<string>
        {
            "OSEP §30.2: with one CV the signal has no addressee. A consumer that empties the",
            "buffer must wake a producer, but wakes whatever sits at the head of the one queue -",
            "often another consumer, which finds the buffer empty and goes back to sleep.",
            "",
            "using a while loop instead of if fixes the first problem (§30.2 figure 30.10) but not",
            "this one. Unblocking the producer needs a second, producer-only condition.",
        };

        return new CvDemoResult(
            "single-cv",
            correct,
            "=== Condition variable: one CV shared by both roles (M29 / OSEP §30.2, broken) ===",
            lines,
            notes);
    }

    /// <summary>
    /// §30.2 "The Single Buffer Producer/Consumer Solution" (Figure 30.12):
    /// two condition variables, one lock. "In the code, producer threads wait on
    /// the condition empty, and signals fill. Conversely, consumer threads wait
    /// on fill and signal empty. By doing so, the second problem above is
    /// avoided by design: a consumer can never accidentally wake a consumer,
    /// and a producer can never accidentally wake a producer." (§30.2)
    /// </summary>
    private static CvDemoResult RunTwoCv()
    {
        const int producers = 2;
        const int consumers = 3;
        const int perProducer = 500;
        const int total = producers * perProducer;
        const int capacity = 8;

        var buffer = new Queue<int>(capacity);
        var gate = new object();
        var empty = new ConditionVariable();
        var fill = new ConditionVariable();
        int produced = 0, consumed = 0, consumedSum = 0, maxObserved = 0;
        // Set only after the run is judged hung, to let a parked participant
        // observe termination. Never set on the happy path.
        bool abandon = false;

        var threads = new List<Thread>();
        for (int p = 0; p < producers; p++)
        {
            int producerId = p;
            threads.Add(new Thread(() =>
            {
                for (int i = 0; i < perProducer; i++)
                {
                    lock (gate)
                    {
                        empty.WaitWhile(gate, () => buffer.Count == capacity && !abandon);
                        if (abandon)
                        {
                            return;
                        }
                        buffer.Enqueue(producerId * perProducer + i);
                        Interlocked.Increment(ref produced);
                        fill.Signal();
                    }
                }
            })
            { IsBackground = true, Name = $"producer-{producerId}" });
        }
        for (int c = 0; c < consumers; c++)
        {
            int consumerId = c;
            threads.Add(new Thread(() =>
            {
                while (true)
                {
                    lock (gate)
                    {
                        // Same shape as the producers: park only while there is
                        // genuinely no work left. Keying the exit on `produced`
                        // rather than on a local counter means the consumer that
                        // empties the buffer last releases the others via the
                        // `fill` broadcast below.
                        while (buffer.Count == 0 && produced < total && !abandon)
                        {
                            fill.Wait(gate);
                        }
                        if (buffer.Count == 0 || abandon)
                        {
                            return;
                        }
                        consumedSum += buffer.Dequeue();
                        Interlocked.Increment(ref consumed);
                        int depth = buffer.Count + 1;
                        if (depth > maxObserved) maxObserved = depth;
                        // A slot freed, so a producer may proceed. If everything
                        // is produced, wake the remaining consumers too so they
                        // can observe termination instead of parking forever.
                        if (produced < total)
                        {
                            empty.Signal();
                        }
                        else
                        {
                            fill.Broadcast();
                        }
                    }
                }
            })
            { IsBackground = true, Name = $"consumer-{consumerId}" });
        }

        bool finished = RunBounded(() =>
        {
            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();
        }, out _);

        int stranded = threads.Count(t => t.IsAlive);
        if (!finished)
        {
            lock (gate)
            {
                abandon = true;
                empty.Broadcast();
                fill.Broadcast();
            }
            foreach (var t in threads) t.Join(DemoTimeoutMs);
        }

        int expectedSum = total * (total - 1) / 2;
        bool correct = finished
                       && produced == total
                       && consumed == total
                       && consumedSum == expectedSum
                       && maxObserved <= capacity;

        var lines = new List<string>
        {
            $"producers={producers} consumers={consumers} items={total} capacity={capacity}",
            "condition variables: 2 (empty = producers wait, fill = consumers wait)",
            "",
            $"produced={produced} consumed={consumed}",
            $"every value consumed exactly once: {consumedSum == expectedSum} (sum={consumedSum}, expected={expectedSum})",
            $"max observed depth={maxObserved} (capacity={capacity})",
            $"signals: empty={empty.SignalCount} fill={fill.SignalCount}",
            $"result: {(finished ? "all threads joined" : $"HUNG - {stranded} thread(s) still parked")}",
        };

        var notes = new List<string>
        {
            "OSEP §30.2: two CVs make the signal's addressee explicit. A consumer signals empty,",
            "which only producers ever wait on, so a consumer can never wake a consumer.",
            "",
            "Every item is produced once and consumed once (the sum matches 0..total-1), and the",
            "buffer never exceeds its capacity - the buffer is invisible above the lock, which is",
            "the §30.2 interface contract.",
        };

        return new CvDemoResult(
            "two-cv",
            correct,
            "=== Condition variable: two CVs, one lock (M29 / OSEP §30.2, correct) ===",
            lines,
            notes);
    }

    /// <summary>
    /// §30.3 "Covering Conditions" (Figure 30.15). A memory allocator where
    /// waiters need different amounts, so the freer cannot tell who to wake:
    /// "the thread waking other threads does not know which thread (or threads)
    /// to wake up" (§30.3). Broadcast covers every case at the cost of waking
    /// threads that immediately re-sleep.
    /// </summary>
    private static CvDemoResult RunCoveringCondition()
    {
        // bytesLeft starts at 0. Ta wants 100, Tb wants 10. Both park.
        // Then a free of 50 makes Tb satisfiable but not Ta.
        var gate = new object();
        var cv = new ConditionVariable();
        int bytesLeft = 0;
        var log = new List<string>();
        int allocated = 0;
        // Set after the observation is recorded so Ta can leave. Ta staying
        // parked *is* the §30.3 result, but a parked thread per request would
        // leak, and this runs once per HTTP call.
        bool done = false;

        var bigWaiter = new Thread(() =>
        {
            lock (gate)
            {
                cv.WaitWhile(gate, () => bytesLeft < 100 && !done);
                if (done)
                {
                    return;
                }
                bytesLeft -= 100;
                allocated++;
                log.Add("Ta (needs 100) woke and allocated");
            }
        })
        { IsBackground = true, Name = "big-waiter" };

        var smallWaiter = new Thread(() =>
        {
            lock (gate)
            {
                cv.WaitWhile(gate, () => bytesLeft < 10 && !done);
                if (done)
                {
                    return;
                }
                bytesLeft -= 10;
                allocated++;
                log.Add("Tb (needs 10) woke and allocated");
            }
        })
        { IsBackground = true, Name = "small-waiter" };

        bigWaiter.Start();
        smallWaiter.Start();

        // Let both park before freeing, so the bug is about addressee choice
        // rather than timing.
        SpinWait.SpinUntil(() => cv.WaitingCount == 2, DemoTimeoutMs);
        int parked = cv.WaitingCount;

        lock (gate)
        {
            bytesLeft += 50;
            log.Add("Tc freed 50 bytes -> 50 available, waiters: Ta(100) unsatisfied, Tb(10) satisfied");
            // Signal would be a coin flip here. Broadcast covers the case:
            // Tb proceeds, Ta re-checks and parks again.
            cv.Broadcast();
        }

        bool tbProgressed = smallWaiter.Join(DemoTimeoutMs);
        bool taProgressed = bigWaiter.Join(DemoTimeoutMs);

        // Ta cannot finish with only 50 bytes - it must still be parked.
        bool taStillWaiting = !taProgressed;

        long released = cv.BroadcastWakeCount;
        if (!taProgressed)
        {
            lock (gate)
            {
                done = true;
                cv.Broadcast();
            }
            bigWaiter.Join(DemoTimeoutMs);
            smallWaiter.Join(DemoTimeoutMs);
        }

        var lines = new List<string>
        {
            "memory allocator: 0 bytes free initially",
            $"waiters parked before the free: {parked} (Ta needs 100, Tb needs 10)",
            "",
            string.Join("\n", log.Select(l => "  " + l)),
            "",
            $"Tb (10-byte request, satisfied by the 50 free)   : {(tbProgressed ? "allocated" : "STILL PARKED - signal woke the wrong thread")}",
            $"Ta (100-byte request, still unsatisfied)            : {(taProgressed ? "allocated" : "correctly still parked after re-check")}",
            $"broadcast released: {released} waiter(s)",
        };

        var notes = new List<string>
        {
            "OSEP §30.3: signal() from free() cannot know whom to wake, because it does not know",
            "how much each waiter needs. Broadcast covers the condition conservatively; Ta wakes,",
            "fails its while (!predicate) re-check, and goes back to sleep.",
            "",
            "A signal here would have woken Ta half the time, leaving Tb asleep despite being",
            "satisfiable - the exact failure §30.3 calls out.",
        };

        return new CvDemoResult(
            "covering-condition",
            tbProgressed && taStillWaiting,
            "=== Condition variable: covering condition (M29 / OSEP §30.3) ===",
            lines,
            notes);
    }
}