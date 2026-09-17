using System.Diagnostics;

/// <summary>
/// Process-stats cache guarded by a <see cref="ReaderWriterLockSlim"/>.
/// Many concurrent readers can take a snapshot without blocking each
/// other; a writer (Refresh) takes the write lock exclusively so it
/// does not race with readers. OSEP §31.5 reader-writer pattern.
/// </summary>
public static class RequestStatsCache
{
    private static readonly ReaderWriterLockSlim RwLock = new();
    private static int Threads;
    private static long WorkingSet;
    private static long PrivateBytes;
    private static DateTime LastRefreshUtc = DateTime.MinValue;

    /// <summary>
    /// Snapshot of the process stats. Acquires the read lock so
    /// concurrent callers do not block each other.
    /// </summary>
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

    /// <summary>
    /// Refreshes the cached values from the current process. Acquires
    /// the write lock exclusively, so any in-flight readers finish
    /// first; new readers arriving during the refresh block until the
    /// write lock is released.
    /// </summary>
    public static void Refresh()
    {
        var p = Process.GetCurrentProcess();
        int t = p.Threads.Count;
        long ws = p.WorkingSet64;
        long pb = p.PrivateMemorySize64;

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

    /// <summary>Time (UTC) of the last successful refresh. For smoke / future TTL.</summary>
    public static DateTime GetLastRefreshUtc()
    {
        RwLock.EnterReadLock();
        try
        {
            return LastRefreshUtc;
        }
        finally
        {
            RwLock.ExitReadLock();
        }
    }
}