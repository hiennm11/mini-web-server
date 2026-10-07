using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// Catch probability for a scrubbing schedule (OSEP §45.7).
/// </summary>
/// <remarks>
/// OSEP §45.7 says what scrubbing is for and gives a schedule in units of time -
/// "Typical systems schedule scans on a nightly or weekly basis" - but states no
/// probability model. The model below is derived here rather than quoted, and
/// the derivation is the point:
///
/// A latent corruption appears in a block at some instant. The scrubber
/// revisits that block once every <c>T</c> hours (its sweep period). Errors
/// affecting one block arrive as a Poisson process of rate
/// <c>lambda = 1/MTTF</c>, so the gap <c>G</c> between the corruption and the
/// next corruption of the same block is exponential with mean <c>MTTF</c>.
///
/// The corruption is caught exactly when a scrub visit lands before that next
/// corruption masks it - i.e. when <c>G &gt; T</c>. For an exponential gap
/// <c>P(G &gt; T) = exp(-lambda*T) = exp(-T/MTTF)</c>.
///
/// The limits are the sanity check: <c>T -&gt; 0</c> gives 1 (a scrub right
/// after the fault always catches it) and <c>T -&gt; infinity</c> gives 0
/// (nothing is ever caught before the next fault), so the quantity is
/// monotone increasing in sweep frequency, which is exactly the pressure §45.7
/// applies.
/// </remarks>
public static class CatchProbability
{
    /// <summary>
    /// Probability that a corruption in one block is detected before the next
    /// corruption of that same block masks it.
    /// </summary>
    /// <param name="sweepPeriodHours">
    /// Hours between two consecutive visits of the scrubber to one block.
    /// </param>
    /// <param name="blockMtbfHours">Mean time between failures for one block.</param>
    public static double OfSweepPeriod(double sweepPeriodHours, double blockMtbfHours)
    {
        if (double.IsNaN(sweepPeriodHours) || sweepPeriodHours < 0)
            throw new ArgumentOutOfRangeException(nameof(sweepPeriodHours), "the sweep period cannot be negative or NaN");
        if (double.IsNaN(blockMtbfHours) || blockMtbfHours <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockMtbfHours), "the MTBF must be positive");

        // A block that never fails is never masked, so it is always caught.
        if (double.IsPositiveInfinity(blockMtbfHours)) return 1.0;

        // exp(-T/MTTF) written this way so T = 0 gives exactly 1 and a huge
        // ratio underflows to 0 rather than throwing.
        return Math.Exp(-sweepPeriodHours / blockMtbfHours);
    }

    /// <summary>
    /// Hours between two visits to the same block, for a schedule that fires
    /// every <paramref name="intervalHours"/> and covers
    /// <paramref name="batchSize"/> of <paramref name="totalBlocks"/> blocks per pass.
    /// </summary>
    /// <remarks>
    /// Batch size does enter, through the number of passes: at a fixed interval,
    /// a smaller batch needs more passes to sweep the disk, so each individual
    /// block waits proportionally longer between visits. Covering the whole
    /// disk in one pass gives the shortest period and the highest catch
    /// probability - but that is exactly the background I/O §45.8 says "can be
    /// tuned", so the batch size is the knob that trades coverage latency
    /// against how much work one pass does.
    /// </remarks>
    public static double SweepPeriodHours(double intervalHours, int totalBlocks, int batchSize)
    {
        if (totalBlocks < 1) throw new ArgumentOutOfRangeException(nameof(totalBlocks), "a disk needs at least one block");
        if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize), "a pass must cover at least one block");
        if (batchSize > totalBlocks) throw new ArgumentOutOfRangeException(nameof(batchSize), "a pass cannot cover more blocks than the disk has");
        if (double.IsNaN(intervalHours) || intervalHours <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalHours), "the interval must be positive");

        // Passes needed to sweep the whole disk, times the interval between passes.
        double passes = Math.Ceiling(totalBlocks / (double)batchSize);
        return intervalHours * passes;
    }
}

/// <summary>
/// Space and time cost of checksumming (OSEP §45.8).
/// </summary>
/// <remarks>
/// §45.8, verbatim: "each stored checksum takes up room on the disk... A
/// typical ratio might be an 8-byte checksum per 4 KB data block, for a 0.19%
/// on-disk space overhead."
/// </remarks>
public static class ChecksumOverhead
{
    /// <summary>§45.8's worked example: 8 bytes of checksum per 4 KB block.</summary>
    public const int TypicalChecksumBytes = 8;
    public const int TypicalBlockBytes = 4096;

    /// <summary>On-disk space overhead as a percentage of the block size.</summary>
    public static double SpacePercent(int checksumBytes, int dataBlockBytes)
    {
        if (checksumBytes < 0) throw new ArgumentOutOfRangeException(nameof(checksumBytes));
        if (dataBlockBytes < 1) throw new ArgumentOutOfRangeException(nameof(dataBlockBytes));
        return 100.0 * checksumBytes / dataBlockBytes;
    }
}

/// <summary>
/// A scrubbing schedule over an <see cref="IntegrityStore"/> (OSEP §45.7).
/// </summary>
/// <remarks>
/// M27's <c>IntegrityStore.Scrub()</c> is one-shot: it walks every block and
/// returns. §45.7 describes something else - a system that "periodically
/// read[s] through every block" over time, with the scan rate set by policy and
/// not by the size of the disk. This class adds the missing half: a cursor that
/// survives between passes, a batch size that bounds how much work one pass
/// does, and a schedule that repeats it.
///
/// The schedule runs on the thread pool and <see cref="Stop"/> waits for the
/// worker to finish, so a stopped scrubber is a scrubber that is not running.
/// </remarks>
public sealed class Scrubber
{
    private readonly IntegrityStore _store;
    private readonly int _batchSize;

    // _gate protects the sweep state (cursor, counters). _lifecycle separately
    // serialises Schedule/Stop as a whole. Keeping them apart matters: Stop
    // must hold _lifecycle while joining, and holding _gate across the join
    // would block the very worker it is waiting for.
    private readonly object _gate = new();
    private readonly object _lifecycle = new();

    private int _cursor;
    private int _passesCompleted;
    private long _blocksScrubbed;
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public Scrubber(IntegrityStore store, int batchSize)
    {
        if (store is null) throw new ArgumentNullException(nameof(store));
        if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize), "a pass must cover at least one block");

        _store = store;
        _batchSize = Math.Min(batchSize, store.Blocks);
    }

    /// <summary>Blocks covered by one pass, capped at the disk size.</summary>
    public int BatchSize => _batchSize;

    /// <summary>Where the next pass will start.</summary>
    public int Cursor { get { lock (_gate) return _cursor; } }

    /// <summary>Whole passes completed since construction, scheduled or manual.</summary>
    public int PassesCompleted { get { lock (_gate) return _passesCompleted; } }

    /// <summary>Blocks seen in total, counting repeats across passes.</summary>
    public long BlocksScrubbed { get { lock (_gate) return _blocksScrubbed; } }

    /// <summary>
    /// True while a scheduled worker is inside its loop. Derived from the task
    /// itself rather than from the request flag, so a <see cref="Stop"/> that
    /// only signalled cancellation without waiting would still read as running.
    /// </summary>
    public bool IsRunning
    {
        get { lock (_gate) return _worker is { IsCompleted: false }; }
    }

    /// <summary>
    /// Scrub the next <see cref="BatchSize"/> blocks, continuing from where the
    /// previous pass stopped and wrapping at the end of the disk.
    /// </summary>
    public ScrubReport ScrubBatch()
    {
        var report = new ScrubReport();
        lock (_gate)
        {
            int covered = 0;
            while (covered < _batchSize)
            {
                int b = _cursor;
                _cursor = (_cursor + 1) % _store.Blocks;

                var failures = _store.Verify(_store.GetBlock(b));
                if (failures.Count == 0) report.OkBlocks.Add(b);
                else report.BadBlocks.Add((b, failures));

                covered++;
                _blocksScrubbed++;
            }
            _passesCompleted++;
        }
        return report;
    }

    /// <summary>
    /// Start scrubbing <see cref="BatchSize"/> blocks every
    /// <paramref name="interval"/>, pausing <paramref name="throttle"/> between
    /// batches so the background read does not monopolise the device.
    /// </summary>
    public void Schedule(TimeSpan interval, TimeSpan throttle)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), "the interval must be positive");
        if (throttle < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(throttle), "the throttle cannot be negative");

        // WaitOne takes whole milliseconds and rejects anything over int.MaxValue
        // of them, so a sub-millisecond interval would become a zero-length wait
        // and spin. Reject both here rather than faulting the worker later, where
        // only the unobserved task would know.
        long intervalMs = (long)interval.TotalMilliseconds;
        long throttleMs = (long)throttle.TotalMilliseconds;
        if (intervalMs < 1) throw new ArgumentOutOfRangeException(nameof(interval), "the interval must be at least one millisecond");
        if (intervalMs > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(interval), "the interval must be at most int.MaxValue milliseconds");
        if (throttleMs > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(throttle), "the throttle must be at most int.MaxValue milliseconds");

        // The whole stop-start-publish sequence is atomic with respect to other
        // Schedule and Stop calls. Without this, two Schedules could both finish
        // Stop before either published, and the first worker would be orphaned -
        // still scrubbing, but unreachable, so no later Stop could reach it.
        lock (_lifecycle)
        {
            StopCore();

            var cts = new CancellationTokenSource();
            var worker = Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try { cts.Token.WaitHandle.WaitOne((int)intervalMs); }
                    catch (ObjectDisposedException) { return; }
                    if (cts.IsCancellationRequested) return;

                    try { ScrubBatch(); }
                    catch (ObjectDisposedException) { return; }

                    if (throttleMs > 0)
                    {
                        try { cts.Token.WaitHandle.WaitOne((int)throttleMs); }
                        catch (ObjectDisposedException) { return; }
                    }
                }
            });

            lock (_gate)
            {
                _cts = cts;
                _worker = worker;
            }
        }
    }

    /// <summary>
    /// Stop the schedule and wait for the worker to leave its loop, so the
    /// caller can rely on no further background reads happening.
    /// </summary>
    public void Stop()
    {
        lock (_lifecycle) StopCore();
    }

    /// <summary>
    /// Cancel and join the current worker. Callers must hold
    /// <see cref="_lifecycle"/>; the worker itself only needs
    /// <see cref="_gate"/>, which is never held across the join.
    /// </summary>
    private void StopCore()
    {
        CancellationTokenSource? cts;
        Task? worker;
        lock (_gate)
        {
            cts = _cts;
            worker = _worker;
            // Leave _worker set until the join succeeds, so IsRunning keeps
            // reporting the truth while the loop is still winding down.
            if (cts is null) return;
            _cts = null;
        }

        cts.Cancel();
        bool joined = false;
        try { joined = worker?.Wait(TimeSpan.FromSeconds(5)) ?? true; }
        catch (AggregateException) { joined = true; }   // cancelled out of the loop

        if (!joined)
        {
            // The worker is still inside ScrubBatch, blocked behind a manual
            // call holding _gate. Releasing its CTS now would let the next
            // access throw ObjectDisposedException inside the loop, and
            // reporting success would be a lie. Keep both, so a later Stop can
            // finish the job.
            lock (_gate) { _cts = cts; }
            throw new TimeoutException(
                "the scrubber did not stop within 5s; it is still running and the schedule was left in place");
        }

        lock (_gate)
        {
            if (ReferenceEquals(_worker, worker)) _worker = null;
        }
        cts.Dispose();
    }

    /// <summary>Format a comparison of candidate schedules for the route.</summary>
    public static string FormatScheduleComparison(
        int totalBlocks,
        double blockMtbfHours,
        (string Name, double IntervalHours, int BatchSize)[] candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Scrubbing schedule (M33 / OSEP §45.7) ===");
        sb.AppendLine($"{totalBlocks} blocks, block MTBF {blockMtbfHours:N0} h");
        sb.AppendLine();
        sb.AppendLine("schedule          | interval | batch | sweep period | P(catch before masked)");
        sb.AppendLine("------------------|----------|-------|--------------|------------------------");
        foreach (var (name, intervalHours, batchSize) in candidates)
        {
            double sweep = CatchProbability.SweepPeriodHours(intervalHours, totalBlocks, batchSize);
            double p = CatchProbability.OfSweepPeriod(sweep, blockMtbfHours);
            sb.AppendLine($"{name,-17} | {intervalHours,8:F1}h | {batchSize,5} | {sweep,12:F1}h | {p,22:P6}");
        }
        return sb.ToString();
    }
}