# Milestone 27: Data Integrity and Protection

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does a storage system know that the data it just read is actually the data that was written? What happens when a disk silently returns the wrong bits — or when a write completes successfully but never actually lands? How do checksums help, and where do they fall short?

## Scope

A standalone data-integrity simulator demonstrating OSEP Ch. 45's core ideas:

- **Three checksum functions** (OSEP §45.3): XOR, additive, Fletcher. We compute all three on the same payload so the trade-offs are visible.
- **Checksum layout** (§45.6): one checksum per block, stored alongside the data in a "data integrity segment" per block.
- **Physical ID** (§45.5): each checksum carries the block's identity (disk + block number). Misdirected writes flip the wrong bits to the wrong address; the physical ID detects it.
- **Write sequence number** (§45.6 ZFS-style): each write bumps a per-block sequence number stored with the checksum. Lost writes (write reported but never persisted) are detected by a stale sequence on the next read.
- **Disk scrubber** (§45.7): reads every block, recomputes every checksum, reports failures. Finds bit rot in rarely-accessed blocks.
- **Failure injection**: silent corruption (flip bits in a block), misdirected write (write lands at wrong address), lost write (write returns success but state doesn't change), latent sector error (read returns error code).

Exposed via `/integrity/run?scenario=compute|corrupt|scrub&blocks=N&blockSize=M` HTTP route.

## Slice

- **[s1-checksum-scrub.md](./s1-checksum-scrub.md)** — `Integrity` simulator with three checksum functions + per-block metadata + scrubber + failure injection.

## OSEP coverage

- **Ch. 45 Data Integrity and Protection** (§45.1 disk failure modes; §45.2 LSE handling; §45.3 checksum functions; §45.4 using checksums; §45.5 misdirected writes + physical ID; §45.6 lost writes; §45.7 disk scrubbing; §45.8 overheads).
- §45.1 latency/frequency numbers (§45.1 figure 45.1) deferred — we model the failure types, not the percentages.
- §45.2 RAID-DP recovery for combined disk failure + LSE deferred — that's a RAID extension.
- §45.4 checksum layout (per-sector vs packed) — we model per-block (the simpler case).
- §45.6 ZFS's full end-to-end checksum tree (checksum in every inode + indirect block) deferred — we model the simpler per-block sequence number.
- §45.7 scrubbing schedule (nightly vs weekly) deferred — we expose a one-shot scrubber.
- §45.8 space + time overheads documented but not measured.

OSEP §45.0:
> "How should systems ensure that the data written to storage is protected? What techniques are required? How can such techniques be made efficient, with both low space and time overheads?"

OSEP §45.1:
> "Modern disks will occasionally seem to be mostly working but have trouble successfully accessing one or more blocks. Specifically, two types of single-block failures are common and worthy of consideration: **latent sector errors** (LSEs) and **block corruption**."

OSEP §45.3:
> "The primary mechanism used by modern storage systems to preserve data integrity is called the **checksum**. A checksum is simply the result of a function that takes a chunk of data (say a 4KB block) as input and computes a function over said data, producing a small summary of the contents of the data."

OSEP §45.5:
> "A **misdirected write** ... arises in disk and RAID controllers which write the data to disk correctly, except in the *wrong* location. ... the answer ... is to add a little more information to each checksum. ... adding a **physical identifier** (**physical ID**) is quite helpful."

OSEP §45.6:
> "A **lost write** ... occurs when the device informs the upper layer that a write has completed but in fact it never is persisted; thus, what remains is the old contents of the block rather than the updated new contents."

OSEP §45.7:
> "Many systems utilize **disk scrubbing** of various forms. By periodically reading through *every* block of the system, and checking whether checksums are still valid, the disk system can reduce the chances that all copies of a certain data item become corrupted."

## OSEP §-specific deviations

| OSEP § | We do | We defer |
|---|---|---|
| §45.1 LSEs + corruption | Two failure types modeled: silent corruption (flip bits) and LSE (read returns failure flag). | Real percentages from §45.1 figure 45.1. |
| §45.3 XOR checksum | Per-byte XOR across the block. | None (simplest). |
| §45.3 additive checksum | Per-byte sum mod 256 across the block. | None (simplest). |
| §45.3 Fletcher checksum | s1 = Σ byte_i mod 255; s2 = Σ s1_i mod 255. Two bytes. | CRC. |
| §45.4 layout | One checksum per block, stored in the block's metadata header. | 520-byte sectors / packed checksum blocks. |
| §45.5 physical ID | Every block carries its (disk, block) ID in the metadata. Mismatch on read → detected. | None. |
| §45.6 lost write detection | Each write bumps a per-block sequence number. A subsequent read that sees the old sequence → detected. | Full ZFS end-to-end checksum tree (checksum in every inode). |
| §45.7 scrubber | One-shot: reads every block, runs all three checksums + physical ID + sequence, reports failures. | Periodic scheduling. |
| §45.8 overheads | Tracked (bytes of checksum per block). | Wall-clock measurement. |

## Key OSEP quotes

> "The crux: HOW TO ENSURE DATA INTEGRITY. How should systems ensure that the data written to storage is protected?" (OSEP §45.0)

> "There is no perfect checksum: it is possible two data blocks with non-identical contents will have identical checksums, something referred to as a collision." (OSEP §45.3)

> "There is no such thing as a free lunch." (OSEP §45.3 TIP) — stronger checksums cost more CPU.

> "By periodically reading through *every* block of the system, and checking whether checksums are still valid, the disk system can reduce the chances that all copies of a certain data item become corrupted." (OSEP §45.7)

## .NET mechanism

- `BlockIntegrity` — the metadata header for one block: `PhysicalId`, `WriteSequence`, `XorChecksum`, `AdditiveChecksum`, `FletcherS1`, `FletcherS2`, plus the data payload.
- `IntegrityStore` — the in-memory "disk": `BlockIntegrity[Blocks]`. Methods: `Write(blockId, data)`, `Read(blockId)`, `Scrub()`, `InjectCorruption`, `InjectMisdirectedWrite`, `InjectLostWrite`.
- Pure checksum functions: `XorChecksum(byte[])`, `AdditiveChecksum(byte[])`, `FletcherChecksum(byte[])` returning `(s1, s2)`.

## Files

- `src/MiniWebServer.Host/MiniScheduler/Integrity.cs` (new, ~280 lines):
  - `BlockIntegrity` struct: physical ID, write sequence, three checksums, data.
  - `ScrubReport` record.
  - `IntegrityStore` simulator with the full read/write/scrub + injection surface.
- `src/MiniWebServer.Host/Program.cs` — `/integrity/run?scenario=compute|corrupt|scrub` route.

## What this slice does NOT do

- Real CRC — we use Fletcher as the strongest of the three.
- ZFS-style end-to-end checksum tree (checksum in every inode + indirect block).
- Periodic scheduling — the scrubber is one-shot.
- Real failure percentages from §45.1 figure 45.1.
- 520-byte sector format / packed checksum layout — we model one-block-one-checksum.

## Where this leads

M27 closes the persistence thread:

- **M11** raw I/O
- **M12** vsfs + journaling
- **M21** FFS placement
- **M24** RAID 0/1/4/5
- **M25** LFS
- **M26** SSD FTL
- **M27** data integrity

Part III Persistence is now covered. The lab still has Part IV Security (mostly done in M23) but the persistence side is closed.
