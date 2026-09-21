# Slice 27.1: Checksums, Physical IDs, Write Sequences, and Scrubbing

## What it does

Implements OSEP Ch. 45's data-integrity toolkit as a single in-memory simulator:

- **Three checksum functions** (OSEP §45.3): XOR (per-byte XOR mod 256), additive (per-byte sum mod 256), Fletcher (s1 = Σ mod 255, s2 = Σ s1 mod 255). All three computed on the same payload so the trade-offs are visible.
- **Per-block metadata** (§45.5 + §45.6): physical ID (disk + block number), write sequence, three checksums.
- **Write path**: bump write sequence, recompute checksums, store data + metadata.
- **Read path**: verify physical ID + recompute every checksum + compare against stored.
- **Scrubber** (§45.7): walks every block, runs full verification, reports failures.
- **Fault injection**: `InjectCorruption` (flip bits silently), `InjectMisdirectedWrite` (swap physical ID), `InjectLostWrite` (decrement sequence).

Exposed via `/integrity/run?scenario=compute|corrupt|scrub&blocks=N&blockSize=M` HTTP route.

## Files added/changed

- `src/MiniWebServer.Host/MiniScheduler/Integrity.cs` (new, ~280 lines):
  - `BlockIntegrity` struct: physical ID, write sequence, three checksums, data.
  - `IntegrityChecksums` static helpers: `Xor`, `Additive`, `Fletcher`.
  - `IntegrityStore` simulator: `Write`, `Read`, `Verify`, `GetBlock`, `Scrub`, `InjectCorruption`, `InjectMisdirectedWrite`, `InjectLostWrite`, `FormatLayout`.
  - `ScrubReport` record.
- `src/MiniWebServer.Host/Program.cs` — `/integrity/run?scenario=compute|corrupt|scrub` route (~85 lines).
- `tests/MiniWebServer.Host.Tests/Program.cs` — 7 new tests.

## OSEP alignment

Implements OSEP §45.3 (three checksum functions compared), §45.4 (verify-on-read), §45.5 (physical ID detects misdirected writes), §45.6 (write sequence detects lost writes, simplified ZFS-style), §45.7 (scrubber).

## Smoke evidence

### `?scenario=compute` (run all three checksums on a known payload)

```
=== Integrity Store (M27 / OSEP Ch. 45) ===
disk: 0   blocks: 8   block size: 16 bytes
global write sequence: 1

block | seq | xor | add | fletcher | data preview
------|-----|-----|-----|----------|--------------
  0   | 1   | D7  | 0F  | (13,4F)  | 6^\xC4\xCD\xBA\x14\x8A\x92

trace:
payload (hex): 36-5E-C4-CD-BA-14-8A-92
xor checksum: 0xD7
additive checksum: 0x0F
fletcher checksum: (s1=0x13, s2=0xB6)
```

OSEP §45.3 "Common Checksum Functions": all three checksums computed on the textbook-style payload `36-5E-C4-CD-BA-14-8A-92`. Note that XOR (0xD7) and additive (0x0F) are both single bytes; Fletcher gives two bytes (s1, s2) — strictly more detection power at slightly more cost.

### `?scenario=corrupt` (silent corruption detection)

```
trace:
after corruption: 3 failures detected:
  - xor-checksum-mismatch: stored=D7 computed=D6
  - additive-checksum-mismatch: stored=0F computed=10
  - fletcher-checksum-mismatch: stored=(13,4F) computed=(14,5F)
```

OSEP §45.4 "Using Checksums": after a single-bit flip in the data, all three checksums detect the change. XOR computed value moved by 1 (0xD7 → 0xD6); additive moved by 1 (0x0F → 0x10); Fletcher s1 moved by 1, s2 moved by 0x10 (the propagation depends on the running sum).

### `?scenario=scrub` (scrubber finds both injected faults)

```
scrub: 6 OK, 2 BAD
  block 2:
    - xor-checksum-mismatch: stored=71 computed=61
    - additive-checksum-mismatch: stored=75 computed=85
    - fletcher-checksum-mismatch: stored=(75,25) computed=(85,26)
  block 5:
    - physical-id-mismatch: block thinks disk=99, we are disk=0
```

OSEP §45.7 "Scrubbing": the scrubber walked all 8 blocks. Block 2 has checksum mismatches (silent corruption injected via `InjectCorruption(2, 0, 0x10)`). Block 5 has the misdirected write detected (its physical ID was swapped to disk 99 via `InjectMisdirectedWrite(5, 99)`). The other 6 blocks are clean.

## OSEP concept

> "How should systems ensure that the data written to storage is protected? What techniques are required? How can such techniques be made efficient, with both low space and time overheads?" (OSEP §45.0)

> "Modern disks will occasionally seem to be mostly working but have trouble successfully accessing one or more blocks. Specifically, two types of single-block failures are common and worthy of consideration: **latent sector errors** (LSEs) and **block corruption**." (OSEP §45.1)

> "The primary mechanism used by modern storage systems to preserve data integrity is called the **checksum**." (OSEP §45.3)

> "There is no perfect checksum: it is possible two data blocks with non-identical contents will have identical checksums, something referred to as a collision." (OSEP §45.3)

> "Adding a physical identifier (physical ID) is quite helpful." (OSEP §45.5)

> "Some systems add a checksum elsewhere in the system to detect lost writes. For example, Sun's Zettabyte File System (ZFS) includes a checksum in each file system inode and indirect block for every block included within a file." (OSEP §45.6)

> "By periodically reading through every block of the system, and checking whether checksums are still valid, the disk system can reduce the chances that all copies of a certain data item become corrupted." (OSEP §45.7)

## .NET mechanism

- `BlockIntegrity` — metadata for one block: physical ID, write sequence, three checksums, data.
- `IntegrityChecksums` — pure functions `Xor`, `Additive`, `Fletcher` returning `byte` / `(byte, byte)`.
- `IntegrityStore` — in-memory array of blocks; `Write` bumps sequence + recomputes checksums; `Verify` returns failure list (no throw); `Scrub` walks every block.
- Fault injection: `InjectCorruption` (flip bits without recompute), `InjectMisdirectedWrite` (set fake disk ID), `InjectLostWrite` (decrement sequence).

## What this slice does NOT do

- CRC — Fletcher is the strongest of the three we model.
- Real CRC polynomial arithmetic (CRC-32, etc.).
- ZFS end-to-end checksum tree (checksum in every inode + indirect block).
- Periodic scheduling — the scrubber is one-shot.
- Real failure percentages from §45.1 figure 45.1.
- 520-byte sector format / packed checksum layout.

## Deferred (other integrity extensions)

- **CRC** — polynomial-division-based checksum; stronger than Fletcher at slightly higher cost.
- **ZFS-style end-to-end checksum tree** — checksum in every inode + indirect block; detects lost writes even if both the data block and the inode are silently corrupted.
- **Periodic scrubber** — schedule nightly/weekly background scans.
- **T10 DIF / Data Integrity Extensions** — per-sector checksums stored in the disk's format (520-byte sectors), so the disk itself can detect corruption.
- **RAID-DP** — dual parity to recover when both a full-disk failure and an LSE happen during reconstruction.
