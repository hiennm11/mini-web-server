namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// LFS segment sizing (OSEP §43.3 "How Much To Buffer?").
///
/// OSEP §43.3: "Every time you write, you pay a fixed overhead of the
/// positioning cost. Thus, how much do you have to write in order to amortize
/// that cost? The more you write, the better (obviously), and the closer you
/// get to achieving peak bandwidth."
///
/// Starting from the chapter's own chain of equations — write time
/// (43.1), effective rate (43.2), set it to F x R_peak (43.3) and solve
/// (43.4-43.6) — the answer is equation 43.6:
///
///     D = (F / (1 - F)) x R_peak x T_position
///
/// OSEP's worked example: "a disk with a positioning time of 10 milliseconds
/// and peak transfer rate of 100 MB/s; assume we want an effective bandwidth
/// of 90% of peak (F = 0.9). In this case, D = 0.9/0.1 x 100 MB/s x 0.01
/// seconds = 9 MB."
/// </summary>
public static class SegmentSizer
{
    /// <summary>Bytes in one MB, matching OSEP's decimal (not binary) units.</summary>
    public const double BytesPerMb = 1_000_000.0;

    /// <summary>
    /// The minimum segment size, in bytes, that achieves
    /// <paramref name="fractionOfPeak"/> of
    /// <paramref name="peakBandwidthMbPerSecond"/>.
    /// </summary>
    /// <param name="peakBandwidthMbPerSecond">R_peak, MB/s.</param>
    /// <param name="positionTimeSeconds">T_position: seek + rotational latency.</param>
    /// <param name="fractionOfPeak">F, strictly between 0 and 1.</param>
    /// <remarks>
    /// Equation 43.6. Note the blow-up as F approaches 1: the chapter asks "how
    /// much is needed to reach 95% of peak? 99%?" and the answer jumps by roughly
    /// an order of magnitude each time, because F/(1-F) grows without bound.
    ///
    /// The product is evaluated as <c>(R_peak * T_position) * odds</c> rather than
    /// <c>odds * R_peak * T_position</c>. Left to associate, a huge bandwidth
    /// paired with a tiny seek time overflows on the intermediate
    /// <c>odds * R_peak</c> and returns Infinity for an answer that is finite —
    /// <c>(1e308, 1e-308, 0.9)</c> is 9 MB, not Infinity.
    /// </remarks>
    public static double OptimalBytes(
        double peakBandwidthMbPerSecond,
        double positionTimeSeconds,
        double fractionOfPeak)
    {
        if (!double.IsFinite(peakBandwidthMbPerSecond) || peakBandwidthMbPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(peakBandwidthMbPerSecond));
        if (!double.IsFinite(positionTimeSeconds) || positionTimeSeconds <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(positionTimeSeconds),
                "T_position must be positive: at zero the disk has no positioning overhead, "
                + "so every segment size reaches peak and equation 43.6 is not invertible");
        if (!double.IsFinite(fractionOfPeak) || fractionOfPeak <= 0 || fractionOfPeak >= 1)
            throw new ArgumentOutOfRangeException(
                nameof(fractionOfPeak), "F must be strictly between 0 and 1");

        double odds = fractionOfPeak / (1.0 - fractionOfPeak);
        double seekCostMb = peakBandwidthMbPerSecond * positionTimeSeconds;

        return seekCostMb * odds * BytesPerMb;
    }

    /// <summary>
    /// The fraction of peak bandwidth actually achieved when writing a segment
    /// of <paramref name="segmentBytes"/>. This is equation 43.2,
    /// <c>R_effective = D / (T_position + D/R_peak)</c>, and it is the check
    /// that <see cref="OptimalBytes"/> inverts.
    /// </summary>
    public static double EffectiveBandwidthFraction(
        double segmentBytes,
        double peakBandwidthMbPerSecond,
        double positionTimeSeconds)
    {
        if (segmentBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(segmentBytes));

        double transferSeconds = segmentBytes / BytesPerMb / peakBandwidthMbPerSecond;
        double totalSeconds = positionTimeSeconds + transferSeconds;
        if (totalSeconds == 0) return 1.0;

        double effectiveMbPerSecond = (segmentBytes / BytesPerMb) / totalSeconds;
        return effectiveMbPerSecond / peakBandwidthMbPerSecond;
    }

    /// <summary>
    /// The whole-segment write cost as a multiple of the data actually stored.
    /// </summary>
    /// <param name="segmentBlocks">Blocks per segment, including the summary block.</param>
    /// <param name="liveBlockRatio">
    /// Fraction of blocks in a segment that are still live when the cleaner
    /// reaches it. Cleaning <c>M</c> segments into <c>N &lt; M</c> costs extra
    /// writes proportional to 1/ratio (§43.9).
    /// </param>
    /// <param name="blockBytes">Bytes per block.</param>
    /// <param name="positionTimeSeconds">T_position.</param>
    /// <param name="peakBandwidthMbPerSecond">R_peak.</param>
    /// <param name="liveBlockRatio">
    /// Fraction of blocks in a segment still live when the cleaner reaches it.
    /// </param>
    /// <remarks>
    /// <para><b>This is a write-path cost, not a total LFS cost.</b> It is the
    /// multiplier applied to the data you asked LFS to store, covering:</para>
    /// <list type="bullet">
    /// <item>the positioning overhead on the write path, which falls as the
    /// segment grows — this is what §43.3's equation 43.6 sizes away;</item>
    /// <item>the cleaner's rewrite of dead blocks, which is
    /// <c>(1 - liveRatio) / liveRatio</c> and is <b>independent of segment
    /// size</b>.</item>
    /// </list>
    ///
    /// <para><b>Correction worth recording.</b> A first version of this method
    /// tried to make the total a U-shape, on the assumption that larger segments
    /// are worse because they accumulate more garbage. That assumption is
    /// wrong, and the arithmetic says so: §43.9's cleaner reads M segments and
    /// writes N &lt; M new ones, so the reclaim cost per unit of live data is
    /// <c>(T_position/R_peak + 1 + liveRatio) / liveRatio</c> — the segment size
    /// cancels out. Both terms therefore fall with segment size, and the total is
    /// monotonically decreasing. A "minimum in the middle of the range"
    /// assertion passed only because it was written against the broken model.</para>
    ///
    /// <para>Segment size is still not free to choose, just not for this
    /// reason: bigger segments hold more unpersisted updates in memory before a
    /// flush, so a crash loses more, and the memory cost itself grows. §43.3
    /// sizes the write path; it does not claim a total-cost optimum.</para>
    /// </remarks>
    public static double WriteAmplification(
        int segmentBlocks,
        double liveBlockRatio,
        double blockBytes,
        double positionTimeSeconds,
        double peakBandwidthMbPerSecond)
    {
        if (segmentBlocks <= 0)
            throw new ArgumentOutOfRangeException(nameof(segmentBlocks));
        if (liveBlockRatio <= 0 || liveBlockRatio > 1)
            throw new ArgumentOutOfRangeException(
                nameof(liveBlockRatio), "live block ratio must be in (0, 1]");

        double segmentBytes = segmentBlocks * blockBytes;

        // Positioning on the write path, amortised over the segment.
        // effective = data / (positionTime + data/peak), so the fraction of the
        // write that is overhead is positionTime / (positionTime + transfer).
        double transferSeconds = segmentBytes / BytesPerMb / peakBandwidthMbPerSecond;
        double positioningOverhead = positionTimeSeconds / (positionTimeSeconds + transferSeconds);

        // Cleaner's rewrite of dead blocks. Independent of segment size: see the
        // remarks above.
        double cleanerRewrite = (1.0 - liveBlockRatio) / liveBlockRatio;

        return 1.0 + positioningOverhead + cleanerRewrite;
    }

    /// <summary>
    /// The write-path term alone, with no cleaning. This is the quantity §43.3
    /// actually sizes, and it is 1 + positioningOverhead - see
    /// <see cref="WriteAmplification"/>.
    /// </summary>
    public static double WritePathOverhead(
        int segmentBlocks,
        double blockBytes,
        double positionTimeSeconds,
        double peakBandwidthMbPerSecond)
    {
        double full = WriteAmplification(
            segmentBlocks, liveBlockRatio: 1.0, blockBytes,
            positionTimeSeconds, peakBandwidthMbPerSecond);
        return full;
    }
}

/// <summary>
/// Two checkpoint regions written alternately (OSEP §43.12 "Crash Recovery And
/// The Log").
///
/// OSEP §43.12: "To ensure that the CR update happens atomically, LFS actually
/// keeps two CRs, one at either end of the disk, and writes to them
/// alternately. LFS also implements a careful protocol when updating the CR
/// with the latest pointers to the inode map and other information;
/// specifically, it first writes out a header (with timestamp), then the body
/// of the CR, and then finally one last block (also with a timestamp). If the
/// system crashes during a CR update, LFS can detect this by seeing an
/// inconsistent pair of timestamps. LFS will always choose to use the most
/// recent CR that has consistent timestamps, and thus consistent update of the
/// CR is achieved."
///
/// <para><b>Why two.</b> A single CR is updated in place, so a crash mid-write
/// destroys the only anchor the file system has. Worse, a crash while the
/// writer is reclaiming segments cannot be recovered at all. Alternating keeps
/// the previous consistent CR intact while the next one is being written, which
/// is what makes the update atomic from the reader's side.</para>
///
/// <para>Timestamps are a monotonic counter, not wall-clock: the simulator must
/// make the *consistency* property testable, and a clock would make the tests
/// depend on timing.</para>
/// </summary>
public sealed class DualCheckpointRegion
{
    /// <summary>Header/trailer/body of one checkpoint region on disk.</summary>
    public sealed record CrImage(long HeaderTimestamp, long BodyTimestamp, long TrailerTimestamp)
    {
        /// <summary>
        /// A CR is trustworthy only if the header and trailer agree: the trailer
        /// is written last, so a mismatch means the crash landed mid-update.
        /// </summary>
        public bool IsConsistent => HeaderTimestamp == TrailerTimestamp;
    }

    private readonly CrImage?[] _crs = new CrImage?[2];
    private long _timestamp;

    /// <summary>Which CR the next write targets. Toggles on every write.</summary>
    public int ActiveIndex { get; private set; }

    /// <summary>Every write that has happened, for the smoke trace.</summary>
    public List<(int Cr, long Timestamp, bool Consistent)> WriteLog { get; } = new();

    /// <summary>CR 0 as last written, or null if never written.</summary>
    public CrImage? Cr0 => _crs[0];

    /// <summary>CR 1 as last written, or null if never written.</summary>
    public CrImage? Cr1 => _crs[1];

    /// <summary>
    /// Writes header, body, then trailer to <see cref="ActiveIndex"/>, then
    /// toggles — the §43.12 protocol.
    /// </summary>
    /// <param name="bodyTimestamp">
    /// The timestamp stamped into the body. Only meaningful for representing a
    /// crash; pass null for a normal write.
    /// </param>
    /// <param name="trailerTimestamp">
    /// The timestamp stamped into the trailer, written last. Only meaningful for
    /// representing a crash before the trailer landed.
    /// </param>
    public void Write(long? bodyTimestamp = null, long? trailerTimestamp = null)
    {
        long ts = ++_timestamp;
        long body = bodyTimestamp ?? ts;
        long trailer = trailerTimestamp ?? ts;

// A crash can leave the header written and the trailer unwritten, but it
        // cannot leave a *new* header paired with a *previous* write's trailer:
        // the trailer is written after the body and always carries its own
        // write's timestamp. Allowing that would publish a torn body behind a
        // "consistent" header/trailer pair, and recovery would mount it happily.
        // So the impossible state is refused at the producer rather than papered
        // over with an extra check in the recovery rule.
        if (_crs[ActiveIndex] is { } previous && trailer == previous.HeaderTimestamp)
        {
            throw new ArgumentException(
                $"CR{ActiveIndex} cannot carry a fresh header (ts={ts}) with the previous "
                + $"write's trailer (ts={trailer}): the trailer is written last and always "
                + "matches its own header",
                nameof(trailerTimestamp));
        }

        var image = new CrImage(ts, body, trailer);
        _crs[ActiveIndex] = image;
        WriteLog.Add((ActiveIndex, ts, image.IsConsistent));

        // Alternate, so the previous consistent CR is never the one being
        // overwritten.
        ActiveIndex ^= 1;
    }

    /// <summary>
    /// Chooses the CR recovery should mount: "the most recent CR that has
    /// consistent timestamps" (§43.12).
    /// </summary>
    public (int Index, CrImage Image, string Reason) Recover()
    {
        var consistent = new List<int>();
        for (int i = 0; i < _crs.Length; i++)
        {
            if (_crs[i] is { IsConsistent: true }) consistent.Add(i);
        }

        if (consistent.Count == 0)
        {
            var written = _crs.Select((c, i) => (c, i))
                              .Where(x => x.c is not null)
                              .Select(x => $"CR{x.i} torn ({_Describe(x.i)})")
                              .ToList();
            string detail = written.Count == 0
                ? "neither CR has ever been written"
                : $"no consistent CR among: {(written.Count == 0 ? "nothing written" : string.Join("; ", written))}"
                  + (_crs.Any(c => c is null) ? "; the other was never written" : "");
            throw new InvalidOperationException(
                $"no consistent checkpoint region - the file system cannot mount. {detail}");
        }

        // Most recent consistent CR wins.
        int best = consistent[0];
        foreach (int i in consistent)
        {
            if (_crs[i]!.HeaderTimestamp > _crs[best]!.HeaderTimestamp) best = i;
        }

        var rejected = consistent.Count == 2
            ? $" (CR{1 - best} also consistent but older, ts={_crs[1 - best]!.HeaderTimestamp})"
            : "";
        var crashed = _crs[1 - best] is { IsConsistent: false }
            ? $" (CR{1 - best} rejected: {_Describe(1 - best)})"
            : rejected;

        return (best, _crs[best]!, crashed.Trim());
    }

    private string _Describe(int i)
    {
        return _crs[i] is null
            ? "never written"
            : $"header ts={_crs[i]!.HeaderTimestamp} != trailer ts={_crs[i]!.TrailerTimestamp}";
    }
}