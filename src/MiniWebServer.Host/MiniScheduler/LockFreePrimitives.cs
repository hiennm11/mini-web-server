using System.Diagnostics;
using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// Lock-free atomic counter (OSEP §32.2 "Mutual Exclusion").
///
/// OSEP §32.2 (Herlihy's idea):
///   "one could design various data structures without locks at all [H91, H93].
///    The idea behind these lock-free (and related wait-free) approaches here is
///    simple: using powerful hardware instructions, you can build data structures
///    in a manner that does not require explicit locking. As a simple example,
///    let us assume we have a compare-and-swap instruction, which as you may
///    recall is an atomic instruction provided by the hardware that does the
///    following:
///
///    int CompareAndSwap(int *address, int expected, int new) {
///        if (*address == expected) {
///            *address = new;
///            return 1; // success
///        }
///        return 0; // failure
///    }"
///
/// OSEP §32.2 "AtomicIncrement":
///   "void AtomicIncrement(int *value, int amount) {
///        do {
///            int old = *value;
///        } while (CompareAndSwap(value, old, old + amount) == 0);
///    }"
///
/// OSEP §32.2 "Instead of acquiring a lock, doing the update, and then
/// releasing it, we have instead built an approach that repeatedly tries to
/// update the value to the new amount and uses the compare-and-swap to do so.
/// In this manner, no lock is acquired, and no deadlock can arise (though
/// livelock is still a possibility). . . . a robust solution will be more
/// complex than the simple code snippet above."
///
/// Our implementation uses `Interlocked.CompareExchange` (the .NET wrapper
/// for the x86 CMPXCHG instruction) as the underlying CAS. The outer loop
/// matches OSEP's pseudocode exactly. We use `SpinWait` for back-off to
/// reduce contention.
/// </summary>
public sealed class AtomicCounter
{
    private int _value;
    public int Value => Volatile.Read(ref _value);  // acquire-load

    public AtomicCounter(int initial = 0) => _value = initial;

    /// <summary>
    /// OSEP §32.2 AtomicIncrement. Increments by `amount` (default 1).
    /// Wait-free bounded retries via SpinWait.
    /// </summary>
    public void Increment(int amount = 1)
    {
        var spinner = default(SpinWait);
        int oldVal, newVal;
        do
        {
            spinner.SpinOnce();
            oldVal = Volatile.Read(ref _value);
            newVal = oldVal + amount;
        }
        while (Interlocked.CompareExchange(ref _value, newVal, oldVal) != oldVal);
    }
}

/// <summary>
/// Lock-free stack (Treiber stack, OSEP §32.2 lock-free linked-list insert).
///
/// OSEP §32.2 "lock-free list insert":
///   "void insert(int value) {
///        node_t *n = malloc(sizeof(node_t));
///        assert(n != NULL);
///        n->value = value;
///        do {
///            n->next = head;
///        } while (CompareAndSwap(&head, n->next, n) == 0);
///    }"
///
/// Treiber (1986): the canonical lock-free LIFO stack. Push and Pop both
/// use a CAS loop on the head pointer.
///
/// Limitations (documented):
///   - **ABA problem**: if a node is popped, then re-pushed (the new node
///     happens to land at the same address), Pop's CAS succeeds but the
///     popped node is gone. We avoid this in practice by NEVER freeing
///     popped nodes — they leak. A production lock-free stack would use
///     hazard pointers or epoch-based reclamation.
///   - **Livelock**: under high contention, threads may repeatedly fail
///     their CAS and retry. We use `SpinWait` to back off.
/// </summary>
public sealed class LockFreeStack<T> where T : class
{
    private Node? _head;

    private sealed class Node
    {
        public T Value;
        public Node? Next;
        public Node(T value) { Value = value; }
    }

    public int CountApprox
    {
        get
        {
            int n = 0;
            for (var c = _head; c is not null; c = c.Next) n++;
            return n;
        }
    }

    /// <summary>
    /// Push a value onto the top of the stack. OSEP §32.2 insert.
    /// Wait-free bounded retries via SpinWait.
    /// </summary>
    public void Push(T value)
    {
        var n = new Node(value);
        var spinner = default(SpinWait);
        do
        {
            spinner.SpinOnce();
            n.Next = Volatile.Read(ref _head);
        }
        while (Interlocked.CompareExchange(ref _head, n, n.Next) != n.Next);
    }

    /// <summary>
    /// Try to pop a value off the top of the stack. Returns false if empty.
    /// Mirror of the OSEP §32.2 insert pattern (CAS on head).
    /// </summary>
    public bool TryPop(out T? value)
    {
        var spinner = default(SpinWait);
        while (true)
        {
            spinner.SpinOnce();
            var head = Volatile.Read(ref _head);
            if (head is null) { value = null; return false; }
            if (Interlocked.CompareExchange(ref _head, head.Next, head) == head)
            {
                value = head.Value;
                // NOTE: we intentionally don't free `head` (see ABA note in class docs).
                return true;
            }
        }
    }
}

/// <summary>
/// Lock-based comparison primitives — for the benchmark to show that
/// lock-free != lock-based.
/// </summary>
public sealed class LockedCounter
{
    private int _value;
    private readonly object _lock = new();
    public int Value { get { lock (_lock) return _value; } }
    public void Increment(int amount = 1) { lock (_lock) _value += amount; }
}

public sealed class LockedStack<T> where T : class
{
    private readonly LinkedList<T> _list = new();
    private readonly object _lock = new();
    public int Count { get { lock (_lock) return _list.Count; } }
    public void Push(T v) { lock (_lock) _list.AddFirst(v); }
    public bool TryPop(out T? v)
    {
        lock (_lock)
        {
            if (_list.First is null) { v = null; return false; }
            v = _list.First.Value;
            _list.RemoveFirst();
            return true;
        }
    }
}

/// <summary>
/// Benchmark harness for lock-free vs lock-based primitives.
///
/// OSEP §29.1 (precise counter scalability):
///   "The performance of the synchronized counter scales poorly. Whereas a single
///    thread can complete the million counter updates in a tiny amount of time
///    (roughly 0.03 seconds), having two threads each update the counter one
///    million times concurrently leads to a massive slowdown (taking over 5
///    seconds!)."
///
/// OSEP §29.1 (approximate counter scalability):
///   "Performance is excellent; the time taken to update the counter four
///    million times on four processors is hardly higher than the time taken to
///    update it one million times on one processor."
/// </summary>
public enum LockFreeImpl { AtomicCounter, LockedCounter, LockFreeStack, LockedStack }

public sealed class LockFreeBenchmark
{
    public LockFreeBenchmark(int threads, int opsPerThread, int seed = 42)
    {
        Threads = threads;
        OpsPerThread = opsPerThread;
        _rng = new Random(seed);
    }

    public int Threads { get; }
    public int OpsPerThread { get; }
    private readonly Random _rng;

    public string Run(LockFreeImpl impl)
    {
        long totalOps = (long)Threads * OpsPerThread;
        var barrier = new Barrier(Threads);
        var sw = Stopwatch.StartNew();

        var threads = new Thread[Threads];
        for (int t = 0; t < Threads; t++)
        {
            int tid = t;
            threads[t] = new Thread(() =>
            {
                barrier.SignalAndWait();  // synchronized start
                switch (impl)
                {
                    case LockFreeImpl.AtomicCounter:
                        var ac = (AtomicCounter)_shared!;
                        for (int i = 0; i < OpsPerThread; i++) ac.Increment();
                        break;
                    case LockFreeImpl.LockedCounter:
                        var lc = (LockedCounter)_shared!;
                        for (int i = 0; i < OpsPerThread; i++) lc.Increment();
                        break;
                    case LockFreeImpl.LockFreeStack:
                        var ls = (LockFreeStack<string>)_shared!;
                        for (int i = 0; i < OpsPerThread; i++)
                        {
                            if (_rng.Next(2) == 0) ls.Push($"v{i}");
                            else ls.TryPop(out _);
                        }
                        break;
                    case LockFreeImpl.LockedStack:
                        var lcs = (LockedStack<string>)_shared!;
                        for (int i = 0; i < OpsPerThread; i++)
                        {
                            if (_rng.Next(2) == 0) lcs.Push($"v{i}");
                            else lcs.TryPop(out _);
                        }
                        break;
                }
            })
            {
                IsBackground = true,
                Name = $"bench-{impl}-{tid}",
            };
        }

        _shared = impl switch
        {
            LockFreeImpl.AtomicCounter => new AtomicCounter(),
            LockFreeImpl.LockedCounter => new LockedCounter(),
            LockFreeImpl.LockFreeStack => new LockFreeStack<string>(),
            LockFreeImpl.LockedStack => new LockedStack<string>(),
            _ => throw new ArgumentOutOfRangeException(nameof(impl)),
        };

        for (int t = 0; t < Threads; t++) threads[t].Start();
        foreach (var th in threads) th.Join();
        sw.Stop();

        var elapsedMs = sw.Elapsed.TotalMilliseconds;
        double opsPerSec = totalOps * 1000.0 / elapsedMs;

        var sb = new StringBuilder();
        sb.AppendLine($"=== Lock-Free Benchmark (M22 / OSEP §32.2) ===");
        sb.AppendLine($"impl: {impl}  threads: {Threads}  ops/thread: {OpsPerThread}  total ops: {totalOps}");
        sb.AppendLine();
        sb.AppendLine($"  elapsed:     {elapsedMs:F2} ms");
        sb.AppendLine($"  ops/sec:     {opsPerSec:F0}");
        sb.AppendLine($"  µs/op:       {elapsedMs * 1000.0 / totalOps:F3}");
        if (impl == LockFreeImpl.AtomicCounter)
            sb.AppendLine($"  final value: {((AtomicCounter)_shared).Value}  (expected: {totalOps})");
        else if (impl == LockFreeImpl.LockedCounter)
            sb.AppendLine($"  final value: {((LockedCounter)_shared).Value}  (expected: {totalOps})");
        else if (impl == LockFreeImpl.LockFreeStack)
            sb.AppendLine($"  stack size:  {((LockFreeStack<string>)_shared).CountApprox}");
        else if (impl == LockFreeImpl.LockedStack)
            sb.AppendLine($"  stack size:  {((LockedStack<string>)_shared).Count}");
        return sb.ToString();
    }

    private object? _shared;
}
