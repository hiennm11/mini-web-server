namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// OSEP §37.1: a drive is an array of sectors, and a request names one.
/// </summary>
public readonly record struct DiskRequest(long Block, int Bytes = 512)
{
    public override string ToString() => $"{Block}({Bytes}B)";
}

/// <summary>One request's cost, split the way equation 37.1 splits I/O time.</summary>
public readonly record struct DiskTiming(
    long Block, double SeekMs, double RotationMs, double TransferMs)
{
    /// <summary>Equation 37.1: T_I/O = T_seek + T_rotation + T_transfer.</summary>
    public double TotalMs => SeekMs + RotationMs + TransferMs;
}

/// <summary>
/// OSEP §37.2-§37.4: drive geometry and the I/O-time arithmetic of equation 37.1.
/// Slice 37.1.
/// </summary>
/// <remarks>
/// <para>
/// The chapter builds a disk up one track at a time — 12 sectors on one track
/// (§37.3), then three tracks (figure 37.3) — and the geometry is what makes
/// addressing work: a block number decomposes into a track and a sector within it.
/// </para>
/// <para>
/// Two datasheet drives from figure 37.5 are included, because §37.4's worked
/// numbers for both are printed and can therefore be checked.
/// </para>
/// </remarks>
public sealed class DriveGeometry
{
    /// <summary>
    /// §37.4's ASIDE, equations 37.4-37.8. The chapter integrates
    /// <c>|x - y|</c> continuously over [0,N]^2 and gets N^3/3, so it reports the
    /// average seek distance as **one third** of the full distance. That is the
    /// limit, not the identity: a disk with finitely many tracks has exact mean
    /// <c>(N^2 - 1) / (3N)</c>, which is N/3 minus <c>1/(3N)</c>. At 1,000 tracks
    /// the gap is 0.0003 of a track — negligible, which is why the rule of thumb
    /// survives — but it is a real difference, and the chapter's own discrete
    /// summation (eq. 37.4) would produce it if evaluated rather than integrated.
    /// </summary>
    public const double AvgSeekFraction = 1.0 / 3.0;

    /// <summary>
    /// The exact discrete mean seek distance over all N^2 ordered track pairs:
    /// <c>(N^2 - 1) / (3N)</c>. Equals <see cref="AvgSeekFraction"/> times the track
    /// count only in the limit.
    /// </summary>
    public static double AvgSeekDistanceExact(int tracks) =>
        tracks >= 1 ? (tracks * (double)tracks - 1.0) / (3.0 * tracks) : 0.0;

    /// <summary>The chapter's N/3, which is the limit of <see cref="AvgSeekDistanceExact"/>.</summary>
    public static double AvgSeekDistance(int tracks) => tracks * AvgSeekFraction;

    /// <summary>§37.1: "a large number of sectors (512-byte blocks)".</summary>
    public const int SectorBytes = 512;

    /// <summary>§37.5's scheduling window example, and the number the chapter quotes.</summary>
    public const int TransferBytes = 4096;

    public required int TracksPerSurface { get; init; }
    public required int SectorsPerTrack { get; init; }
    public required int Surfaces { get; init; }
    public required int Rpm { get; init; }

    /// <summary>§37.2: the datasheet's average seek. Distinct from a full seek.</summary>
    public required double AvgSeekMs { get; init; }

    /// <summary>§37.2: "the settling time is often quite significant, e.g., 0.5 to 2 ms".</summary>
    public double SettleMs { get; init; } = 1.0;

    /// <summary>§37.2: peak transfer rate in MB/s, the datasheet's "Max Transfer".</summary>
    public required double TransferMBps { get; init; }

    /// <summary>Figure 37.5's capacity, for the record.</summary>
    public required string Name { get; init; }

    public long TotalSectors => (long)TracksPerSurface * SectorsPerTrack * Surfaces;

    /// <summary>The chapter's ASIDE conversion: 60,000 ms per minute ÷ RPM.</summary>
    public double RotationMs() => 60_000.0 / Rpm;

    /// <summary>
    /// §37.3: "On average, the disk will encounter a half rotation". A request's
    /// rotational delay is uniform over a full turn, so the expectation is R/2.
    /// </summary>
    public double AvgRotationMs() => RotationMs() / 2.0;

    /// <summary>Which track a block lives on. §37.3's outermost track holds sectors 0-11.</summary>
    public long TrackOf(long block) => block / SectorsPerTrack;

    /// <summary>Which sector within its track.</summary>
    public int SectorOf(long block) => (int)(block % SectorsPerTrack);

    /// <summary>The first block of a track.</summary>
    public long TrackStart(long track) => track * SectorsPerTrack;

    /// <summary>§37.5: seek distance is |x - y| in tracks.</summary>
    public double SeekDistance(long trackA, long trackB) => Math.Abs(trackA - trackB);

    /// <summary>
    /// §37.4's dimensional-analysis example, generalised. The transfer time is the
    /// size over the peak rate. The chapter's worked examples are in binary units —
    /// "512 KB * 1024/KB / 1 MB / 100 ms / 1 second / 1000 ms = 5 ms" at 100 MB/s —
    /// so KB and MB are 2^10 here and only seconds and milliseconds are 10^3.
    /// </summary>
    public double TransferMs(int bytes) => bytes / (TransferMBps * 1024.0 * 1024.0) * 1000.0;

    /// <summary>
    /// Equation 37.1 for one request. <paramref name="seekMs"/> is passed in because
    /// a full seek and an average seek are different costs and only the caller knows
    /// which one applies — §37.2 notes a full seek "would likely take two or three
    /// times longer" than the datasheet average.
    /// </summary>
    public DiskTiming Time(DiskRequest request, double seekMs, double? rotationMs = null) =>
        new(request.Block, seekMs,
            rotationMs ?? AvgRotationMs(),
            TransferMs(request.Bytes));

    /// <summary>
    /// Equation 37.2: rate = size / time. Units are binary, matching §37.4's own
    /// worked examples — the chapter writes KB as 1024 bytes ("512 KB * 1024/KB /
    /// 1 MB / 100 ms / 1 second / 1000 ms = 5 ms"), so a 4 KB read on a 6 ms
    /// Cheetah random I/O comes out at 4/6 = 0.66 MB/s, exactly as figure 37.6
    /// reports. Using decimal 10^6 here would give 0.68 and quietly disagree with
    /// the chapter by 2%.
    /// </summary>
    public double Rate(DiskRequest request, DiskTiming timing) =>
        (request.Bytes / (1024.0 * 1024.0)) / (timing.TotalMs / 1000.0);

    // --- The two drives of figure 37.5 -------------------------------------

    /// <summary>
    /// Cheetah 15K.5 [S09b]: 300 GB, 15,000 RPM, 4 ms average seek, 125 MB/s.
    /// §37.4 calls it the "high performance" drive.
    /// </summary>
    public static DriveGeometry Cheetah15K5 { get; } = new()
    {
        Name = "Cheetah 15K.5",
        Rpm = 15_000,
        AvgSeekMs = 4.0,
        TransferMBps = 125.0,
        // The chapter does not give a track count for these drives; the layout here
        // exists so TrackOf/SectorOf are usable, and is not part of §37.4's maths.
        TracksPerSurface = 10_000,
        SectorsPerTrack = 300,
        Surfaces = 4,
    };

    /// <summary>
    /// Barracuda [S09a]: 1 TB, 7,200 RPM, 9 ms average seek, 105 MB/s. §37.4 calls it
    /// the "capacity" drive.
    /// </summary>
    public static DriveGeometry BarracudaES2 { get; } = new()
    {
        Name = "Barracuda ES.2",
        Rpm = 7_200,
        AvgSeekMs = 9.0,
        TransferMBps = 105.0,
        TracksPerSurface = 20_000,
        SectorsPerTrack = 300,
        Surfaces = 4,
    };

    /// <summary>
    /// §37.3's teaching disk: one track, 12 sectors of 512 bytes, head over
    /// sector 6. Small enough that every figure in the chapter can be checked.
    /// </summary>
    public static DriveGeometry SingleTrack { get; } = new()
    {
        Name = "single track (figure 37.1)",
        Rpm = 60 * 60,          // one rotation per second
        AvgSeekMs = 0.0,        // one track, so there is nothing to seek
        TransferMBps = 1.0,
        TracksPerSurface = 1,
        SectorsPerTrack = 12,
        Surfaces = 1,
    };
}