# Milestone 24: RAID — Redundant Arrays of Inexpensive Disks

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does a file system survive a disk crash? What's the difference between striping for performance, mirroring for reliability, and parity for cheap redundancy? Why does RAID 5 exist when RAID 4 already does parity?

## Scope

A standalone RAID simulator that demonstrates OSEP Ch. 38's core levels:

- **RAID 0** (OSEP §38.4): block-level striping across `N` disks (no redundancy — `N`× throughput but `N`× failure rate).
- **RAID 1** (OSEP §38.5): full mirroring on `2` disks (writes doubled; survives `1` of `2` failures).
- **RAID 4** (OSEP §38.6): block-level striping + dedicated parity disk (parity disk is the write bottleneck — every write hits it).
- **RAID 5** (OSEP §38.7): block-level striping + rotating parity (parity writes spread across all disks — no bottleneck).

For each level: encode a stream of blocks (the stripe unit is one block), simulate a single-disk failure, and prove recovery by reconstructing the lost block via XOR.

Exposed via `/raid/run?level=0|1|4|5&disks=N&blocks=M&scenario=write|fail&failed=K` HTTP route.

## Slice

- **[s1-raid-levels.md](./s1-raid-levels.md)** — `Raid` simulator with four levels + a parity-recovery demo.

## OSEP coverage

- **Ch. 38 Redundant Arrays of Inexpensive Disks** (§38.1 interface; §38.2 fault model; §38.3 evaluation axes; §38.4 RAID 0 striping; §38.5 RAID 1 mirroring; §38.6 RAID 4 dedicated parity; §38.7 RAID 5 rotating parity; §38.8 comparison summary; §38.9 RAID 6 mention).
- RAID 2 (bit-level Hamming) and RAID 3 (byte-level striping + parity) deferred — mentioned only in the §38.9 "Other Interesting RAID Issues" paragraph as "Levels 2 and 3 from the original taxonomy", with no dedicated section. Superseded by block-level striping in practice.
- §38.9 RAID 6 (dual parity) deferred — only a one-paragraph mention in §38.9 pointing to the [C+04] paper; extends RAID 5's XOR to a 2D parity (P + Q).

OSEP §38.1 "Interface":
> "A RAID system ... presents to the host file system a clean interface: a sequence of blocks that the file system reads or writes. ... The file system is thus mostly oblivious to the fact that the underlying storage has been split across multiple physical disks."

OSEP §38.2 "How To Make RAID Work — The Failure Model":
> "We assume ... that any (and only) one of the N disks in the array may fail at any given time. This assumption ... follows directly from a straightforward statistical argument: ... if the mean-time-to-failure (MTTF) of a single disk is, say, 3 years, the MTTF of an array of 100 disks is about 3 years / 100, or roughly 11 days."

OSEP §38.4 "RAID Level 0: Striping":
> "RAID level 0 is a straightforward mapping of stripes across disks. ... An obvious benefit of striping is performance: if you have N disks, you get N times the bandwidth (e.g., 4 disks means 4× the bandwidth). The downside, of course, is reliability — the MTTF of the array drops by a factor of N."

OSEP §38.5 "RAID Level 1: Mirroring":
> "With mirroring, we make a copy of every block we write to disk. So, each logical write becomes two physical writes. ... When reading, we can read from either disk (the one with the shorter seek time), so the read performance can be improved by reading from both disks in parallel."

OSEP §38.6 "RAID Level 4: Saving Space With Parity":
> "RAID 4 is a hybrid of striping and parity. ... In RAID 4, we have a dedicated parity disk. The parity block is computed by XOR-ing all the corresponding data blocks in the stripe. ... When a write comes in for any data block, the parity disk must also be updated, thus creating a small write bottleneck."

OSEP §38.7 "RAID Level 5: Rotating Parity":
> "RAID 5 solves the small-write problem of RAID 4 by rotating the parity block across all disks. ... With RAID 5, parity writes are spread across all disks, eliminating the parity-disk bottleneck."

## OSEP §-specific deviations

| OSEP § | We do | We defer |
|---|---|---|
| §38.1 transparent interface | `Raid.Write(blockId, value)` / `Raid.Read(blockId)` — the file system sees a flat array. | True device-driver integration. |
| §38.2 independent failure model | One disk can fail at a time. Recovery reconstructs the lost block via XOR. | Concurrency of multi-disk failure (§38.9 RAID 6 territory). |
| §38.4 RAID 0 striping | Round-robin across `N` disks; stripe unit = one block. | Chunked striping with chunk size > 1 block. |
| §38.5 RAID 1 mirroring | Full duplicate on `2` disks. Reads pick first surviving copy. | >2 mirrors, asymmetric read preference. |
| §38.6 RAID 4 dedicated parity | Parity on a fixed disk (disk `N-1`). Single-write case is the bottleneck demo. | Hot-spare swap. |
| §38.7 RAID 5 rotating parity | Parity stripes rotate: stripe `i`'s parity lives on disk `(i + offset) % N`. | I/O scheduling (e.g., shortest-seek-first placement). |
| Recovery via XOR | After failure of disk `f`, the lost block at stripe `s` is `⊕` of the surviving `N-1` blocks. | Background scrubbing + reconstruction scheduling. |
| §38.9 RAID 2/3 mention | Not implemented. | Bit- or byte-level striping. |
| §38.9 RAID 6 | Not implemented. | Dual parity (P+Q). |

## Key OSEP quotes

> "When you build a system, you should make it work, and then make it work better." (M24 route smoke output; paraphrasing §38.4 build-up) — the chapter builds RAID 0 first, then adds redundancy, then optimizes the bottleneck.

> "The fundamental insight is the XOR operation: XOR of all bits in a row of bits (i.e., the parity) equals 0 if there are an even number of 1s and 1 if odd. ... If we XOR a value into the parity, the parity becomes incorrect. To recompute, we can just XOR out the old value and XOR in the new one." (OSEP §38.6)

> "In RAID 5, the parity is rotated across the disks in the array, so each disk eventually becomes the parity disk. ... This solves the small-write problem." (OSEP §38.7)

## .NET mechanism

- `byte[][]` per disk (one slot per physical block). `Raid.Write(blockId, value)` writes to one or more disks depending on level; `Raid.Read(blockId)` reads + reconstructs if any disk failed.
- XOR recovery: a `BlockAt(stripe, diskIndex)` helper that, on a failed disk, XORs the other `N-1` blocks in the stripe (OSEP §38.6 + §38.7 "to recompute parity, you can just XOR").
- Rotating parity: parity-stripe index = `stripe % N` (the textbook choice — parity stripe `s` lives on disk `(s + 1) % N` for RAID 5, on disk `N-1` for RAID 4).

## Files

- `src/MiniWebServer.Host/MiniScheduler/Raid.cs` (new, ~250 lines):
  - `RaidLevel` enum: `Raid0`, `Raid1`, `Raid4`, `Raid5`.
  - `Raid` simulator: `Write(stripeId, values)` (writes the stripe across disks), `Read(stripeId)` (returns the block reconstructed from surviving disks), `FailDisk(diskIdx)`, `FormatLayout()` (the grid showing where each block landed).
- `src/MiniWebServer.Host/Program.cs` — `/raid/run?level=...&disks=N&blocks=M&failed=K` route.

## What this slice does NOT do

- RAID 2 (bit-level Hamming) and RAID 3 (byte-level striping) — superseded by block-level striping.
- RAID 6 (dual parity) — XOR + a second parity code (e.g., Reed-Solomon).
- Real device simulation — the "disks" are in-memory `byte[][]`.
- Concurrency — writes are serialized through a single test; no locking.
- Hot-spare swap, background scrubbing, reconstruction scheduling.
- Detection of which disk failed — caller specifies via `?failed=K`.

## Where this leads

After M24 the roadmap continues with:

- **M25 LFS** (Ch. 43)
- **M26 Flash-based SSDs** (Ch. 44)
- **M27 Data integrity** (Ch. 45)

These round out Part III Persistence. Once M24–M27 are done, the persistence thread is closed.
