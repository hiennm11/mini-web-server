# Slice 24.1: RAID Levels 0 / 1 / 4 / 5

## What it does

Implements the four canonical RAID levels in OSEP Ch. 38 as a single in-memory simulator:

- **RAID 0** (§38.3) — block-level striping across `N` disks (no redundancy).
- **RAID 1** (§38.4) — full mirroring on 2 disks.
- **RAID 4** (§38.7) — block-level striping + dedicated parity disk.
- **RAID 5** (§38.8) — block-level striping + rotating parity.

The simulator exposes:

- `WriteX(stripe/block, value)` / `ReadX(stripe/block, ...)` — full-stripe and small-write paths.
- `FailDisk(d)` / `ReviveDisk()` — single-disk failure model per §38.2.
- `FormatLayout()` — disk-grid ASCII dump with parity cells marked.
- `ParityDiskFor(stripe)` / `DataDiskFor(stripe, idx)` — placement helpers for RAID 4/5.

Recovery via XOR (OSEP §38.7 "fundamental insight"): when a disk fails, the lost block at stripe `s` is `⊕` of the surviving `N-1` blocks in that stripe.

## Files added/changed

- `src/MiniWebServer.Host/MiniScheduler/Raid.cs` (new, ~370 lines):
  - `RaidLevel` enum: `Raid0, Raid1, Raid4, Raid5`.
  - `Raid` simulator with `WriteRaid0` / `ReadRaid0` / `WriteRaid1` / `ReadRaid1` / `WriteRaidParity` / `ReadRaidParity` / `WriteStripeRaidParity` / `ReadParity` / `ParityDiskFor` / `DataDiskFor` / `FailDisk` / `ReviveDisk` / `FormatLayout`.
- `src/MiniWebServer.Host/Program.cs` — `/raid/run?level=N&disks=N&blocks=M&failed=K` route.
- `tests/MiniWebServer.Host.Tests/Program.cs` — 10 new tests.

## OSEP alignment

Implements OSEP §38.3 (RAID 0 striping), §38.4 (RAID 1 mirroring), §38.7 (RAID 4 dedicated parity), §38.8 (RAID 5 rotating parity + §38.7 XOR recovery).

## Smoke evidence

### `?level=0&disks=3&blocks=2` (RAID 0 striping)

```
=== RAID Layout (M24 / OSEP Ch. 38) ===
level: Raid0   disks: 3   blocks: 2

disk | stripe -> (disk, block)
-----|--------------------------------------------------
  0  | [A D]
  1  | [B E]
  2  | [C F]

trace: wrote 6 logical blocks across 3 disks
trace: read OK: A B C D E F
```
Round-robin across 3 disks: blocks 0,3→disk 0; 1,4→disk 1; 2,5→disk 2. Matches §38.3 "round-robin placement of blocks across disks".

### `?level=1&disks=2&blocks=4` (RAID 1 mirroring)

```
level: Raid1   disks: 2   blocks: 4

disk | block values (mirror)
-----|--------------------------------------------------
  0  | [A B C D]
  1  | [A B C D]

trace: wrote 4 logical blocks mirrored across 2 disks
trace: read OK: A B C D
```
Both disks carry the same bytes — §38.4 "each logical write becomes two physical writes".

### `?level=4&disks=4&blocks=4` (RAID 4 dedicated parity)

```
level: Raid4   disks: 4   blocks: 4

disk | stripe rows (parity cell marked with [P])
-----|--------------------------------------------------
  0  | [A D G J]
  1  | [B E H K]
  2  | [C F I L]
  3  | [P@ PG PF PM]

trace: wrote 4 stripes with 3 data bytes each
trace: read OK: stripe0=[A,B,C,P@] stripe1=[D,E,F,PG] stripe2=[G,H,I,PF] stripe3=[J,K,L,PM]
```
Disk 3 holds the parity for every stripe (the §38.7 dedicated parity disk). Each `Pn` is the XOR of the data bytes in that stripe.

### `?level=5&disks=4&blocks=4` (RAID 5 rotating parity)

```
level: Raid5   disks: 4   blocks: 4

disk | stripe rows (parity cell marked with [P])
-----|--------------------------------------------------
  0  | [A D G PM]
  1  | [P@ E H J]
  2  | [B PG I K]
  3  | [C F PF L]

parity rotation (stripe -> parity disk):
  stripe 0 -> disk 1
  stripe 1 -> disk 2
  stripe 2 -> disk 3
  stripe 3 -> disk 0

trace: wrote 4 stripes with 3 data bytes each
trace: read OK: stripe0=[A,B,C,P@] stripe1=[D,E,F,PG] stripe2=[G,H,I,PF] stripe3=[J,K,L,PM]
```
Parity stripe `s` lives on disk `(s + 1) % N` — exactly OSEP §38.8 figure 38.8's diagonal layout.

### `?level=5&disks=4&blocks=4&failed=0` (RAID 5 XOR recovery)

```
* failed disk: 0 (recovery via XOR)

disk | stripe rows (parity cell marked with [P])
-----|--------------------------------------------------
  0  | [A D G PM] [DEAD]
  1  | [P@ E H J]
  2  | [B PG I K]
  3  | [C F PF L]

trace: wrote 4 stripes with 3 data bytes each
trace: read OK: stripe0=[A,B,C,P@] stripe1=[D,E,F,PG] stripe2=[G,H,I,PF] stripe3=[J,K,L,PM]
```
Disk 0 holds data for stripes 0,1,2 and parity for stripe 3. After failing disk 0, every stripe still reads back correctly — the lost data blocks (A, D, G) and the lost parity block (PM) are reconstructed by XOR of the surviving three disks.

### `?level=0&disks=3&blocks=2&failed=1` (RAID 0 unrecoverable)

```
* failed disk: 1 (recovery via XOR - UNRECOVERABLE)

  1  | [B E] [DEAD]

trace: read FAIL: RAID 0 cannot recover from disk failure
```
RAID 0 has no redundancy — the failure is unrecoverable (§38.3 "the MTTF of the array drops by a factor of N").

### `?level=1&disks=2&blocks=4&failed=0` (RAID 1 mirror survives)

```
* failed disk: 0 (recovery via mirror)

  0  | [A B C D] [DEAD]
  1  | [A B C D]

trace: read OK: A B C D
```
The mirror on disk 1 serves every read (§38.4 "we can read from either disk").

## OSEP concept

> "When you build a system, you should make it work, and then make it work better." (OSEP §38.4 TIP)

> "The fundamental insight is the XOR operation: XOR of all bits in a row of bits (i.e., the parity) equals 0 if there are an even number of 1s and 1 if odd." (OSEP §38.7)

> "RAID 5 solves the small-write problem of RAID 4 by rotating the parity block across all disks." (OSEP §38.8)

> "We assume ... that any (and only) one of the N disks in the array may fail at any given time." (OSEP §38.2)

## .NET mechanism

- `byte[][]` per disk. Each "block" is one byte (the simulator demonstrates layout + recovery, not real block size).
- XOR recovery: `for d in 0..N-1 { if (d != failedDisk) recovered ^= disk[d][stripe]; }` — exactly OSEP §38.7's "just XOR out the old and XOR in the new" applied to the lost-block recovery case.
- Rotating parity: `parityDiskFor(s) = (s + 1) % N` — direct translation of §38.8 figure 38.8.

## What this slice does NOT do

- RAID 2 (bit-level Hamming) and RAID 3 (byte-level striping + parity) — superseded by block-level striping.
- RAID 6 (dual parity) — extends XOR to a second parity code (Reed-Solomon).
- Real device simulation — the disks are in-memory `byte[][]`.
- Concurrency — writes are serialized; no locking.
- Hot-spare swap, background scrubbing, reconstruction scheduling.

## Deferred (other RAID extensions)

- **RAID 6 dual parity** (OSEP §38.9) — `P = XOR(d1, ..., dN-1)`, `Q = ReedSolomon(d1, ..., dN-1)`.
- **Chunked striping** — stripe size > 1 block for sequential workloads.
- **Hot spare** — a standby disk the controller can swap in automatically.
- **MTTF simulator** — `Random.Shared` failure timing to demonstrate §38.2's MTTF math.
