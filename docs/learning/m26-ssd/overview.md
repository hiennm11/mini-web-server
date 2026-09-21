# Milestone 26: Flash-based SSDs (FTL + Garbage Collection + Wear Leveling)

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

Why can't you just write to a flash page like you write to a disk block? What does the FTL actually do, and why does garbage collection cost write amplification? What does wear leveling protect against?

## Scope

A standalone SSD simulator demonstrating OSEP Ch. 44's core ideas:

- **Raw flash model** (OSEP §44.2–§44.4): blocks of pages. Pages have states `INVALID → ERASED → VALID`. Erase operates on a whole block at once; program operates on one page; erase + program are far more expensive than read.
- **Flash Translation Layer (FTL)** (§44.5, §44.7): turns client `read(LBA)` / `write(LBA)` calls into the underlying read/erase/program sequence. A page-level mapping table `LBA → physicalPage` is kept in memory.
- **Log-structured writes** (§44.7): writes append to the next free page in the current block (the "log"). Old versions become dead.
- **Garbage collection (GC)** (§44.8): when no free pages remain, pick a block with the most dead pages, read live pages to the log, erase the block.
- **Wear leveling** (§44.10): every erase bumps the block's erase count; we track wear and can dump a histogram.
- **Trim hint** (§44.8 ASIDE): a `trim(LBA)` operation that lets the GC drop a dead block without consulting the mapping table.

Exposed via `/ssd/run?scenario=write|gc|wear&blocks=N&pages=K` HTTP route.

## Slice

- **[s1-ssd-ftl-gc.md](./s1-ssd-ftl-gc.md)** — `Ssd` simulator with FTL + GC + wear tracking.

## OSEP coverage

- **Ch. 44 Flash-based SSDs** (§44.1 storing a single bit; §44.2 bits to banks/planes; §44.3 basic flash operations read/erase/program; §44.4 performance + wear; §44.5 raw flash to SSD; §44.6 direct-mapped FTL — bad approach; §44.7 log-structured FTL + mapping table; §44.8 garbage collection; §44.9 mapping table size; §44.10 wear leveling; §44.12 TRIM).
- §44.1 SLC/MLC/TLC distinction deferred — we model generic pages with one byte each.
- §44.2 banks/chips/planes deferred — we model a single chip.
- §44.4 raw performance numbers (µs) deferred — we don't simulate timing.
- §44.6 direct-mapped FTL not implemented — OSEP itself calls it a "bad approach".
- §44.9 block-level + hybrid FTL not implemented — we use page-level mapping (the simple form), which OSEP notes is impractical for 1 TB devices but is the most explicit demonstration.
- §44.11 SSD-vs-HDD performance comparison deferred.
- §44.12 read/program disturbance deferred.

OSEP §44.0:
> "Flash, as we'll see, has some unique properties. For example, to write to a given chunk of it (i.e., a flash page), you first have to erase a bigger chunk (i.e., a flash block), which can be quite expensive. In addition, writing too often to a page will cause it to **wear out**."

OSEP §44.3:
> "Read (a page): ... typically quite fast, 10s of microseconds or so, regardless of location on the device."
> "Erase (a block): ... quite expensive, taking a few milliseconds to complete."
> "Program (a page): ... usually taking around 100s of microseconds on modern flash chips."

OSEP §44.7:
> "Upon a write to logical block N, the device appends the write to the next free spot in the currently-being-written-to block; we call this style of writing **logging**. To allow for subsequent reads of block N, the device keeps a **mapping table** (in its memory, and persistent, in some form, on the device); this table stores the physical address of each logical block in the system."

OSEP §44.8:
> "The process of finding garbage blocks (also called **dead blocks**) and reclaiming them for use is called **garbage collection**, and it is an important component of any modern SSD. The basic process is simple: find a block that contains one or more garbage pages, read in the live (non-garbage) pages from that block, write out those live pages to the log, and (finally) reclaim the entire block for use in writing."

OSEP §44.10:
> "Because multiple erase/program cycles will wear out a flash block, the FTL should try its best to spread that work across all the blocks of the device evenly. In this manner, all blocks will wear out at roughly the same time, instead of a few 'popular' blocks quickly becoming unusable."

## OSEP §-specific deviations

| OSEP § | We do | We defer |
|---|---|---|
| §44.3 read/erase/program | Three low-level ops exposed by the simulator. Block-level erase. | Microsecond timing. |
| §44.4 wear | Per-block erase count; we surface a `WearHistogram` report. | Manufacturer P/E cycle limits (SLC 100k, MLC 10k). |
| §44.5 FTL interface | Client calls `Read(lba)` / `Write(lba, value)` / `Trim(lba)`. Same interface as a disk. | Multi-chip parallelism. |
| §44.6 direct-mapped FTL | Not implemented (OSEP calls it bad). | N/A |
| §44.7 log-structured writes | Writes append to next free page; mapping table `LBA → physicalPage`. | OOB area + persistent mapping table. |
| §44.7 page-level mapping | One entry per page. Simplest mapping. | Block-level + hybrid (page+block) mapping. |
| §44.8 garbage collection | Pick block with most dead pages; read live pages to log; erase the block. | Switch merge / partial merge / full merge (hybrid-FTL terminology). |
| §44.8 over-provisioning | Not modeled — the SSD reports its full capacity. | Reserved blocks hidden from the client. |
| §44.10 wear leveling | Track erase count per block; surface in `WearReport`. | Active cold-data migration (real wear leveling). |
| §44.12 trim | `Trim(lba)` drops the mapping entry; the physical page becomes dead. | Batch TRIM commands. |
| §44.4 disturbance | Not modeled. | Read / program disturbs. |

## Key OSEP quotes

> "The crux: HOW TO BUILD A FLASH-BASED SSD? How can we handle the expensive nature of erasing? How can we build a device that lasts a long time, given that repeated overwrite will wear the device out?" (OSEP §44.0)

> "When writing to a page within a flash, you first have to erase a bigger chunk (i.e., a flash block), which can be quite expensive." (OSEP §44.3)

> "The log-based approach by its nature improves performance (erases only being required once in a while, and the costly read-modify-write of the direct-mapped approach avoided altogether), and greatly enhances reliability." (OSEP §44.7)

> "Excessive garbage collection drives up write amplification and lowers performance." (OSEP §44.8)

## .NET mechanism

- `SsdPageState` enum: `Invalid, Erased, Valid, Dead`.
- `FlashBlock` (id, page states, page values, erase count).
- `Ssd` simulator:
  - `Write(lba, value)` — log-structured append to next free page + update mapping.
  - `Read(lba)` — look up mapping → read page.
  - `Trim(lba)` — drop mapping, mark the page Dead (so GC knows).
  - `CollectGarbage()` — pick block with most dead pages, migrate live pages, erase.
  - `Erase(blockId)` — bumps erase count + resets all pages to Erased.
  - `WearReport` — block id → erase count histogram.

## Files

- `src/MiniWebServer.Host/MiniScheduler/Ssd.cs` (new, ~250 lines):
  - `SsdPageState` enum + `FlashBlock`.
  - `SsdWearReport` (block id, erase count, page distribution).
  - `SsdGarbageCollectReport` (cleaned block, live pages migrated, dead pages freed).
  - `Ssd` simulator with the full FTL/GC/wear surface.
- `src/MiniWebServer.Host/Program.cs` — `/ssd/run?scenario=write|gc|wear` route.

## What this slice does NOT do

- Multi-chip parallelism (real SSDs use many flash chips in parallel).
- Block-level or hybrid mapping (we use page-level; OSEP §44.9 explains why this doesn't scale to TB devices).
- Over-provisioning (real SSDs reserve 7–28% of capacity for GC [A+08]).
- Microsecond timing — every operation is O(1).
- Wear-leveling migration (cold-data shuffling). We track erase count but don't move data.
- Disturbance errors.
- Out-of-band mapping persistence (real FTLs store per-page mapping in an OOB area so the table can be reconstructed after power loss).

## Where this leads

After M26 the roadmap continues with:

- **M27 Data integrity** (Ch. 45)

M27 closes the persistence thread. Once M24–M27 are done, Part III Persistence is complete.
