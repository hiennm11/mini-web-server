# Milestone 37: Disk Geometry and Disk Scheduling (Ch. 37)

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

Where do a disk's blocks actually live, what does one I/O cost, and which pending request
should the drive serve next?

## Scope

OSEP Ch. 37 §37.1-§37.5, in two simulators exposed at `/disk/run`.

- **Geometry** (§37.2-§37.3) — platters, surfaces, tracks, sectors, and how a block number
  decomposes into a track and a position within it.
- **I/O time** (§37.4) — equation 37.1, `T_I/O = T_seek + T_rotation + T_transfer`, and
  equation 37.2, `R_I/O = Size / T_I/O`.
- **Disk scheduling** (§37.5) — FIFO, SSTF, NBF, SCAN, C-SCAN, with the head travel each costs.

## Why this is a milestone

ADR 0025 named Ch. 37 as M37 and recorded that `ROADMAP.md` had excluded it as *"Ch. 37
buses"*. Buses are Ch. 36 §36.7-§36.9; Ch. 37 is *Hard Disk Drives*. The exclusion was a
number mistake, and it hid a real dependency.

M21 (FFS) and M31 (LFS segment sizing) both treat positioning time as a constant. M31's
cost model is `D = (F/(1-F)) × R_peak × T_position`, where `T_position` is a single number
the caller supplies — and ADR 0021 flags twice that this "ignores disk geometry (seek
curves, rotational latency as a function of position)". Ch. 37 is where those curves live.
Modelling them turns "positioning time" from a constant into a function of where the head is
and which way it is moving, which is the caveat the two earlier milestones could only state.

## OSTEP coverage

- **§37.1 The Interface** — sectors as the address space, 512-byte blocks, the "unwritten
  contract" that nearby blocks are faster. Implemented: `SectorBytes`, `TrackOf`, `SectorOf`,
  `TotalSectors`, surfaced by `/disk/run?scenario=geometry`.
- **§37.2 Basic Geometry** — platter, surface, spindle, track, head, arm; rotation period
  from RPM; the seek phases and settling time; track skew, multi-zoned disks, and the
  write-back / write-through cache choice.
- **§37.3 A Simple Disk Drive** — one track of 12 sectors, then three tracks; rotational
  delay; seek time; the three phases of I/O.
- **§37.4 I/O Time** — equations 37.1 and 37.2; the Cheetah 15K.5 and Barracuda worked
  examples; the random-versus-sequential gap; the ASIDE deriving average seek as one third;
  dimensional analysis.
- **§37.5 Disk Scheduling** — SSTF, its starvation problem, NBF as the fix for not knowing
  the geometry, SCAN and its variants, and SPTF as the rotation-aware answer.

**Not covered**: **track skew**, **multi-zoned disks**, and the **cache / write-back
policy** from §37.2's "Some Other Details" — all named in one paragraph each, and each a
modifier of the geometry rather than a policy. **SPTF** is named and explicitly not
implemented, for the chapter's own reason: it "is usually performed inside a drive", which is
where the geometry and head position are actually known. The other "scheduling issues" —
queue depth at the OS/drive boundary, I/O merging, and anticipatory scheduling — are named
without worked figures and are deferred.

## Files

- `src/MiniWebServer.Host/MiniScheduler/DriveGeometry.cs` — geometry, the two datasheet
  drives, and the I/O-time arithmetic.
- `src/MiniWebServer.Host/MiniScheduler/DiskScheduler.cs` — `DiskPolicy` and
  `DiskScheduler`.
- `src/MiniWebServer.Host/Program.cs` — `/disk/run` and its four formatters.

## Implementation notes

**Binary units throughout, because the chapter uses them.** §37.4's dimensional analysis
writes KB as 1024 bytes, and its worked figures only reproduce that way: 4 KB over 6 ms
gives 0.66 MB/s with 2^10 and 0.68 with 10^6. The chapter's "about 0.66" is the binary
reading, and a decimal implementation disagrees with the textbook by 2% on every rate.

**Distance is not time.** §37.4's ASIDE derives the average seek over *distance* and says
so. The route reports head travel in tracks and refuses to multiply it by the datasheet's
average seek time, because that number is a manufacturer's average over unknown distances
and §37.2 warns a full seek "would likely take two or three times longer". Converting needs
a seek curve, which the chapter never gives.

**Track 0 is outermost.** §37.3's layout puts "the first sectors (0 through 11)" on the
outermost track, so ascending track numbers move inward. This is what makes C-SCAN's
direction well defined: it sweeps outer-to-inner, which is the *increasing* direction, and
resets when it falls off the outer end.

## What this slice does NOT do

- **SPTF** (§37.5) — the chapter says it runs inside the drive.
- **Track skew, multi-zone, cache policy** (§37.2) — named in a paragraph, not worked.
- **Queue depth, I/O merging, anticipatory scheduling** (§37.5) — named without figures.
- **Real spindles.** Time is arithmetic over RPM and seek constants; nothing here waits.
- **Wiring `T_position` back into M31.** The segment sizer now has a chapter to point at for
  what `T_position` ought to depend on, but changing M31's cost model is its own change.