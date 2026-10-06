using System.Runtime.ExceptionServices;
using System.Threading;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// A condition variable: "an explicit queue that threads can put themselves on
/// when some state of execution (i.e., some condition) is not as desired
/// (by waiting on the condition); some other thread, when it changes said
/// state, can then wake one (or more) of those waiting threads and thus allow
/// them to continue (by signaling on the condition)." (OSEP §30.1)
///
/// This is a real CV with its own wait queue, not a wrapper over
/// <see cref="Monitor"/>. That distinction is the whole point: §30.2's
/// producer/consumer solution needs <c>empty</c> and <c>fill</c> to be
/// <em>different</em> queues, so a consumer's <c>Signal(fill)</c> can never
/// wake a consumer parked on <c>empty</c>. <see cref="Monitor.Pulse(object)"/>
/// cannot express that — two CVs over one monitor are one wait queue, which is
/// exactly the "wake up the wrong type of thread" bug §30.2 walks through.
/// POSIX draws the same line for the same reason: <c>pthread_cond_t *c</c> is
/// an object separate from <c>pthread_mutex_t *m</c>.
/// </summary>
/// <remarks>
/// <para><b>Discipline.</b> Call <see cref="Wait"/> while holding the associated
/// lock exactly once — not re-entrantly — and call
/// <see cref="Signal"/>/<see cref="Broadcast"/> while holding it too. POSIX
/// assumes the same, and it is what makes the predicate check and the wait
/// atomic: releasing the lock and enqueuing are one step, so no signal can
/// land in the gap between "I decided to wait" and "I am waiting".</para>
///
/// <para><b>Mesa semantics.</b> A signal "only wakes them up; it is thus a hint
/// that the state of the world has changed ... but there is no guarantee that
/// when the woken thread runs, the state will still be as desired ...
/// Virtually every system ever built employs Mesa semantics." (OSEP §30.1)
/// <see cref="Wait"/> returns with the lock re-acquired but the predicate
/// unchecked, so every caller must re-check it: "using a while loop is always
/// correct; using an if statement only might be ... Thus, always use while"
/// (OSEP §30.2 TIP). <see cref="WaitWhile"/> bundles the idiom so it cannot be
/// forgotten.</para>
///
/// <para>This type is safe for concurrent use.</para>
/// </remarks>
public sealed class ConditionVariable
{
    /// <summary>One parked thread. <see cref="Released"/> is its private handoff slot.</summary>
    private sealed class Waiter
    {
        /// <summary>
        /// Spin count is zero: the whole point of a CV is to take the thread
        /// off the CPU, not to burn it before blocking (§30.1 opens by calling
        /// spinning "grossly inefficient" - the reason CVs exist at all).
        /// </summary>
        public readonly ManualResetEventSlim Released = new(false, 0);
    }

    /// <summary>
    /// Guards <see cref="_waiters"/> only. This is a leaf lock: it is never
    /// held while acquiring the caller's lock, otherwise a thread that already
    /// owns that lock and is calling <see cref="Signal"/> would deadlock
    /// against a waiter finishing <see cref="Wait"/>.
    /// </summary>
    private readonly object _queueLock = new();

    /// <summary>
    /// Threads parked on this CV, in arrival order. OSEP §30.1 promises no
    /// ordering, but FIFO makes the single-CV bug in §30.2 reproduce
    /// deterministically instead of rarely — which is the point of the demo.
    /// </summary>
    private readonly LinkedList<Waiter> _waiters = new();

    private long _waitCount;
    private long _signalCount;
    private long _broadcastWakeCount;

    /// <summary>Threads parked on this CV right now.</summary>
    public int WaitingCount
    {
        get { lock (_queueLock) return _waiters.Count; }
    }

    /// <summary>Threads that have parked on this CV since construction.</summary>
    public long WaitCount => Interlocked.Read(ref _waitCount);

    /// <summary>Signals that found a waiter and released it.</summary>
    public long SignalCount => Interlocked.Read(ref _signalCount);

    /// <summary>Waiters released by <see cref="Broadcast"/>.</summary>
    public long BroadcastWakeCount => Interlocked.Read(ref _broadcastWakeCount);

    /// <summary>
    /// Atomically releases <paramref name="lockObj"/> and parks the calling
    /// thread on this CV's queue; re-acquires <paramref name="lockObj"/> before
    /// returning.
    /// </summary>
    /// <remarks>
    /// "The responsibility of wait() is to release the lock and put the calling
    /// thread to sleep (atomically); when the thread wakes up (after some other
    /// thread has signaled it), it must re-acquire the lock before returning to
    /// the caller." (OSEP §30.1)
    /// </remarks>
    /// <exception cref="SynchronizationLockException">
    /// The caller does not hold <paramref name="lockObj"/>. Waiting without the
    /// lock is the single most damaging CV mistake — it reintroduces the race
    /// the CV exists to remove — so it fails loudly instead of hanging.
    /// </exception>
    public void Wait(object lockObj)
    {
        ArgumentNullException.ThrowIfNull(lockObj);
        if (!Monitor.IsEntered(lockObj))
        {
            throw new SynchronizationLockException(
                $"ConditionVariable.Wait requires holding '{lockObj}' when called. " +
                "OSEP §30.1 assumes the lock is held across the predicate check and the wait; " +
                "waiting without it makes the check-and-wait sequence racy again.");
        }

        var waiter = new Waiter();
        lock (_queueLock)
        {
            _waiters.AddLast(waiter);
        }
        Interlocked.Increment(ref _waitCount);

        // Release + park as one step: Monitor.Exit hands the lock on before we
        // block, so a signaler that acquires it can never find us parked while
        // still holding it, and our wakeup cannot race our own exit.
        //
        // One Exit is issued, which is correct for the documented discipline:
        // hold the lock exactly once across the wait. A recursive
        // `lock(gate) { lock(gate) { Wait(gate); } }` would leave the monitor
        // owned at depth 1 and the waiter would park still holding it, so a
        // discipline-following signaler could never acquire it. The BCL does
        // not expose recursion depth, so that case is documented as invalid
        // rather than detected.
        Monitor.Exit(lockObj);

        ExceptionDispatchInfo? failure = null;
        try
        {
            waiter.Released.Wait();
        }
        catch (Exception ex)
        {
            failure = ExceptionDispatchInfo.Capture(ex);
        }

        // Restore both invariants — dequeued, and holding the caller's lock
        // again — even when the wait was interrupted. If we returned without
        // ownership the caller's `lock` would throw from its own Exit and mask
        // whatever actually went wrong, so reacquisition is retried to
        // completion and the original failure is rethrown afterwards.
        //
        // The waiter node is deliberately not Disposed: a signaler that dequeued
        // us microseconds earlier may still be inside Set().
        for (; ; )
        {
            try
            {
                lock (_queueLock) { _waiters.Remove(waiter); }
                break;
            }
            catch (ThreadInterruptedException)
            {
                failure ??= ExceptionDispatchInfo.Capture(new ThreadInterruptedException());
            }
        }
        for (; ; )
        {
            try
            {
                Monitor.Enter(lockObj);
                break;
            }
            catch (ThreadInterruptedException)
            {
                failure ??= ExceptionDispatchInfo.Capture(new ThreadInterruptedException());
            }
        }

        failure?.Throw();
    }

    /// <summary>
    /// Releases the longest-waiting thread, if any. The woken thread must
    /// re-acquire the associated lock and re-check its predicate before acting
    /// (OSEP §30.1 Mesa semantics).
    /// </summary>
    public void Signal()
    {
        Waiter? target = null;
        lock (_queueLock)
        {
            if (_waiters.First is not null)
            {
                target = _waiters.First.Value;
                _waiters.RemoveFirst();
                Interlocked.Increment(ref _signalCount);
            }
        }

        // Set outside _queueLock: the woken thread immediately races for the
        // caller's lock, and holding _queueLock across that handoff only adds
        // contention.
        target?.Released.Set();
    }

    /// <summary>
    /// Releases every thread parked on this CV. Each re-checks its own
    /// predicate under the lock; those whose condition is still unmet sleep
    /// again. Use when the signaler cannot tell which waiter can make progress
    /// — "Lampson and Redell call such a condition a covering condition, as it
    /// covers all the cases where a thread needs to wake up (conservatively);
    /// the cost ... is that too many threads might be woken." (OSEP §30.3)
    /// </summary>
    public void Broadcast()
    {
        List<Waiter> targets;
        lock (_queueLock)
        {
            targets = new List<Waiter>(_waiters);
            _waiters.Clear();
        }
        if (targets.Count == 0)
        {
            return;
        }
        Interlocked.Add(ref _broadcastWakeCount, targets.Count);
        foreach (var waiter in targets)
        {
            waiter.Released.Set();
        }
    }

    /// <summary>
    /// Parks while <paramref name="predicate"/> holds — the canonical idiom.
    /// </summary>
    /// <remarks>
    /// Callers pass the condition that means "still not ready", which is the
    /// shape OSEP writes: <c>while (count == 0) pthread_cond_wait(&amp;cond, &amp;mutex);</c>
    /// (§30.2 figure 30.10) and <c>while (bytesLeft &lt; size) ...</c> (§30.3
    /// figure 30.15). Using <c>while</c> rather than <c>if</c> is what makes
    /// Mesa semantics and spurious wakeups safe: "using a while loop is always
    /// correct; using an if statement only might be ... Thus, always use while"
    /// (§30.2 TIP).
    ///
    /// The naming matches <c>SpinWait.SpinWhile</c>: <c>predicate</c> is the
    /// reason to keep waiting, not the reason to stop.
    /// </remarks>
    public void WaitWhile(object lockObj, Func<bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        while (predicate())
        {
            Wait(lockObj);
        }
    }
}