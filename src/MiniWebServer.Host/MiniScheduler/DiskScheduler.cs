namespace MiniWebServer.Host.MiniScheduler;

/// <summary>§37.5: which pending request a drive services next.</summary>
public enum DiskPolicy
{
    /// <summary>§37.5 FCFS — the order the requests arrived.</summary>
    Fifo,

    /// <summary>§37.5 SSTF — "shortest-seek-time-first", nearest track first.</summary>
    Sstf,

    /// <summary>
    /// §37.5 NBF — "nearest-block-first", the nearest *block address* next.
    /// §37.5's fix for SSTF's first problem: "the drive geometry is not available
    /// to the host OS; rather, it sees an array of blocks".
    /// </summary>
    NearestBlock,

    /// <summary>§37.5 SCAN — the elevator: sweep to one end, then reverse.</summary>
    Scan,

    /// <summary>§37.5 C-SCAN — "only sweeps from outer-to-inner, and then resets".</summary>
    CScan,
}

/// <summary>
/// OSEP §37.5: disk scheduling over a queue of requests at a known head position.
/// Slice 37.1.
/// </summary>
/// <remarks>
/// <para>
/// §37.5 contrasts this with CPU scheduling: "Unlike job scheduling, where the
/// length of each job is usually unknown, with disk scheduling, we can make a good
/// guess at how long a 'job' (i.e., disk request) will take."
/// </para>
/// <para>
/// The chapter walks the policies through two cruxes — starvation, then rotation —
/// and this models the first. SPTF, the answer to the second, is not implemented
/// because §37.5 says it "is usually performed inside a drive".
/// </para>
/// </remarks>
public sealed class DiskScheduler
{
    private readonly DriveGeometry _drive;

    /// <summary>
    /// Tracks are numbered with 0 outermost (§37.3: "the outermost track contains
    /// the first sectors"), so ascending track numbers move inward. SCAN sweeps
    /// outward-to-inward on <c>true</c> and reverses at the ends; C-SCAN always
    /// sweeps outward-to-inward and then resets.
    /// </summary>
    public bool GoingInward { get; private set; } = true;

    /// <summary>
    /// The block most recently serviced. NBF compares against this, because §37.5's
    /// whole point is that the OS knows block addresses and not tracks — so it must
    /// remember where it left off in *block* space, not reconstruct a track start.
    /// </summary>
    private long _lastBlock;

    public DiskScheduler(DriveGeometry drive) => _drive = drive;

    public static long Choose(DriveGeometry drive, DiskPolicy policy,
        IReadOnlyList<long> queue, long headTrack, bool goingInward, long headBlock)
    {
        if (queue.Count == 0) throw new ArgumentException("no requests to schedule");

        switch (policy)
        {
            case DiskPolicy.Fifo:
                return queue[0];

            case DiskPolicy.Sstf:
            case DiskPolicy.NearestBlock:
            {
                int best = -1;
                long bestKey = 0;
                for (int i = 0; i < queue.Count; i++)
                {
                    // SSTF knows where tracks are; NBF does not, so it measures
                    // distance in blocks. The two coincide whenever tracks are one
                    // block wide and differ as soon as requests share a track.
                    long key = policy == DiskPolicy.Sstf
                        ? Math.Abs(drive.TrackOf(queue[i]) - headTrack)
                        : Math.Abs(queue[i] - headBlock);
                    if (best < 0 || key < bestKey)
                    {
                        best = i;
                        bestKey = key;
                    }
                }
                return queue[best];
            }

            case DiskPolicy.Scan:
            {
                // §37.5: SCAN "simply moves back and forth across the disk
                // servicing requests in order across the tracks". The defining
                // property is ORDER, not distance: on an inward sweep the head
                // takes the *next* track with a pending request, which is the
                // smallest eligible one - not the farthest.
                int best = -1;
                long bestTrack = 0;
                for (int i = 0; i < queue.Count; i++)
                {
                    long track = drive.TrackOf(queue[i]);
                    bool ahead = goingInward ? track >= headTrack : track <= headTrack;
                    if (!ahead) continue;
                    if (best < 0 || (goingInward ? track < bestTrack : track > bestTrack))
                    {
                        best = i;
                        bestTrack = track;
                    }
                }
                // Nothing ahead: this sweep is over and the next goes the other way.
                return best >= 0 ? queue[best] : queue[0];
            }

            case DiskPolicy.CScan:
            {
                // §37.5: C-SCAN "only sweeps from outer-to-inner, and then resets
                // at the outer track to begin again. Doing so is a bit more fair to
                // inner and outer tracks, as pure back- and-forth SCAN favors the
                // middle tracks."
                //
                // §37.3 fixes which end is which: "the outermost track contains the
                // first sectors (0 through 11)", so track 0 is outer and increasing
                // track numbers move inward. The sweep is therefore always in the
                // increasing direction, and a request *behind* the head is not
                // serviced on this sweep - it waits for the reset.
                int best = -1;
                long bestTrack = long.MaxValue;
                for (int i = 0; i < queue.Count; i++)
                {
                    long track = drive.TrackOf(queue[i]);
                    if (track < headTrack) continue;          // behind; waits for reset
                    if (best < 0 || track < bestTrack)
                    {
                        best = i;
                        bestTrack = track;
                    }
                }
                if (best >= 0) return queue[best];

                // Nothing ahead: reset to the outermost pending track and begin again.
                int outer = -1;
                long outermost = long.MaxValue;
                for (int i = 0; i < queue.Count; i++)
                {
                    long track = drive.TrackOf(queue[i]);
                    if (outer < 0 || track < outermost)
                    {
                        outer = i;
                        outermost = track;
                    }
                }
                return queue[outer];
            }

            default:
                throw new InvalidOperationException($"unhandled policy {policy}");
        }
    }

    /// <summary>
    /// Serves a whole queue, returning the service order and the total head travel
    /// in tracks, including the movement a SCAN reset makes to reach the outer end.
    /// </summary>
    public (IReadOnlyList<long> Order, long TracksTravelled) Serve(
        DiskPolicy policy, IReadOnlyList<long> requests, long startTrack)
    {
        var queue = new List<long>(requests);
        var order = new List<long>(queue.Count);
        long head = startTrack;
        long travel = 0;

        long outerTrack = 0;
        long innerTrack = Math.Max(0, _drive.TracksPerSurface - 1);

        // NBF measures from a block address. Starting "at the head's first block"
        // is the only interpretation available when the caller gives a track.
        _lastBlock = _drive.TrackStart(startTrack);

        // A head already at an end starts the sweep inward.
        GoingInward = head < innerTrack;
        if (head <= outerTrack) GoingInward = true;

        while (queue.Count > 0)
        {
            // If nothing is pending in the current sweep direction, the sweep is
            // over: reverse and look again. Choosing "the first queued request"
            // instead would keep the head pointing at an empty direction and
            // service the queue in arrival order, which is FIFO wearing SCAN's
            // name rather than a sweep.
            if (policy == DiskPolicy.Scan)
            {
                bool anythingAhead = queue.Any(b =>
                {
                    long t = _drive.TrackOf(b);
                    return GoingInward ? t >= head : t <= head;
                });
                // Reverse either at a disk end or when the queue runs out in the
                // current direction. SCAN sweeps to the physical end, so a queue
                // that empties earlier simply stops without the extra travel.
                if (!anythingAhead || (GoingInward && head >= innerTrack) ||
                    (!GoingInward && head <= outerTrack))
                {
                    GoingInward = !GoingInward;
                }
            }

            long next = Choose(_drive, policy, queue, head, GoingInward, _lastBlock);
            queue.Remove(next);

            long nextTrack = _drive.TrackOf(next);
            travel += Math.Abs(nextTrack - head);
            head = nextTrack;
            _lastBlock = next;
            order.Add(next);

            if (policy == DiskPolicy.CScan && nextTrack <= outerTrack)
            {
                // At the outer end the next sweep resets back out to the head's
                // starting position, which is a real physical movement.
                long target = queue.Count > 0
                    ? _drive.TrackOf(queue.MaxBy(b => _drive.TrackOf(b)))
                    : _drive.TrackOf(next);
                travel += Math.Abs(target - head);
                head = target;
            }
        }
        return (order, travel);
    }
}
