using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>Outcome of one LFS-extension demo.</summary>
public sealed record LfsExtensionResult(
    string Scenario,
    string Headline,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Notes);

/// <summary>
/// The two M31 scenarios: §43.3 segment sizing and §43.12 two-CR recovery.
/// </summary>
public static class LfsExtensionsDemos
{
    /// <summary>OSEP §43.3's own worked example.</summary>
    private const double PeakBandwidthMbPerSecond = 100;
    private const double PositionTimeSeconds = 0.010;

    public static LfsExtensionResult Run(string scenario)
    {
        return scenario switch
        {
            "segment-size-sweep" => RunSegmentSizeSweep(),
            "cost-model" => RunCostModel(),
            "dual-cr-recovery" => RunDualCrRecovery(),
            "cr-alternation" => RunCrAlternation(),
            "cr-crash-during-write" => RunCrCrashDuringWrite(),
            _ => throw new ArgumentException(
                $"unknown lfs extension scenario '{scenario}' (use segment-size-sweep, " +
                "cost-model, dual-cr-recovery, cr-alternation, cr-crash-during-write)",
                nameof(scenario)),
        };
    }

    public static string Format(LfsExtensionResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.Headline);
        sb.AppendLine();
        foreach (var line in result.Lines) sb.AppendLine(line);
        if (result.Notes.Count > 0)
        {
            sb.AppendLine();
            foreach (var note in result.Notes) sb.AppendLine(note);
        }
        return sb.ToString();
    }

    /// <summary>
    /// §43.3's cost model, checked against the chapter's own worked example and
    /// against the effective-rate formula it inverts.
    /// </summary>
    private static LfsExtensionResult RunCostModel()
    {
        double optimal90 = SegmentSizer.OptimalBytes(PeakBandwidthMbPerSecond, PositionTimeSeconds, 0.9);
        double optimal95 = SegmentSizer.OptimalBytes(PeakBandwidthMbPerSecond, PositionTimeSeconds, 0.95);
        double optimal99 = SegmentSizer.OptimalBytes(PeakBandwidthMbPerSecond, PositionTimeSeconds, 0.99);

        var lines = new List<string>
        {
            $"disk: R_peak = {PeakBandwidthMbPerSecond} MB/s, T_position = {PositionTimeSeconds * 1000} ms",
            "",
            $"F=90%:  D = {optimal90 / 1_000_000,8:0.00} MB   achieved {SegmentSizer.EffectiveBandwidthFraction(optimal90, PeakBandwidthMbPerSecond, PositionTimeSeconds):P2} of peak",
            $"F=95%:  D = {optimal95 / 1_000_000,8:0.00} MB   achieved {SegmentSizer.EffectiveBandwidthFraction(optimal95, PeakBandwidthMbPerSecond, PositionTimeSeconds):P2} of peak",
            $"F=99%:  D = {optimal99 / 1_000_000,8:0.00} MB   achieved {SegmentSizer.EffectiveBandwidthFraction(optimal99, PeakBandwidthMbPerSecond, PositionTimeSeconds):P2} of peak",
            "",
            "why it grows so fast: F/(1-F) is 9x at F=0.9, 19x at 0.95, 99x at 0.99.",
            "chasing the last few percent of bandwidth costs a multiple of the",
            "memory you must hold before flushing.",
        };

        var notes = new List<string>
        {
            "OSEP §43.3 equation 43.6: D = (F / (1 - F)) x R_peak x T_position. The chapter's",
            "worked example is the F=0.9 row: 'a disk with a positioning time of 10 milliseconds",
            "and peak transfer rate of 100 MB/s ... D = 0.9/0.1 x 100 MB/s x 0.01 seconds = 9 MB'.",
            "",
            "This sizes the write path only. It says nothing about how much RAM that costs, nor",
            "about how long unpersisted updates sit in memory (and so at risk) before a flush.",
        };

        return new LfsExtensionResult("cost-model",
            "=== LFS segment size: the §43.3 cost model (M31) ===", lines, notes);
    }

    /// <summary>
    /// Sweeps segment sizes and reports the write-amplification model, whose
    /// minimum is the size worth using. The point is the U-shape: positioning
    /// cost falls with size, cleaning cost rises.
    /// </summary>
    private static LfsExtensionResult RunSegmentSizeSweep()
    {
        const double blockBytes = 4096;
        const double liveRatio = 0.4;

        int[] sizes = { 4, 8, 16, 32, 64, 128, 256 };
        var lines = new List<string>
        {
            $"disk: R_peak = {PeakBandwidthMbPerSecond} MB/s, T_position = {PositionTimeSeconds * 1000} ms, block = {blockBytes} bytes",
            $"cleaner sees {liveRatio:P0} live blocks per segment (each live block is rewritten once)",
            "",
            $"{"blocks/seg",-12} {"segment MB",-12} {"effective BW",-14} {"write amp",-12}",
        };

        double bestAmp = double.MaxValue;
        int bestBlocks = 0;
        foreach (int blocks in sizes)
        {
            double segmentBytes = blocks * blockBytes;
            double amp = SegmentSizer.WriteAmplification(
                blocks, liveRatio, blockBytes, PositionTimeSeconds, PeakBandwidthMbPerSecond);
            if (amp < bestAmp)
            {
                bestAmp = amp;
                bestBlocks = blocks;
            }
            lines.Add(string.Format(
                "{0,-12} {1,-12:0.###} {2,-14:P1} {3,7:0.00}x",
                blocks,
                segmentBytes / SegmentSizer.BytesPerMb,
                SegmentSizer.EffectiveBandwidthFraction(segmentBytes, PeakBandwidthMbPerSecond, PositionTimeSeconds),
                amp));
        }

        double optimalBytes = SegmentSizer.OptimalBytes(PeakBandwidthMbPerSecond, PositionTimeSeconds, 0.9);
        double optimalBlocks = optimalBytes / blockBytes;

        lines.Add("");
        lines.Add($"best of the swept sizes: {bestAmp:0.00}x at {bestBlocks} blocks/segment");
        lines.Add($"§43.3 size for 90% of peak: {optimalBytes / SegmentSizer.BytesPerMb:0.###} MB " +
                  $"= {optimalBlocks:0.#} blocks/segment");

        var notes = new List<string>
        {
            "The write path improves monotonically with segment size. The model is",
            "  A = 1/u + T / (T + n*B/1e6R)",
            "where n = blocks per segment, B = block bytes, T = T_position, R = R_peak and",
            "u = live ratio. dA/dn = -T*(B/1e6R) / (T + n*B/1e6R)^2 < 0 for positive T, B, R,",
            "which is why every row above falls.",
            "",
            "The cleaner's (1-u)/u term is independent of segment size by construction: the",
            "cleaner reads M segments and writes N<M new ones, and the rewrite-to-freed ratio",
            "depends on how dirty the segment is, not on how many blocks it held.",
            "",
            "So there is no total-cost optimum in segment size under this model. Segment size is",
            "still not free to pick - bigger segments hold more unpersisted updates in memory before",
            "a flush, so a crash loses more - but §43.3 sizes the write path, not that trade-off.",
        };

        return new LfsExtensionResult("segment-size-sweep",
            "=== LFS segment size sweep (M31 / OSEP §43.3) ===", lines, notes);
    }

    /// <summary>
    /// §43.12: writes alternate CR0/CR1; recovery takes the most recent CR with
    /// consistent header/trailer timestamps.
    /// </summary>
    private static LfsExtensionResult RunDualCrRecovery()
    {
        var cr = new DualCheckpointRegion();
        cr.Write();  // CR0 ts=1
        cr.Write();  // CR1 ts=2
        cr.Write();  // CR0 ts=3
        var (index, image, reason) = cr.Recover();

        var lines = new List<string>
        {
            "three checkpoint writes, alternating:",
            "  write 1 -> CR0 (ts=1)",
            "  write 2 -> CR1 (ts=2)",
            "  write 3 -> CR0 (ts=3)",
            "",
            $"CR0: {(cr.Cr0 is null ? "empty" : $"header={cr.Cr0.HeaderTimestamp} trailer={cr.Cr0.TrailerTimestamp} consistent={cr.Cr0.IsConsistent}")}",
            $"CR1: {(cr.Cr1 is null ? "empty" : $"header={cr.Cr1.HeaderTimestamp} trailer={cr.Cr1.TrailerTimestamp} consistent={cr.Cr1.IsConsistent}")}",
            "",
            $"recovered from CR{index} (ts={image.HeaderTimestamp})  {reason}",
        };

        var notes = new List<string>
        {
            "OSEP §43.12: 'LFS will always choose to use the most recent CR that has consistent",
            "timestamps'. Alternating means write 3 never touched CR1, so a crash during write 3",
            "would still leave CR1 (ts=2) mountable.",
        };

        return new LfsExtensionResult("dual-cr-recovery",
            "=== Two checkpoint regions: recovery picks the newest consistent CR (M31 / OSEP §43.12) ===",
            lines, notes);
    }

    /// <summary>Shows the alternation pattern over N writes.</summary>
    private static LfsExtensionResult RunCrAlternation()
    {
        const int writes = 10;
        var cr = new DualCheckpointRegion();
        for (int i = 0; i < writes; i++) cr.Write();

        var newest = cr.Cr0?.HeaderTimestamp >= cr.Cr1?.HeaderTimestamp ? 0 : 1;
        var lines = new List<string>
        {
            $"{writes} checkpoint writes -> {string.Join(", ", cr.WriteLog.Select(w => $"CR{w.Cr}"))}",
            $"next write targets CR{cr.ActiveIndex}",
            "",
            $"CR0 last ts: {cr.Cr0?.HeaderTimestamp.ToString() ?? "never"}",
            $"CR1 last ts: {cr.Cr1?.HeaderTimestamp.ToString() ?? "never"}",
            "",
            $"{writes} writes is an even count, so each CR holds {(writes + 1) / 2} of them and",
            $"CR{newest} is one timestamp newer than the other. Recovery would mount CR{newest}.",
            "A tie is impossible with a monotonic clock: each write stamps a distinct timestamp,",
            "so 'most recent consistent CR' is always unambiguous once the CRs are told apart.",
        };

        var notes = new List<string>
        {
            "OSEP §43.12 keeps 'two CRs, one at either end of the disk, and writes to them",
            "alternately'. The purpose is not redundancy of data but redundancy of *validity*:",
            "whichever CR is not being written right now is intact and mountable.",
        };

        return new LfsExtensionResult("cr-alternation",
            "=== Two checkpoint regions: alternation (M31 / OSEP §43.12) ===", lines, notes);
    }

    /// <summary>
    /// The case the header/trailer timestamps exist for: a crash between the
    /// body and the trailer leaves an inconsistent CR, and recovery must fall
    /// back to the other one.
    /// </summary>
    private static LfsExtensionResult RunCrCrashDuringWrite()
    {
        var cr = new DualCheckpointRegion();
        cr.Write();                        // CR0 ts=1, consistent
        cr.Write();                        // CR1 ts=2, consistent
        // Crash partway through the CR0 update: header written, body written,
        // trailer not. A distinct trailer timestamp is exactly how that is
        // represented here.
        cr.Write(bodyTimestamp: 3, trailerTimestamp: -1);

        var (index, image, reason) = cr.Recover();

        var lines = new List<string>
        {
            "writes:",
            "  write 1 -> CR0 (ts=1, consistent)",
            "  write 2 -> CR1 (ts=2, consistent)",
            "  write 3 -> CR0 CRASHED (header=3, body=3, trailer=never written)",
            "",
            $"CR0: header={cr.Cr0!.HeaderTimestamp} trailer={cr.Cr0!.TrailerTimestamp} consistent={cr.Cr0!.IsConsistent}",
            $"CR1: header={cr.Cr1!.HeaderTimestamp} trailer={cr.Cr1!.TrailerTimestamp} consistent={cr.Cr1!.IsConsistent}",
            "",
            $"recovered from CR{index} (ts={image.HeaderTimestamp})  {reason}",
        };

        var notes = new List<string>
        {
            "OSEP §43.12: 'If the system crashes during a CR update, LFS can detect this by seeing an",
            "inconsistent pair of timestamps.' The trailer is written last, so a mismatch means the",
            "crash landed mid-update and that CR must not be trusted.",
            "",
            "With a single CR this is unrecoverable: the one anchor the file system has is half-written.",
            "Alternating is what makes CR1 still valid here.",
        };

        return new LfsExtensionResult("cr-crash-during-write",
            "=== Two checkpoint regions: crash mid-update (M31 / OSEP §43.12) ===", lines, notes);
    }
}