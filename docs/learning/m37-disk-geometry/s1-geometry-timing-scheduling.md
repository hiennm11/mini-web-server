# Slice 37.1: Geometry, I/O Time, and Disk Scheduling

> **What it does** — runs OSTEP Ch. 37's geometry and timing arithmetic over two datasheet
> drives, and five disk-scheduling policies over a queue at a known head position.

## Route

```
/disk/run?scenario=geometry|random-vs-seq|seek-fraction|schedule
           &drive=cheetah|barracuda&bytes=N&policy=fifo|sstf|nbf|scan|cscan
           &head=N&queue=b1,b2,b3,...
```

Unknown scenario, policy or drive, a non-integer parameter, a duplicate block in the queue,
a non-positive `bytes`, and an unknown parameter name all answer 400.

## The chapter's own numbers

Every expectation is a figure OSTEP prints. Where the chapter rounds, the exact value is
asserted and the rounding is named.

| § | What | Chapter says | Simulator |
|---|---|---|---|
| §37.2 | 15,000 RPM | "a single rotation takes about 6 milliseconds" | 4.000 ms |
| §37.4 | Cheetah average rotation | "the disk will encounter a half rotation and thus 2 ms" | 2.000 ms |
| §37.4 | Cheetah random 4 KB transfer | "vanishingly small (30 microseconds)" | 31.25 µs |
| §37.4 | Cheetah random T_I/O | "roughly equals 6 ms" | 6.031 ms |
| §37.4 | Cheetah random rate | "about 0.66 MB/s" | 0.648 MB/s |
| §37.4 | Barracuda random T_I/O | "about 13.2 ms, more than twice as slow" | 13.204 ms |
| §37.4 | Barracuda random rate | "about 0.31 MB/s" | 0.296 MB/s |
| §37.4 | Cheetah sequential 100 MB | "about 800 ms" | 806 ms |
| §37.4 | Barracuda sequential 100 MB | "about 950 ms" | 965.5 ms |
| §37.4 | Sequential rates | "very nearly the peak transfer rates of 125 MB/s and 105 MB/s" | 124.07 / 103.57 |
| §37.4 | random vs sequential | "almost a factor of 200 ... for the Cheetah and more than a factor 300 ... for the Barracuda" | 191× / 349× |
| §37.4 ASIDE | average seek distance | "one-third the full distance" | N/3 tracks, limit of (N²−1)/(3N) |

## Measured

`scenario=random-vs-seq&drive=cheetah`:

```
workload | T_seek | T_rotation | T_transfer | T_I/O  | R_I/O
random 4KB |    4.00 |       2.00 |      0.031 |   6.03 |  0.648 MB/s
sequential 100MB  |    4.00 |       2.00 |    800.00 |  806.0 |  124.1 MB/s

random is 192x slower than sequential
```

The chapter's factor for the Cheetah is "almost a factor of 200". The gap is the whole
point: the random case pays 6 ms of setup on a 4 KB payload, the sequential case pays the
same 6 ms once on 100 MB.

`scenario=seek-fraction` shows the ASIDE's limit against its exact value:

```
tracks | N/3 (chapter) | exact      | difference
     4 |       1.3333 |     1.2500 |   0.083333
    10 |       3.3333 |     3.3000 |   0.033333
  1000 |     333.3333 |    333.3330 |   0.000333
```

## Scheduling, measured

`scenario=schedule&policy=scan&head=10&queue=50,105,1800,1100,5,15` on a 20-track disk
(requests on tracks 0, 1, 18, 11, 0, 0):

```
policy | total track travel
FIFO   |                46
SSTF   |                26
SCAN   |                26
C-SCAN |                28
```

SSTF wins on distance, SCAN ties it here, and C-SCAN is worse — which is exactly what §37.5
says: *"Unfortunately, SCAN and its cousins do not represent the best scheduling
technology."* Their value is fairness, and head travel cannot express it.

## Tests

Eight, in `tests/MiniWebServer.Host.Tests/Program.cs`:

- `io time is seek plus rotation plus transfer` — equations 37.1 and 37.2, the Cheetah's
  4 ms / 2 ms / 31.25 µs, and its rate.
- `sequential transfers reach the peak rate and random ones do not` — both drives' random
  and sequential figures, and the 200×/300× gap.
- `the average seek distance is one third of the disk` — brute force at N = 4, 5, 10, 17,
  checking that the chapter's N/3 is a **limit** and the exact mean is (N²−1)/(3N).
- `sstf services the nearer track first` — §37.5's figure 37.7, head on the inner track with
  requests for sectors 21 and 2.
- `scan services the next track ahead, not the farthest` — the sweep-order regression, and
  reversal when the current direction runs dry.
- `nearest-block-first measures from the last block, not a track start` — §37.5's point that
  the OS sees blocks, not tracks.
- `sstf order depends on proximity and scan order does not` — the starvation crux.
- `c-scan sweeps one way and resets, unlike scan` — that the two policies genuinely differ.

### One test replaced rather than added

The first version of the starvation test asserted that SCAN *services the far request
before the near ones*. That is not something a finite queue can show. §37.5's mechanism is
"a steady stream of requests to the inner track" — an unbounded arrival process — so with
six queued requests every policy eventually serves every request, and a test claiming
otherwise was asserting something the chapter does not say.

What a finite queue *can* show is that SCAN's order depends on sweep position while SSTF's
depends on distance alone: move the near requests to the far side of the head, and SCAN's
answer changes while SSTF's does not. That is the property that makes starvation impossible
rather than merely unlikely, and it is what the test asserts now.

## Bugs found while building this

Four in the scheduler, all found by review after the suite was green, plus two in the
arithmetic:

1. **SCAN serviced the farthest track ahead.** §37.5 requires "servicing requests in order
   across the tracks", but the selection took the *largest* eligible track, so a head at
   track 10 swept past 11 to serve 18 and only returned for 11 on the way back. That is the
   opposite of the chapter's ordering.
2. **SCAN reversed after every request.** An early version flipped direction each time it
   serviced anything, which made SCAN a near-random permutation with a preference rather
   than a sweep. Reversal belongs at the ends of the disk, and when the queue runs dry in the
   current direction — otherwise the head sits pointing at nothing and falls back to
   arrival order, which is FIFO wearing SCAN's name.
3. **NBF forgot the last serviced block.** Measuring distance from "the first block of the
   head's track" is right once and wrong after that: from block 599, block 598 is one away
   and block 300 is 299 away, but the reconstruction pointed at 600 and chose 300.
4. **C-SCAN serviced behind the head.** §37.3 puts the first sectors on the outermost track,
   so outer-to-inner is the *increasing* direction and a request behind the head waits for
   the reset. The implementation swept the other way, and because SCAN and C-SCAN shared one
   branch, the chapter's claim about middle-track bias could not have been checked at all.
5. **Decimal units in transfer time and rate** — 10^6 instead of 2^20, which put the Cheetah
   at 0.68 MB/s where §37.4 says 0.66.
6. **The route multiplied head travel by the average seek time.** §37.4's ASIDE derives the
   average seek over *distance* and says so; converting needs a seek curve the chapter never
   gives. A request spanning a whole disk reported ~40,000 ms of seeking on a drive whose
   full seek is two to three times 4 ms.

## What this slice does NOT do

- **SPTF** — the chapter says it runs inside the drive.
- **Track skew, multi-zone, write-back caching** (§37.2) — named, not worked.
- **Queue depth, I/O merging, anticipatory scheduling** (§37.5) — named without figures.
- **A seek curve.** Head travel is reported in tracks and not converted to time, for the
  reason in bug 6.

## Source documents

- `docs/learning/m37-disk-geometry/overview.md` — milestone scope.
- `docs/adr/0025-remaining-coverage-gaps-m35-m36-m37.md` — why this is a new milestone, and
  the "Ch. 37 buses" misrecord it retires.
- `docs/adr/0021-m31-lfs-extensions.md` — the caveat about `T_position` that this chapter
  is the answer to.
- OSTEP Ch. 37 §37.1, §37.2, §37.3, §37.4 including its ASIDE, §37.5.