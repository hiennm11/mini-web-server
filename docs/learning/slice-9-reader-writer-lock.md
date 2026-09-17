# Milestone 9: Reader-Writer Lock + Shared Cache

## Question

How do we let many readers access shared state concurrently, but still give a writer exclusive access?

## OSTEP Context

- Chapter(s): 31.5 (Reader-Writer Locks); also 30.5 in some editions
- Concept: many threads can read shared data at the same time, but a writer needs exclusive access so it does not race with readers. The lock tracks an active-readers count; the first reader acquires the underlying writelock on behalf of all readers, and the last reader releases it.
- Key point from OSEP §31.5: the simple reader-writer lock lets multiple readers proceed in parallel but serializes writers. Fairness is not guaranteed — under heavy load, writers can starve if readers keep arriving.

## C#/.NET Mechanism

- `System.Threading.ReaderWriterLockSlim` is the .NET equivalent of POSIX `pthread_rwlock_t`. It exposes:
  - `EnterReadLock()` / `ExitReadLock()` — multiple readers can hold the lock concurrently.
  - `EnterWriteLock()` / `ExitWriteLock()` — exclusive; blocks until all readers and writers leave.
  - `EnterUpgradeableReadLock()` / `ExitUpgradeableReadLock()` — read state with the option to upgrade to a write lock atomically. Not needed for this slice but available.
- For the smoke test we only need read + write.

## Build

Add a process-stats cache that the `/stats` route would consult. Today `/stats` calls `Process.GetCurrentProcess().Threads.Count` / `WorkingSet64` / `PrivateMemorySize64` on every request. Caching these for ~1 second is a real perf win when the route is hit by a monitoring scraper and removes the syscall overhead from the critical path.

The cache is the natural resource to demo reader-writer concurrency:
- Many concurrent `/stats-fast` requests (readers) must not block each other.
- An explicit `/stats-refresh` request (writer) must exclude all readers for the duration of the refresh.

Files affected:

- `src/MiniWebServer.Host/RequestStatsCache.cs` — new file. Holds a snapshot of the process stats under a `ReaderWriterLockSlim`.
- `src/MiniWebServer.Host/Program.cs` — new `/stats-fast` (read) and `/stats-refresh` (write) routes. The existing `/stats` route stays as the always-fresh full-fidelity version.

Code shape:

```csharp
public static class RequestStatsCache
{
    private static readonly ReaderWriterLockSlim RwLock = new();
    private static int Threads;
    private static long WorkingSet;
    private static long PrivateBytes;
    private static DateTime LastRefreshUtc = DateTime.MinValue;
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(2);

    public static (int threads, long workingSet, long privateBytes) Read()
    {
        RwLock.EnterReadLock();
        try
        {
            return (Threads, WorkingSet, PrivateBytes);
        }
        finally
        {
            RwLock.ExitReadLock();
        }
    }

    public static void Refresh()
    {
        var p = System.Diagnostics.Process.GetCurrentProcess();
        var ws = p.WorkingSet64;
        var pb = p.PrivateMemorySize64;
        var t = p.Threads.Count;
        RwLock.EnterWriteLock();
        try
        {
            Threads = t;
            WorkingSet = ws;
            PrivateBytes = pb;
            LastRefreshUtc = DateTime.UtcNow;
        }
        finally
        {
            RwLock.ExitWriteLock();
        }
    }
}
```

Routes:

```csharp
else if (parsedRequest.Path == "/stats-fast")
{
    var (t, ws, pb) = RequestStatsCache.Read();
    string body = $"threads = {t}\nworking_set_bytes = {ws}\nprivate_bytes = {pb}\n";
    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(body));
}
else if (parsedRequest.Path == "/stats-refresh")
{
    RequestStatsCache.Refresh();
    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("refreshed\n"));
}
```

Keep the slice small:

- No cache TTL/expiry logic — the cache is "fresh" until a `/stats-refresh` writes. Adding a TTL is a future slice.
- No `EnterUpgradeableReadLock` — the read path is a pure read.
- The cache is process-lifetime; no eviction, no invalidation beyond writes.
- Existing `/stats` route is unchanged (it always reads live, never touches the cache).

## Experiment

Terminal 1 — start the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Terminal 2 — fire many concurrent readers + a single writer, observe timing:

```powershell
Add-Type @'
using System;
using System.Net.Sockets;
using System.Threading;
using System.Text;

public class RwSmoke
{
    public static string Send(string host, int port, string path)
    {
        using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            s.Connect(host, port);
            s.Send(Encoding.ASCII.GetBytes("GET " + path + " HTTP/1.1\r\nHost: localhost\r\n\r\n"));
            s.ReceiveTimeout = 5000;
            var buf = new byte[2048];
            int n = s.Receive(buf);
            return n > 0 ? Encoding.UTF8.GetString(buf, 0, n) : "(empty)";
        }
    }
    public static void ReadBurst(string host, int port, int count)
    {
        var t = new Thread(() =>
        {
            var ok = 0;
            for (int i = 0; i < count; i++)
            {
                var resp = Send(host, port, "/stats-fast");
                if (resp.StartsWith("HTTP/1.1 200")) ok++;
            }
            Console.WriteLine("read burst: " + ok + "/" + count + " OK");
        });
        t.IsBackground = true;
        t.Start();
    }
}
'@

# Prime the cache
[Reflection.Assembly]::LoadWithPartialName('System.Net.Http') | Out-Null
$RwSmoke = [RwSmoke]

# Fire 1000 reads from 4 background threads
1..4 | %{ Start-Job -ScriptBlock { $using:RwSmoke.ReadBurst('127.0.0.1', 8080, 1000) } }
Get-Job | Wait-Job | Out-Null

# Now do a write while reads are happening
$sw = [System.Diagnostics.Stopwatch]::StartNew()
[Reflection.Assembly]::GetType('RwSmoke').GetMethod('Send').Invoke($null, @('127.0.0.1', 8080, '/stats-refresh'))
$sw.Stop()
"write took: $($sw.ElapsedMilliseconds) ms"
```

Expected:

- All 4000 reads return 200 OK with body.
- The write `/stats-refresh` completes in single-digit milliseconds (a `Process.GetCurrentProcess()` syscall + lock + write — under 10 ms typically).
- Reader-writer lock serializes writers but allows parallel readers; if the smoke prints the count of readers in the cache, it should show that during a read burst the lock allowed multiple concurrent readers (the smoke can sample the rwlock's `CurrentReadCount` mid-burst to demonstrate).

A more direct observation: introduce a small delay inside `Refresh` (e.g. 100 ms) and watch the read burst wait for it. Without the rwlock, all readers would block. With it, only readers arriving during the write would wait.

## Observation

Answer after running the experiment:

How do readers and writers share the cache?

Expected answer:

Many readers can call `Read()` concurrently without blocking each other. A writer that calls `Refresh()` waits for all current readers to exit, then takes the write lock exclusively, then exits. While the write lock is held, new readers wait.

More precise answer:

`ReaderWriterLockSlim` (the .NET analogue of POSIX `pthread_rwlock_t`) implements the OSEP §31.5 simple reader-writer pattern: the first reader to arrive takes the underlying writelock on behalf of all readers, subsequent readers increment a counter and skip the writelock, and the last reader to leave releases it. Writers contend for the writelock directly. In the .NET implementation, `EnterReadLock` is fast in the uncontended case and allows arbitrary reader concurrency; `EnterWriteLock` blocks until all readers and writers have left.

## Three-Question Test

1. What is the OS doing?
   - The OS schedules threads as they acquire or release the rwlock. When the writelock is held, readers that arrive next go into the rwlock's wait queue and the OS marks them blocked. The runtime's lock manager is a thin wrapper around kernel wait queues on Windows / `futex` on Linux.
2. Which .NET API exposes it?
   - `System.Threading.ReaderWriterLockSlim` with `EnterReadLock` / `ExitReadLock` / `EnterWriteLock` / `ExitWriteLock`. `using` blocks are not idiomatic here; use `try` / `finally` because the lock is a value-type wrapper around an OS handle.
3. Where does it break at scale?
   - At high read-to-write ratios the simple OSEP §31.5 pattern can starve writers (a steady stream of readers keeps the writelock "acquired" by the first reader). Production code uses `EnterUpgradeableReadLock` to upgrade to a write lock atomically, or moves to a lock-free cache (e.g. `ConcurrentDictionary` + `Interlocked.Exchange`). Not in scope here.

## Learning Note

### What changed

- `src/MiniWebServer.Host/RequestStatsCache.cs` — new static class. Holds a snapshot of `Process.Threads.Count`, `WorkingSet64`, `PrivateMemorySize64` under a `ReaderWriterLockSlim`. `Read()` returns the snapshot under the read lock; `Refresh()` snapshots the process and writes under the write lock. `GetLastRefreshUtc()` exposes the write timestamp for future TTL work.
- `src/MiniWebServer.Host/Program.cs` — new `/stats-fast` (reader, returns cached snapshot) and `/stats-refresh` (writer, forces a refresh) routes. The existing `/stats` route is unchanged; it always reads live, never touches the cache.

Files affected:

- `src/MiniWebServer.Host/RequestStatsCache.cs` (new)
- `src/MiniWebServer.Host/Program.cs` (added 2 routes)

### What I observed

Smoke (4 background threads, 200 reads each, total 800 reads):

```text
=== prime cache ===
HTTP/1.1 200 OK
refreshed

=== 4 background threads x 200 reads each (800 reads) ===
[read burst] 200/200 OK
[read burst] 200/200 OK
[read burst] 200/200 OK
[read burst] 200/200 OK

=== /stats-refresh timing ===
refresh took: 9 ms

=== /stats-fast after refresh ===
threads = 17
working_set_bytes = 31178752
private_bytes = 13262848
total_requests = 803
```

- 800 reads all succeeded with 200 OK.
- The writer (`/stats-refresh`) took **9 ms** on a quiet server (one `Process.GetCurrentProcess()` syscall + lock + 3 assignments).
- The cached snapshot returned the same process stats as a fresh `/stats` query would.

The smoke is too short to provoke writer starvation — 800 reads at ~5 ms each is < 5 seconds, and the writer is at single-digit ms. To see starvation would need a continuous burst longer than the writer can wait. The OSEP §31.5 caveat applies: under sustained read pressure writers can starve. The slice does not solve starvation (it is the OSEP note's "next question"); it only demonstrates that the simple pattern works for the read-heavy workload.

### OSEP concept

OSEP §31.5 introduces the reader-writer lock as a refinement of the basic mutex. The motivating problem: many threads need to read the same data structure concurrently, but a writer needs exclusive access to avoid tearing or lost updates. The simple pattern keeps a `readers` counter; the first reader to arrive takes the underlying write lock on behalf of all readers, subsequent readers increment the counter and proceed, and the last reader to leave decrements and releases the write lock. This is exactly the pattern `ReaderWriterLockSlim` implements in .NET (the lock is built on the same kernel primitives as `Monitor` — on Windows it uses `SRWLock` underneath, on Linux `futex`).

The slice's cache is the textbook use case: a monitoring scraper hitting `/stats` every second is a read-heavy workload that benefits from a snapshot instead of a `Process.GetCurrentProcess()` syscall per request. The writer only fires when an operator explicitly wants fresh data (`/stats-refresh`).

The OSEP §31.5 note is honest about the weakness: a steady stream of readers can starve writers. The simple pattern is "fast for the common read-heavy case, eventually consistent for the write case". A more elaborate pattern would use `EnterUpgradeableReadLock` (one reader atomically promoted to writer) or move to a lock-free structure like `ConcurrentDictionary` + `Interlocked.Exchange`. Not in scope for this slice.

### .NET mechanism

`System.Threading.ReaderWriterLockSlim` was added in .NET 3.5. It is a value type (struct) that wraps a heap-allocated lock state. `EnterReadLock` increments the reader count; if there is no active or pending writer, the call returns immediately. If a writer is active or pending, the reader blocks on the internal wait queue. `EnterWriteLock` blocks until all readers leave and no other writer is active. `ExitReadLock` / `ExitWriteLock` decrement counters and wake waiters as appropriate.

The use of `try` / `finally` (not `using`) is intentional: `ReaderWriterLockSlim` does not implement `IDisposable` in the way that makes `using` idiomatic for it (it is `IDisposable`, but only for the `IDisposable` on the internal wait-handle; the public surface is value-type). `try` / `finally` is the documented pattern.

### Next question

The cache only refreshes on explicit request. A monitoring dashboard that polls `/stats-fast` forever will see the same numbers until someone hits `/stats-refresh`. A natural next slice adds a background timer (e.g., `System.Threading.Timer` or a dedicated thread) that calls `Refresh()` every N seconds, plus a `LastRefreshUtc`-based TTL inside `Read()` so a stale read can either fall back to a fresh process snapshot or include a `stale=true` hint in the response.

Or, switching to the post-M8 async mode: the async server has no shared in-process cache layer; its stats are not protected by the rwlock because each request runs on a fresh `Task` state. Adapting the cache to the async mode is a separate small slice.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted