# Mini Web Server Context

## Status Snapshot

Updated 2026-09-21 (M27 data integrity added — closes Part III Persistence). Legend: ✅ built + experimented + noted · 🟡 planned · ⬜ future.

| Slice | Capability | OSTEP chapter | Status |
|-------|-----------|---------------|--------|
| 1.1 | Raw socket lifecycle (`Socket.Bind`/`Listen`/`Accept`/`Receive`/`Send`) | Ch. 4, 6 | ✅ |
| 1.2 | Parse HTTP request → method, path, version, headers | Ch. 4, 13, 36 | ✅ |
| 1.3 | Serve static files from `wwwroot`, reject path traversal | Ch. 39 | ✅ |
| 1.4 | Robust receive loop until `\r\n\r\n` + `Content-Length` bytes | Ch. 4.4, 36 | ✅ |
| 4.1 | Prove single-thread blocking via `/slow` (`Thread.Sleep`) | Ch. 4, 4.4 | ✅ |
| 4.2 | Spawn one background thread per accepted client | Ch. 26, 27 | ✅ |
| 4.3 | Log `ManagedThreadId`, observe scheduler non-determinism | Ch. 26, 4.4 | ✅ |
| 4.4 | Show shared address space (`static` vs locals) | Ch. 13, 26 | ✅ |
| 4.5 | Reproduce OSTEP `threads.c` race (`counter++`) | Ch. 26, 28 | ✅ |
| 4.6 | Stress thread-per-connection stack limit | Ch. 26, 27 | ✅ |
| M5 | Race lab: fix with `lock` / `Interlocked` | Ch. 28 | ✅ |
| M6 | Bounded worker pool + producer-consumer queue | Ch. 30, 31 | ✅ |
| M7 | Async / event-based server | Ch. 33, 36 | ✅ |
| M8 (6.3) | Bounded queue + 503 backpressure in `WorkerPool` | Ch. 30, 31 | ✅ |
| M9 | Reader-writer lock + process-stats cache | Ch. 31.5 | ✅ |
| M10 | Async mode `--max-threads` / `--min-threads` ThreadPool cap | Ch. 33 | ✅ |
| M11 | Raw `open`/`read`/`close` syscall demo (FileStream + `/read-syscall`) | Ch. 39 | ✅ |
| M12 (12.1–12.4) | Mini FS: superblock, bitmaps, inode table, dir ops, HTTP routes | Ch. 40 | ✅ (slice 12.5 journal deferred) |
| M12 (12.5) | Mini FS journal: TxB/TxE write-ahead log + backing file | Ch. 42 | ✅ |
| M12 (12.6) | Mini FS multi-block transactions: Begin/Append/Commit + Tail pointer | Ch. 42.3 "Batching" | ✅ |
| M12 (12.7) | Mini FS atomic rmdir: empty-only check + Begin/Commit around DirUnlink + Idestroy | Ch. 40.7 (`rmdir`) | ✅ |
| M13 (13.1) | Mini MLFQ: multi-level feedback queue with priority boost | Ch. 8 | ✅ |
| M13 (13.2) | Stride + Lottery proportional-share scheduling (tickets → CPU share) | Ch. 9 (§9.1, §9.3, §9.4, §9.6) | ✅ |
| M13 (13.3) | Multi-CPU scheduling: SQMS vs MQMS vs Work-Stealing + cache affinity | Ch. 10 (§10.3, §10.4, §10.5) | ✅ |
| M14 (14.1) | Mini Pager: linear page table + VA→PA translation + page fault | Ch. 18 | ✅ |
| M16 (16.1) | TLB: per-CPU hardware cache of recent VA→PA translations | Ch. 19 | ✅ |
| M17 (17.1) | Multi-level page table: two-level radix tree (PD/PT) saves memory | Ch. 20 | ✅ |
| M18 (18.1) | Replacement policy: FIFO + LRU + Random on Pager | Ch. 21 + Ch. 22 | ✅ |
| M19 (19.1) | Complete VM: copy-on-write (fork) + swap-out/in (eviction) | Ch. 23 | ✅ |
| M20 (20.1) | Dining philosophers: broken (deadlock) vs fixed (Dijkstra's reverse order) | Ch. 31.6 | ✅ |
| M21 (21.1) | FFS block-group placement: locality policy + large-file exception | Ch. 41 (§41.3, §41.4, §41.6) | ✅ |
| M22 (22.1) | Lock-free CAS primitives: AtomicCounter + LockFreeStack (Treiber) | Ch. 29 §29.1-§29.2 | ✅ |
| M23 (23.1) | Password auth: PBKDF2-HMAC-SHA256 + 16-byte salt + timing-safe Verify + fail-safe defaults | Ch. 53.4, Ch. 54.4, Ch. 56.4 | ✅ |
| M23 (23.2) | At-rest encryption: AES-256-GCM + 12-byte nonce + tamper detection + nonce-reuse demo | Ch. 56.2, Ch. 56.4, Ch. 56.5, Ch. 56.6, Ch. 56.7 | ✅ |
| M23 (23.3) | RBAC + protected route: User/Admin role gate on `/protected/secret` | Ch. 55.6 | ✅ |
| M23 (23.4) | Public-key crypto: RSA-2048 sign/verify + SPKI pubkey distribution | Ch. 56.3, Ch. 56.6, Ch. 57.3 (foreshadow) | ✅ |
| M23 (23.5) | TLS-style handshake: client/server nonces + HKDF-SHA256 session key + encrypt/decrypt | Ch. 57.5 | ✅ |
| M23 (23.6) | TOTP: HMAC-SHA256 + 6-digit truncation + ±1-step window | Ch. 54.5 | ✅ |
| M24 (24.1) | RAID simulator: striping (0) + mirroring (1) + dedicated parity (4) + rotating parity (5) + XOR recovery | Ch. 38 (§38.1-§38.8; §38.4-§38.7 cover the four implemented levels) | ✅ |
| M25 (25.1) | LFS simulator: segments + inode map (imap) + checkpoint region + segment summary block + segment cleaner | Ch. 43 (§43.2, §43.5, §43.6, §43.7, §43.9, §43.10, §43.11) | ✅ |
| M26 (26.1) | SSD simulator: FTL with page-level mapping + log-structured writes + garbage collection + wear tracking + TRIM | Ch. 44 (§44.3, §44.7, §44.8, §44.10, §44.12) | ✅ |
| M27 (27.1) | Data integrity: XOR / additive / Fletcher checksums + physical-ID detection of misdirected writes + write-sequence detection of lost writes + scrubber | Ch. 45 (§45.1, §45.3, §45.4, §45.5, §45.6, §45.7) | ✅ |
| M15 | ArrayPool&lt;byte&gt; in async mode (receive + response buffers) | n/a (perf) | ✅ |

**Milestones**: M1 ✅–M15 ✅, M13.2 ✅, M13.3 ✅, M16 ✅, M17 ✅, M18 ✅, M19 ✅, M20 ✅, M21 ✅, M22 ✅, M23 ✅ (incl. .1 password + .2 at-rest + .3 RBAC + .4 PK + .5 handshake + .6 TOTP), M24 ✅ (RAID 0/1/4/5 with XOR recovery), M25 ✅ (LFS segments + imap + CR + cleaner), M26 ✅ (SSD FTL + GC + wear tracking), M27 ✅ (data integrity: checksums + physical ID + write sequence + scrubber). **Part III Persistence is now closed.** M8–M15 are the post-roadmap extensions per `docs/adr/0004-extend-broad-concurrency-roadmap.md` (M8–M12), `docs/adr/0006-mini-scheduler-mlfq-proportional-multicpu.md` (M13), `docs/adr/0007-mini-pager-mmu-tlb-multilevel-replacement.md` (M14). The original 12-slice roadmap is closed. **Latest**: M27 (data integrity — closes OSEP Ch. 45, the last canonical persistence chapter). See `docs/adr/0008`–`0017` for ADRs. Each yielded a small slice with its own overview + smoke trace in `docs/learning/`.
**Tests**: 42 passing (14 pre-existing + 10 RAID + 6 LFS + 5 SSD + 7 Integrity).
**Code/runtime**: `net10.0`. Two run modes selectable via `--async` flag: default = bounded worker pool (8 threads) + producer/consumer queue, async = `AcceptAsync` + `Task` per connection with `ReceiveAsync` / `SendAsync`. Port 8080, static files under `wwwroot`.
**Latest commit**: M27 (data integrity — closes OSEP Ch. 45, the last canonical persistence chapter). New file `src/MiniWebServer.Host/MiniScheduler/Integrity.cs` (~280 lines): a single `IntegrityStore` simulator + `IntegrityChecksums` static helpers + `BlockIntegrity` metadata struct. The store carries per-block metadata: physical ID (disk + block, OSEP §45.5 — detects misdirected writes), write sequence (monotonic per write, §45.6 — detects lost writes), and three checksums (XOR / additive / Fletcher, §45.3). Reads verify all metadata + recompute all checksums; the scrubber (§45.7) walks every block and reports failures. Fault injection: `InjectCorruption` (flip bits silently), `InjectMisdirectedWrite` (swap physical ID), `InjectLostWrite` (decrement sequence). New HTTP route `/integrity/run?scenario=compute|corrupt|scrub&blocks=N&blockSize=M`. Build clean, 42/42 tests pass (14 pre-existing + 10 RAID + 6 LFS + 5 SSD + 7 Integrity).

## OSTEP Coverage

The book has ~50 chapters. Repo maps **the core three pieces** (Virtualization + Concurrency + Persistence) end-to-end, with detailed §-sub-section citations in each milestone's `overview.md`:

| Piece | Coverage | Roadmap slices |
|---|---|---|
| **Virtualization** | Ch. 4 (§4.1 process, §4.4 states Running/Ready/Blocked); Ch. 6 (§6.1 direct execution, §6.2 syscalls, §6.3 timer interrupt); Ch. 8 (§8.1-§8.5 MLFQ); **Ch. 9 lottery + stride scheduling**; **Ch. 10 multi-CPU scheduling (SQMS / MQMS / work stealing)**; Ch. 13 (address space, implicit); Ch. 18 (linear page table); **Ch. 19 TLB** (§19.1-§19.3 basic + miss handling, §19.5 context switch + ASID, §19.7 MIPS R4000 example with ASID + Global bit — spec-only in M28); **Ch. 20 multi-level page tables**; **Ch. 21 + Ch. 22 replacement policies**; **Ch. 23 complete VM (COW + swapping)**; Ch. 26-27 (thread = point of execution, thread API); Ch. 33 (event-based) | M1, M2, M3, M4, M7, M13, M13.2, M13.3, M14, M16, M17, M18, M19, M28 (spec) |
| **Concurrency** | Ch. 26 (§26.4 figure 26.7 the race); Ch. 27 (thread API); Ch. 28 (§28.1 lock abstraction, §28.7 test-and-set, §28.9 CAS, §28.16 two-phase); Ch. 29 (§29.1 concurrent counters, §29.2 concurrent linked lists — lock-free CAS via `AtomicIncrement` + lock-free list insert); Ch. 30 (§30.1 CV definition + Mesa/Hoare, §30.2 producer/consumer, §30.3 covering conditions); Ch. 31 (§31.4 bounded buffer, §31.5 reader-writer, **§31.6 dining philosophers**); Ch. 32 (§32.2 non-deadlock bugs, **§32.3 deadlock bugs + prevention + Banker's**); Ch. 33 (events) | M4, M5, M6, M7, M8, M9, M10, M20, M22, M29 (spec), M30 (spec) |
| **Persistence** | Ch. 36 (§36.1 system architecture, §36.2 canonical device, §36.3 canonical protocol, §36.4 interrupts, §36.5 DMA, §36.6 PIO vs MMIO — spec-only in M34; TCP receive loop uses kernel async I/O); Ch. 38 (§38.1 interface, §38.2 fault model, §38.3 evaluation axes, **§38.4 RAID 0 striping, §38.5 RAID 1 mirroring, §38.6 RAID 4 dedicated parity, §38.7 RAID 5 rotating parity**, §38.8 comparison summary, §38.9 RAID 6 mention); Ch. 39 (§39.3 open, §39.4 read/write, §39.13 rmdir); Ch. 40 (§40.2 vsfs layout, §40.3 inode, §40.4 directory, §40.5 free space, §40.6 access path, §40.7 caching); **Ch. 41 FFS (§41.3 cylinder/block groups, §41.4 locality policies, §41.6 large-file exception, §41.7 filespan + dirspan metrics)**; Ch. 42.3 (data journaling, recovery, batching, circular log, [Tricky Case: Block Reuse] deferred); **Ch. 43 LFS** (§43.2 write buffering + segments, §43.5 inode map, §43.6 checkpoint region, §43.7 read path, §43.9 garbage collection, §43.10 segment summary block + liveness, §43.11 cleaner policy) **+ §43.3 segment-size math + §43.12 two-CR alternating writes — spec-only in M31**; **Ch. 44 Flash SSDs** (§44.3 raw flash operations, §44.7 log-structured FTL with page-level mapping, §44.8 GC, §44.10 wear leveling, §44.12 TRIM) **+ §44.9 block-level + hybrid FTL — spec-only in M32**; **Ch. 45 Data integrity** (§45.1 LSEs + corruption, §45.3 XOR/additive/Fletcher checksums, §45.4 verify-on-read, §45.5 physical ID, §45.6 write sequence, §45.7 scrubber) **+ §45.8 overheads argument motivating scrubbing schedule — spec-only in M33** | M1, M2, M3, M11, M12.1–12.7, M21, M24, M25, M26, M27, M31 (spec), M32 (spec), M33 (spec), M34 (spec) |
| **Security** | Ch. 53.4 Saltzer-Schroeder; **Ch. 54.5 TOTP / what-you-have** (RFC 6238, HMAC-SHA256, ±1-step window); Ch. 54.4 password storage; Ch. 55.6 RBAC + protected route; Ch. 56.2 AES-256-GCM at-rest; Ch. 56.3 RSA-2048 sign/verify; Ch. 56.4 hashes + integrity; Ch. 56.5 nonce / brute force / key selection; Ch. 56.6 cryptography + OSes; Ch. 56.7 at-rest; **Ch. 57.5 TLS-style handshake** (HKDF-SHA256 session key) — Ch. 53.5 system-call primitives, Ch. 54.6 biometrics, Ch. 54.7 sudo/setuid, Ch. 55 ACLs per file / capabilities / mandatory mode, Ch. 56.3 hybrid encryption, Ch. 57.3 X.509 cert chains, Ch. 57.6 replay protection / SSH / HTTPS, hardware enclaves (TPM) | M23 (.1 password + .2 at-rest + .3 RBAC + .4 PK + .5 handshake + .6 TOTP) |

**Coverage**: 33 of OSEP's ~50 chapters have working code in this repo (Ch. 4, 6, 8-10, 13, 18-23, 26-28, 30-33, 36, 38-45, 53-57). The lab focuses on the **core three pieces** (Virtualization + Concurrency + Persistence) plus Part IV Security; remaining chapters (Ch. 7 process API, Ch. 14-17 base+bound / segmentation, Ch. 24-25, Ch. 29, Ch. 34-35, Ch. 46-52 distributed systems / network files / advanced) are not addressed. §-sub-section coverage noted in each milestone's `overview.md`.

### Per-milestone OSEP attribution (each overview.md has detailed deviations)

- **M1 raw-socket-server** — Ch. 4 §4.1 + §4.4; Ch. 6 §6.1 + §6.2 + §6.3. See `m1-raw-socket-server/overview.md`.
- **M2 http-request** — Ch. 39 (file API); Ch. 4 (process API analog). See `m2-http-request/overview.md`.
- **M3 static-file-server** — Ch. 39 §39.1 + §39.4 + §39.5; Ch. 40 §40.2 (underlying layout). See `m3-static-file-server/overview.md`.
- **M4 thread-per-connection** — Ch. 4 §4.4; Ch. 26 (§26.2 thread creation, §26.3 shared data, §26.4 the race — Figure 26.7, §26.5 atomicity); Ch. 27 (thread API). See `m4-thread-per-connection/overview.md`.
- **M5 race-lab** — Ch. 28 (entire chapter; §28.1 lock abstraction, §28.7 test-and-set, §28.9 CAS). See `m5-race-lab/overview.md`.
- **M6 bounded-worker-pool** — Ch. 28 §28.1-2; Ch. 30 §30.2 producer/consumer; Ch. 31 §31.4 bounded buffer. See `m6-bounded-worker-pool/overview.md`.
- **M7 async-event-based** — Ch. 33 (entire chapter; §33.1 event loop, §33.4 no locks, §33.5 no blocking, §33.7 state mgmt). See `m7-async-event-based/overview.md`.
- **M8 bounded-queue** — Ch. 30 §30.2; Ch. 31 §31.4 (decline vs block). See `m8-bounded-queue/overview.md`.
- **M9 reader-writer-lock** — Ch. 31 §31.5 (reader-writer lock, Figure 31.13). See `m9-reader-writer-lock/overview.md`.
- **M10 threadpool-cap** — Ch. 27 (thread API). See `m10-threadpool-cap/overview.md`.
- **M11 raw-syscall-demo** — Ch. 36 §36.7 device driver abstraction; Ch. 39 §39.3 + §39.4 + §39.7. See `m11-raw-syscall-demo/overview.md`.
- **M12 mini-file-system** — Ch. 39 §39.1 + §39.10 + §39.11 + §39.13; Ch. 40 (§40.2-§40.7); Ch. 42.3 (data journaling + batching + circular log; revoke records deferred). See `m12-mini-file-system/overview.md`.
- **M13 MLFQ** — Ch. 8 (§8.1-§8.5; §8.4 anti-gaming Rule 4 simplified). See `m13-mlfq/overview.md`.
- **M13.2 Stride + Lottery** — Ch. 9 (§9.1 lottery, §9.3 lottery + stride, §9.4 fairness study, §9.6 stride; §9.2 currency/transfer/inflation + §9.7 CFS deferred). See `m13.2-stride-lottery/overview.md`.
- **M14 pager** — Ch. 18 (§18.2-§18.5); Ch. 19 motivation. See `m14-pager/overview.md`.
- **M16 TLB** — Ch. 19 (§19.1 simple example, §19.2 TLB as cache, §19.3 who handles the TLB miss, §19.5 TLB issue: context switch; §19.4 "TLB Contents" + §19.7 MIPS R4000 example with ASID + Global bit deferred to M28 spec-only). See `m16-tlb/overview.md`.
- **M28 ASID (spec-only)** — Ch. 19 §19.5 context-switch ASID + §19.7 MIPS R4000 ASID-tagged TLB + per-ASID flush + Global bit. See `m28-asid/overview.md`.
- **M29 Condition variables (spec-only)** — Ch. 30 §30.1 CV definition + Mesa/Hoare semantics + §30.2 producer/consumer (two CVs) + §30.3 covering conditions. See `m29-cv/overview.md`.
- **M30 Deadlock prevention (spec-only)** — Ch. 32 §32.3 prevention strategies (lock ordering, hold-and-wait prevention, preemption via trylock) + Banker's algorithm + detect-and-recover. See `m30-deadlock/overview.md`.
- **M31 LFS extensions (spec-only)** — Ch. 43 §43.3 segment-size cost model + §43.12 two-CR alternating writes. See `m31-lfs-extensions/overview.md`.
- **M32 Block-level FTL (spec-only)** — Ch. 44 §44.9 block-level + hybrid (log-block) FTL + switch/partial/full merge. See `m32-ssd-extensions/overview.md`.
- **M33 Scrubbing schedule (spec-only)** — Ch. 45 §45.7 periodic scrubber + §45.8 overheads motivating the schedule. See `m33-integrity-extensions/overview.md`.
- **M34 Device drivers (spec-only)** — Ch. 36 §36.2 canonical device + §36.3 canonical protocol + §36.4 interrupts + §36.5 DMA + §36.6 PIO vs MMIO. See `m34-device-drivers/overview.md`.
- **M17 Multi-level page table** — Ch. 20 (§20.1 simple example, §20.2 multi-level, §20.3 more than two levels; §20.4 invert page tables deferred). See `m17-multi-level-pt/overview.md`.
- **M18 Replacement** — Ch. 21 (§21.1 cache memory review, §21.2 average memory access time, §21.3 simple policies: optimal/FIFO/Random/LRU; §21.4 stack-property analysis, §21.5 approximating LRU, §21.6 considering dirty pages deferred). Ch. 22 (§22.1 background, §22.2 segment, §22.3 considering workloads, §22.4 considering write cost deferred — we apply policy at eviction only). See `m18-replacement/overview.md`.
- **M19 Complete VM** — Ch. 23 §23.1 VMS (demand zeroing deferred; COW implemented; segmented FIFO + second-chance list deferred; RSS per process deferred); §23.2 Linux (COW implemented; 2Q, huge pages, 4-level PTs, NX, ASLR, KPTI all deferred; TLB not re-impl'd since M16 already covers it). See `m19-complete-vm/overview.md`.
- **M20 Dining philosophers** — Ch. 31.6 (broken solution = deadlock; Dijkstra's fix = last philosopher reverses order). See `m20-dining-philosophers/overview.md`.
- **M22 Lock-free** — Ch. 29 §29.1 concurrent counters (CAS-based `AtomicIncrement` from the textbook pseudocode) + §29.2 concurrent linked lists (lock-free list insert / Treiber stack). ABA mitigation by never freeing popped nodes; livelock mitigated by `SpinWait`. The previous version of this overview cited Ch. 32 §32.3 — that section is "Deadlock Bugs"; the lock-free material is a one-paragraph aside there pointing to Herlihy. The canonical coverage is Ch. 29. See `m22-lock-free/overview.md`.
- **M21 FFS** — Ch. 41 §41.3 (cylinder/block groups + per-group bitmaps), §41.4 (locality policies: dirs in low-density group + files in parent's group), §41.6 (large-file exception with N-block rotation), §41.7 (filespan + dirspan metrics). §41.7 sub-blocks + parameterized placement deferred. See `m21-ffs/overview.md`.
- **M24 RAID** — Ch. 38 §38.1 (RAID interface), §38.2 (independent failure model — one disk may fail at a time), §38.4 (RAID 0 round-robin striping, no redundancy), §38.5 (RAID 1 full mirroring on 2 disks), §38.6 (RAID 4 dedicated parity disk + XOR recovery via the surviving N-1 blocks + small-write parity-update path), §38.7 (RAID 5 rotating parity, parity for stripe `s` on disk `(s + 1) % N` per figure 38.7). RAID 2 + RAID 3 + RAID 6 are mentioned only in the §38.9 "Other Interesting RAID Issues" paragraph as "Levels 2 and 3 from the original taxonomy" + dual parity; superseded by block-level striping in practice. The simulator models each block as one byte in a `byte[][]` so the XOR is observable in the smoke trace; real RAID stores whole 4 KB blocks. See `m24-raid/overview.md` and `docs/adr/0014-m24-raid.md`.
- **M25 LFS** — Ch. 43 §43.2 (segments — in-memory buffer accumulates updates, flushes to next free disk segment), §43.5 (inode map — `inodeNum → diskAddress` indirection that rides along in each segment write), §43.6 (checkpoint region — fixed slot at segment 0 that points to latest imap piece + log head), §43.7 (read path — name → inodeNum → imap → diskAddr → inode → data block, three indirections), §43.9 (garbage collection — old inodes + data blocks become "dead" on rewrite), §43.10 (segment summary block + liveness check — every flushed block carries its `(inode, offset)` pair; `IsLive` compares against the current imap + inode), §43.11 (cleaner policy — "coldest segment first" simplification of the hot/cold segregation from [RO91]). §43.3 segment-size math + §43.12 two-CR alternating writes + roll-forward crash recovery + §43.13 WAFL/ZFS/btrfs deferred. See `m25-lfs/overview.md` and `docs/adr/0015-m25-lfs.md`.
- **M26 SSD** — Ch. 44 §44.3 (raw flash operations — Read/Program on pages, Erase on whole blocks; page states Invalid/Erased/Valid/Dead), §44.7 (log-structured FTL with page-level mapping table `LBA → physical page`; writes append to next free page in the log block), §44.8 (garbage collection — pick block with most dead pages, migrate live pages to log, erase the block), §44.10 (wear leveling — per-block erase counter; surface as `FormatWearReport`; we don't actively migrate cold data), §44.12 (TRIM — `Trim(lba)` drops the mapping entry, marking the underlying page dead without rewriting). §44.6 direct-mapped FTL (OSEP calls it bad), §44.9 block-level + hybrid mapping, multi-chip parallelism, over-provisioning, active wear leveling, OOB mapping persistence, microsecond timing — all deferred. See `m26-ssd/overview.md` and `docs/adr/0016-m26-ssd.md`.
- **M27 Data integrity** — Ch. 45 §45.1 (failure modes — LSEs + silent corruption; modeled as `InjectCorruption` and `InjectMisdirectedWrite`), §45.3 (three checksum functions: XOR per-byte, additive mod 256, Fletcher s1 + s2 mod 255 — all three computed side-by-side so the trade-offs are visible), §45.4 (verify-on-read — recompute every checksum, compare to stored), §45.5 (physical ID — each block carries `(disk, block)`; misdirected write is detected by ID mismatch), §45.6 (write sequence — monotonic per-block counter; lost write detected by stale sequence), §45.7 (scrubber — walks every block, runs full verification, reports failures). CRC, ZFS-style end-to-end checksum tree, periodic scrubbing schedule, T10 DIF / 520-byte sectors, RAID-DP dual parity — all deferred. **M27 closes the Part III Persistence thread** (Ch. 38 + 39 + 40 + 41 + 42 + 43 + 44 + 45 all have working code in this repo). See `m27-integrity/overview.md` and `docs/adr/0017-m27-integrity.md`.
- **M15 arraypool** — performance only; closest OSEP reference is Ch. 40.7 (caching). See `m15-arraypool/overview.md`.
- **M23 Password auth** — Ch. 53.4 Saltzer-Schroeder fail-safe defaults (identical "invalid credentials" response for both "no such user" and "wrong password"; dummy PBKDF2 when username is missing so the response time doesn't leak it); Ch. 54.4 password storage: PBKDF2-HMAC-SHA256 with 100k iterations + 16-byte random salt + constant-time compare via `CryptographicOperations.FixedTimeEquals`; Ch. 56.4 cryptographic hash foundations (PBKDF2 is built on HMAC-SHA256). **Out of scope**: TLS (Ch. 57), RBAC/ACLs (Ch. 55), MFA, account lockout, persistence. See `m23-auth/overview.md` and `docs/adr/0008-m23-auth-password-hashing.md`.
- **M23.2 At-rest encryption** — Ch. 56.2 symmetric crypto (AES-256 via `System.Security.Cryptography.AesGcm`); Ch. 56.4 cryptographic hashes + integrity (GCM's 128-bit auth tag fails closed); Ch. 56.5 brute force + key selection (never use weak keys; the per-session 256-bit key is `RandomNumberGenerator`-sourced); Ch. 56.6 cryptography + OSes (key lives in process RAM only — a compromised OS reading our key is exactly the threat OSEP §56.6 names); Ch. 56.7 at-rest encryption (the chapter the slice is named after: if the device is stolen, the blocks are useless without the in-memory key). **Out of scope**: TLS handshake (Ch. 57), PK cryptography (Ch. 56.3), TPM-backed keys (Ch. 53 security enclaves), encrypting `minifs.img` blocks at the M12 layer. See `m23-at-rest-encryption/overview.md` and `docs/adr/0009-m23.2-at-rest-encryption.md`.
- **M23.3 RBAC** — Ch. 55.6 RBAC by Ferraiolo & Kuhn [FK92]: two-role (User / Admin) gate with password verify + role check combined in a single `AuthenticateWithRole` call. `/protected/secret` returns 200 + secret body for Admin + correct password, byte-identical 403 for any failure (no info leak). **Out of scope**: per-file ACLs, capabilities, mandatory access control, multi-role switching, sudo for non-humans. See `m23-rbac/overview.md` and `docs/adr/0010-m23.3-rbac.md`.
- **M23.4 PK crypto** — Ch. 56.3 RSA-2048 sign/verify with SPKI/DER public key distribution; Ch. 56.6 key secrecy (private key in process RAM only); Ch. 57.3 foreshadowing — distributed-system PK auth needs X.509 chains we don't model. See `m23-pkcrypto/overview.md` and `docs/adr/0011-m23.4-pkcrypto.md`.
- **M23.5 TLS handshake** — Ch. 57.5 SSL/TLS: client/server nonces + HKDF-SHA256 session key derivation + encrypt/decrypt demo with the M23.2 AEAD. **Out of scope**: ECDH / X25519, X.509 cert chain verification, mutual TLS, perfect forward secrecy, record-layer MACs. We model the key-derivation half of §57.5; the PK-auth-on-handshake half requires M23.4 + a CA story. See `m23-handshake/overview.md` and `docs/adr/0012-m23.5-tls-handshake.md`.
- **M23.6 TOTP** — Ch. 54.5 "what you have": RFC 6238 TOTP with HMAC-SHA256 + 6-digit truncation + 30s step + ±1-step window. **Out of scope**: HOTP (RFC 4226), U2F/FIDO, QR-code enrollment, replay tracking on the verify endpoint, MFA composition with M23.1 (one-line addition to `/auth/login` not made). See `m23-totp/overview.md` and `docs/adr/0013-m23.6-totp.md`.

### Deviations from OSEP (consolidated)

- **MLFQ §8.4 anti-gaming Rule 4**: we implement §8.2 (R4a + R4b with `YieldsEarly` flag) not §8.4 (allotment tracking). Documented in `m13-mlfq/overview.md`.
- **Stride tie-breaking**: Stride's "lowest pass wins" tie is broken by first-found order (deterministic) rather than random — OSEP doesn't specify. Documented in `m13.2-stride-lottery/overview.md`.
- **Multi-CPU §10.1 cache coherence**: jobs are abstract work units; no real MSI/MESI protocol modeled. Documented in `m13.3-multicpu-scheduling/overview.md`.
- **M19 refcount for last-shareer-frees**: COW works for one-shareer (P1 writes → P1 gets private, P2 keeps shared). Multi-shareer (>2 procs sharing a frame) is documented but a per-frame refcount for "free on last unshare" is deferred. Documented in `m19-complete-vm/overview.md`.
- **Dining philosophers we run with thinkMs=0 eatMs=0 to force the deadlock**: in production each philosopher would normally spend real time between cycles, which would let some lucky ones escape the cycle. With thinking time, the deadlock is racy rather than deterministic.
- **Lock-free ABA**: we never free popped stack nodes; they leak. A production lock-free stack would use hazard pointers [Michael 2004] or epoch-based reclamation. Documented in `m22-lock-free/overview.md`.
- **Pager TranslateOutcome naming**: our `PageFault` label conflates OSEP's `SEGMENTATION_FAULT` (no valid PTE) with the literal "page fault" (valid + not present, needs swap). M14.5 will fix. Documented in `m14-pager/overview.md`.
- **TLB global flush on context switch**: M16 implements only the naive "flush everything on context switch" approach. The ASID-tagged-TLB fix (§19.5 context-switch issue + §19.7 MIPS R4000 example with ASID + Global bit) is spec-only in M28. Documented in `m16-tlb/overview.md`.
- **Replacement policy §21.6 dirty bit**: we don't track a dirty bit per PTE for replacement decisions; writes always set `Dirty` but the policy treats it as advisory. Documented in `m18-replacement/overview.md`.
- **Mini FS inode fields**: we use a subset of OSEP §40.3's full inode (no `atime`/`ctime`/`mtime`/`dtime`/protection/blocks-count flags). Documented in `m12-mini-file-system/overview.md`.
- **Mini FS no multi-level index**: file size capped at 12 × 4 KB = 48 KB. OSEP §40.3 indirect / double-indirect pointers deferred.
- **Journaling is data journaling mode** (OSEP §42.3). Ordered/metadata journaling mode deferred.
- **Revoke records** (OSEP §42.3 "Tricky Case: Block Reuse") deferred — our simulator never frees a block during a transaction.
- **M23 password is in the query string**: OSEP §57.4 says encrypting the password in transit is mandatory for real systems; we don't have TLS. Real systems put credentials in the POST body.
- **M23 no rate-limiting / account lockout** (OSEP §54.4): the slow PBKDF2 hash is the only speed bump. No throttling per username.
- **M23 no persistence**: users live in `ConcurrentDictionary`. Server restart drops every user.
- **M23 timing-safe compare is at the verify step only**: we use `CryptographicOperations.FixedTimeEquals`. We don't defend against timing leaks in `String.Equals` style username comparisons (usernames are public-ish in this design).
- **M23.2 key in process memory only**: OSEP §56.6 explicitly warns that an OS reading its own key makes "the cryptography useless". We don't have a TPM or kernel keyring; the key is in the host process's RAM. Same trade-off as OSEP §56.7's "compromise between usability and security ... remember[ing] the key after first entry for a significant period of time, but only keeping it in RAM".
- **M23.2 `SymmetricCipher.EncryptWithFixedNonce` is the one intentionally-bad primitive**: a separate method name so it can never be called by accident. Only `/crypto/nonce-reuse-demo` uses it; the route's purpose is to *show why* nonce reuse is fatal. Real code never calls it.
- **M23.2 doesn't encrypt `minifs.img`**: M12 still writes plaintext blocks to disk; M23.2 only protects in-memory named slots. A future milestone could replace M12's block writes with `SymmetricCipher.Encrypt`, but that's its own piece of work.
- **M23.3 grant isn't auth-gated**: anyone can call `/auth/grant?user=X&role=admin`. Real systems require an already-Admin caller to grant.
- **M23.4 single in-memory keypair**: no per-user keys. A multi-tenant system would have a public-key registry indexed by URL / username.
- **M23.5 no PK-auth on handshake**: the §57.3 step (verify the server certificate) is missing. We skip the trust-at-handshake half; the post-handshake symmetric traffic still uses a fresh per-session key.
- **M23.6 no replay tracking**: an attacker who captured a code within the 30s + 1-step window could replay it. Real TOTP services track which (code, step) pairs have already been accepted.

Chapters **not yet implemented** (natural next slices):

- **Part I Virtualization**: Ch. 7 process API (out of scope for .NET), Ch. 14-17 base+bound / segmentation / free-space mgmt (superseded by paging), §19.5+§19.7 ASID (spec-only in M28), §20.4 inverted page tables, §21.4-§21.6 stack property + approximated LRU + dirty pages, §23.1 VMS demand-zeroing + RSS + segmented FIFO + second-chance list, §23.2 Linux 2Q + huge pages + 4-level PTs + NX + ASLR + KPTI
- **Part II Concurrency**: Ch. 30 §30.3 covering conditions + broadcast (spec-only in M29); Ch. 32.2 atomicity/order bugs (CV fixes for the original cases — these are the example bugs OSEP §32.2 cites; M29 covers the CV primitive but doesn't currently extend M23 with atomicity/order-bug fixes); Ch. 32.3 deadlock prevention/avoidance (lock ordering, hold-and-wait, Banker's — spec-only in M30); Ch. 29 deeper lock-free data structures beyond §29.1-§29.2 (DFTL, Michael-Scott queue, hazard pointers) deferred.
- **Part III Persistence**: Ch. 36 §36.7-§36.10 device-driver abstraction + case study + historical notes (M34 spec covers §36.2-§36.6 only); Ch. 43 §43.3 segment-size cost model + §43.12 two-CR alternating writes (spec-only in M31); Ch. 44 §44.9 block-level + hybrid FTL (spec-only in M32); Ch. 45 §45.7 periodic scrubbing schedule (spec-only in M33); §45.8 ZFS-style end-to-end checksum tree (M27 already deferred). The non-spec-only Ch. 38-45 coverage is closed: M12 covers Ch. 40 vsfs + Ch. 42.3 journaling; M21 covers Ch. 41 FFS placement; M24 covers Ch. 38 RAID levels 0/1/4/5; M25 covers Ch. 43 LFS segments + imap + CR + cleaner; M26 covers Ch. 44 SSD FTL + GC + wear tracking; M27 covers Ch. 45 data integrity (checksums + physical ID + write sequence + scrubber). **Part III Persistence is closed (modulo the M31-M34 spec-only slices).**
- **Part IV Security**: Ch. 53 §53.5 (system calls + access control primitives), Ch. 54 §54.6 (biometrics) + §54.7 (non-human auth: sudo, setuid), Ch. 55 in depth (ACLs per file, capabilities, mandatory vs discretionary, Android permission model), Ch. 56.3 hybrid encryption (sign-then-encrypt example), Ch. 57 entire except §57.5 key-derivation (X.509 chains, MITM, SSH, HTTPS), key-revocation (§57.6), hardware enclaves (§53 TPM) — M23.1 (password) + M23.2 (at-rest) + M23.3 (RBAC) + M23.4 (PK sign/verify) + M23.5 (TLS-style handshake key derivation) + M23.6 (TOTP) cover most of the canonical Part IV content; remaining is mostly operational/extended PK + biometrics + sudo-equivalent.

The repo is best understood as an **OS concepts lab for the core three pieces**, not a full reproduction of the textbook. Concurrency sweep: race observable → race fixed → pool → async → dining philosophers → lock-free CAS. Persistence sweep: raw I/O → vsfs + journaling → FFS placement → RAID 0/1/4/5 → LFS segments + cleaner → SSD FTL + GC + wear → checksums + physical ID + scrubber. Virtualization sweep: thread/process abstraction → MLFQ → proportional-share → multi-CPU → linear paging → TLB → multi-level page tables → replacement policy → COW fork + swap.

## Purpose

Mini Web Server is a learning-oriented .NET console application that demonstrates how a minimal HTTP server works directly on top of TCP sockets. It does not use ASP.NET Core or `HttpListener`; the point is to expose the operating-system-level network steps: create a socket, bind it to a port, listen, accept a client connection, read raw bytes, write a raw HTTP response, and close the connection.

## Current Shape

The repository is a single-context .NET solution.

- `MiniWebServer.sln` contains one host project.
- `src/MiniWebServer.Host/MiniWebServer.Host.csproj` builds a `net10.0` executable.
- `src/MiniWebServer.Host/Program.cs` is the startup. Branches on `--async` CLI flag; default starts the bounded worker-pool server; `--async` starts the event-based server. Accepts and parses `--async`.
- `src/MiniWebServer.Host/WorkerPool.cs` is the bounded pool (Phase 3, default mode). Queue + lock + `Monitor.Wait`/`Monitor.Pulse` + 8 worker threads. Accept loop just `Enqueue`s.
- `src/MiniWebServer.Host/AsyncServer.cs` is the event-based server (Phase 4, `--async` mode). Single-threaded `AcceptAsync` loop + `Task` per connection with `ReceiveAsync` / `SendAsync`.
- `src/MiniWebServer.Host/HttpRequest.cs` models the parsed request line and headers.
- `src/MiniWebServer.Host/HttpRequestParser.cs` parses raw HTTP request text.
- `src/MiniWebServer.Host/HttpRequestReceiver.cs` is the pure helper for header-terminator + Content-Length detection used by both modes.
- `src/MiniWebServer.Host/HttpResponse.cs` formats raw HTTP response bytes.
- `src/MiniWebServer.Host/StaticFileResponder.cs` maps parsed request paths to files under `wwwroot`.
- `src/MiniWebServer.Host/WebRootLocator.cs` locates the runtime `wwwroot` directory.
- `src/MiniWebServer.Host/ServerConfig.cs` holds the `MaxRequestBytes` and `RaceIterations` constants shared by both server modes.
- `src/MiniWebServer.Host/RequestStats` (nested inside Program.cs) holds `TotalRequests` (atomic), `UnsafeCounter` (non-atomic race demo), `SafeCounter` + `SafeCounterLock` (lock-fixed demo).
- `src/MiniWebServer.Host/MiniAuth/PasswordHasher.cs` (M23) wraps PBKDF2-HMAC-SHA256 with a 100k-iteration budget, 16-byte salt, and `CryptographicOperations.FixedTimeEquals` for constant-time compare. Sibling file `UserStore.cs` keeps the in-memory `ConcurrentDictionary<string, AuthUser>` and threads the dummy-PBKDF2 "no user" path so the response time can't leak username validity. Both are wired into `Program.cs` under `else if (parsedRequest.Path.StartsWith("/auth/"))`.
- `src/MiniWebServer.Host/MiniCrypto/SymmetricCipher.cs` (M23.2) wraps `System.Security.Cryptography.AesGcm` with 32-byte (256-bit) keys, 12-byte (96-bit) randomly-generated nonces, 16-byte (128-bit) auth tags; `EncryptWithFixedNonce` is the one intentionally-bad primitive exposed solely for the nonce-reuse demo. Sibling file `AtRestStore.cs` keeps the in-memory `ConcurrentDictionary<string, Slot>` storing only `{nonce, ciphertext, tag}` and threads the auth-tag-failure decryption through the route's `try / catch` so a tamper attempt returns 401 rather than corrupting a slot. Both are wired into `Program.cs` under `else if (parsedRequest.Path.StartsWith("/crypto/"))`.
- `src/MiniWebServer.Host/MiniCrypto/RsaSigner.cs` (M23.4) wraps `System.Security.Cryptography.RSA.Create(2048)` with SHA-256 + PSS; `SignData` + `VerifyData`. Private key kept in process memory; public key exported as SPKI/DER bytes via `ExportSubjectPublicKeyInfo()`.
- `src/MiniWebServer.Host/MiniCrypto/Handshake.cs` (M23.5) implements a clean-room RFC 5869 HKDF-SHA256 (Extract+Expand) plus a one-shot `RunDemo()` that generates two nonces + derives a 32-byte session key + feeds it to M23.2's AEAD for a round-trip demo.
- `src/MiniWebServer.Host/MiniCrypto/Totp.cs` (M23.6) implements RFC 6238 TOTP with HMAC-SHA256 (instead of the historical SHA-1), 6-digit truncation via dynamic-offset, 30-second step, ±1 step verification window, plus RFC 4648 base32 encoder for enrollment strings.
- `src/MiniWebServer.Host/MiniScheduler/Raid.cs` (M24) is the RAID simulator: a single `Raid` class with the four canonical levels (RAID 0 round-robin striping per §38.4, RAID 1 full mirroring per §38.5, RAID 4 dedicated parity per §38.6, RAID 5 rotating parity per §38.7 figure 38.7). Single-disk failure model per §38.2; XOR recovery per §38.6 — `lostBlock = ⊕ of surviving N-1 blocks in the stripe`. Each block is modelled as one byte so the XOR is observable in the smoke trace; real RAID stores whole 4 KB blocks.
- `src/MiniWebServer.Host/MiniScheduler/Lfs.cs` (M25) is the LFS simulator: a single `Lfs` class implementing the full write path (in-memory segment buffer + flush to next free disk segment) and read path (CR → imap → inode → data block per OSEP §43.7). Segment summary block (§43.10) records `(inode, offset)` per flushed block so `IsLive(seg, slot)` can compare against the current imap + inode pointer to detect dead blocks. Cleaner (§43.9 + §43.11) picks the coldest segment (fewest live blocks), compacts live blocks into a new segment, frees the old one. Each block is modelled as one byte; real LFS stores whole 4 KB blocks.
- `src/MiniWebServer.Host/MiniScheduler/Ssd.cs` (M26) is the flash SSD simulator: a single `Ssd` class modeling raw flash (blocks of pages with `Erased`/`Valid`/`Dead` states per OSEP §44.3) + a log-structured FTL with page-level mapping table (§44.7) + garbage collection that picks the block with the most dead pages, migrates live pages to the log, and erases the block (§44.8) + per-block wear counter (§44.10) + `Trim(lba)` (§44.12). Client surface (`Read` / `Write` / `Trim`) matches the disk interface — same shape a real SSD exposes. Each page is one byte; real flash pages are 4 KB+.
- `src/MiniWebServer.Host/MiniScheduler/Integrity.cs` (M27) is the data-integrity simulator: a single `IntegrityStore` class + `IntegrityChecksums` static helpers (XOR / additive / Fletcher) + `BlockIntegrity` metadata struct. Each block carries a physical ID (OSEP §45.5), a monotonic write sequence (OSEP §45.6), and three checksums. Reads verify all metadata + recompute all checksums; the scrubber (§45.7) walks every block and reports failures. Fault injection: `InjectCorruption` (silent bit flip), `InjectMisdirectedWrite` (swap physical ID), `InjectLostWrite` (decrement sequence). **M27 closes Part III Persistence.**
- `src/MiniWebServer.Host/wwwroot/index.html` is the default static page for `/` and is copied to the build output.
- `tests/MiniWebServer.Host.Tests/` contains console-based parser tests.

`docs/adr/0001-build-first-ostep-learning-design.md` records the accepted build-first OSTEP learning direction and milestone roadmap.

`docs/adr/0003-four-phase-ostep-roadmap.md` records the accepted four-phase OSEP learning roadmap:

1. Phase 1: The Process & The Byte Stream.
2. Phase 2: Threads: Multiple Points of Execution.
3. Phase 3: Bounded Concurrency.
4. Phase 4: Event-Based Concurrency.

All four phases are complete; see the **OSEP Coverage** section above for what this maps to in the textbook.

## Glossary

This glossary defines the canonical vocabulary used across `CONTEXT.md`, `docs/adr/`, and `docs/learning/`. Each term's `_Avoid_` line lists synonyms that should NOT be used in place of the canonical term. Where a runtime implementation is relevant (e.g. specific BCL types), it is noted after the definition with "Impl:" — these are illustrative, not exhaustive. Implementation details that change frequently belong in code, not here.

### Networking (M1, M2, M3)

- **Host**: The executable process that owns the socket server and runs continuously until stopped.
  _Avoid_: process, server process, application
- **Server socket**: The TCP listening socket that the host binds, listens on, and accepts client connections from.
  _Avoid_: listener, listening socket (when referring to the same object), TCP socket
- **Endpoint**: The address-and-port pair the server socket binds to.
  _Avoid_: bind address, listen address
- **Listen backlog**: The maximum queue length for pending (not-yet-accepted) connections.
  _Avoid_: accept queue, kernel queue (in this context)
- **Client socket**: A per-connection socket returned by accepting one pending connection.
  _Avoid_: connection, accepted socket (redundant), TCP socket
- **Raw HTTP request**: Bytes received from a client socket and decoded as UTF-8 for console logging.
  _Avoid_: HTTP bytes, request bytes
- **Parsed HTTP request**: A structured view of the raw request text containing method, path, version, and headers.
  _Avoid_: request object, HTTP request (when distinguishing raw from parsed)
- **Request line**: The first line of an HTTP request, such as `GET /ostep HTTP/1.1`.
- **Header**: A `Name: Value` line after the request line, parsed into the request header dictionary.
- **Raw HTTP response**: A manually formatted HTTP/1.1 response string sent over the client socket.
- **Static file response**: A response generated by reading bytes from a file under `wwwroot`.
- **Web root**: The directory files can be served from. The source web root is `src/MiniWebServer.Host/wwwroot`; at runtime it is copied beside the host executable.
  _Avoid_: document root, content root
- **Path traversal**: A request path such as `/../CONTEXT.md` that tries to escape the web root. These requests return `404 Not Found`.
  _Avoid_: directory traversal, ../ escape (informal)
- **Connection close**: The server closes the client socket after sending the response.
  _Avoid_: socket close (in this HTTP/1.0-style flow)

### OS primitives (M1, M4, M5, M11)

- **Syscall**: A controlled entry into the kernel that a user-mode process invokes to request an OS service (I/O, process, memory). The kernel enforces privilege; the call is the seam between user and kernel mode.
  _Avoid_: system call (the same concept, but write as two words only when quoting OSEP verbatim), kernel call
- **Kernel mode / User mode**: Two CPU execution privilege levels. Kernel mode can execute any instruction and access any memory; user mode is restricted and must trap into the kernel via a syscall to touch protected resources.
- **Process states** (Running / Ready / Blocked): The three high-level states a process can be in. Running holds the CPU; Ready is runnable but waiting; Blocked is waiting on I/O or an event.
  _Avoid_: R, R-, B (single-letter labels) outside code
- **Scheduler**: The OS component that picks the next runnable process/thread to dispatch on a CPU.
  _Avoid_: dispatcher (in this lab — pick one)
- **Context switch**: The act of saving one thread's CPU state and loading another's. The seam between scheduler decisions and observed execution.
  _Avoid_: task switch
- **Race condition**: A correctness outcome that depends on the interleaving of concurrent operations on shared state. Observable as non-determinism in M4.5 / M5.
  _Avoid_: race (alone, ambiguous), data race (use only for the lower-level memory-model concept)
- **Critical section**: A block of code that accesses shared state and must run atomically with respect to other critical sections on the same state.
  _Avoid_: critical region, atomic block (overloaded)
- **Condition variable**: A synchronization primitive that lets a thread atomically release a lock and block until another thread signals it. Used to implement the producer/consumer queue in the worker pool.
  _Avoid_: condvar, condition (alone)
- **Bounded queue**: A producer/consumer queue with a fixed capacity. Producers block (or get backpressure) when full; consumers block when empty.
  _Avoid_: blocking queue (overloaded with BCL type), limited queue
- **Backpressure**: The signal a bounded queue sends to its producer when full — block, drop, or return a 503 — so the producer slows down instead of growing memory.
  _Avoid_: flow control (in this OS-queue sense)
- **Semaphore**: A synchronization primitive with a non-negative integer counter. `Wait` decrements (blocks at 0); `Release` increments and wakes one waiter. Used for resource-cap counting and dining-philosophers forks.
  _Avoid_: mutex (different — semaphore ≥1 is not exclusive)
- **Reader-writer lock**: A lock that allows concurrent readers OR a single writer, never both. Improves throughput on read-heavy shared state.
  _Avoid_: RW lock, shared-exclusive lock
- **Worker pool**: A fixed set of long-lived worker threads that dequeue work items (here, client sockets) from a shared queue and run them.
  _Avoid_: thread pool (capitalised — that name belongs to the runtime concept below), worker thread pool
- **ThreadPool** (runtime): The .NET runtime-managed pool of OS threads that backs `Task`, async I/O, and timer callbacks. Distinct from this lab's hand-rolled `WorkerPool`.
  _Avoid_: thread pool (use lowercase only when speaking generically; capitalised for the .NET type)

### Pager / VM (M14, M16–M19)

- **Virtual address (VA)**: An address as seen by user-mode code, split into a virtual page number (VPN) and an offset.
- **Physical address (PA) / Physical frame**: A real RAM location, addressed by frame number + offset. A frame is one page-sized chunk of physical memory.
- **Page table**: The per-process data structure that translates VPN → frame number. A linear page table has one entry per VPN; a multi-level page table is a radix tree (PD/PT).
- **Page table entry (PTE)**: One row of the page table. Holds the frame number plus bits like Valid, Dirty, Reference.
- **Page fault**: A trap raised when a translation fails (no valid PTE) or when a valid PTE marks the page not-present. The pager resolves it by mapping a frame or swapping in.
  _Avoid_: SEGFAULT (that's the OS-level signal name for the unrecoverable case; "page fault" is the recoverable case)
- **TLB** (Translation Lookaside Buffer): A small per-CPU hardware cache of recent VA→PA translations. A TLB miss falls through to the page-table walk.
- **Replacement policy**: The algorithm that picks which frame to evict when no free frame is available. Options: FIFO, LRU, Random.
- **Copy-on-write (COW)**: A fork optimisation. Parent and child share frames read-only; the writer (whoever touches first) gets a private copy.
- **Multi-level page table**: A page table as a radix tree of page directories + page tables. Saves memory when the address space is sparse.
- **Address translation**: The act of converting a virtual address into a physical address, via the page table (with a TLB cache in front).
  _Avoid_: VA→PA mapping (when speaking about the act rather than the table)
- **VMS** (Virtual Memory System, OSEP §23.1): a paging policy with a working-set model; segments FIFO + second-chance list; the historical baseline against which Linux §23.2 is compared.
- **Swap / Swapping** (M19, OSEP §23.1): the act of evicting a page to disk to make room for a different page. The pager writes the victim frame to a swap device and marks its PTE not-present; the next reference triggers swap-in.
  _Avoid_: page out (informal)
- **Demand zeroing** (OSEP §23.1): a VMS optimisation where newly mapped pages are marked not-present + zero-on-demand. M19 does not implement this; new mappings are simply zero-filled eagerly.
- **Working set** (OSEP §23.1): the set of pages a process has referenced recently. Determines how much memory to give a process. The M19 simulator uses uniform eviction regardless of working-set size.

### Scheduling (M13, M13.2, M13.3)

- **SQMS** (Single-Queue Multiprocessor Scheduling, OSEP §10.4): one shared ready queue for all CPUs. Simple but suffers from cache-line contention.
- **MQMS** (Multi-Queue Multiprocessor Scheduling, OSEP §10.5): one ready queue per CPU. Better cache behaviour but can leave one CPU idle while another is overloaded.
- **Work-stealing** (OSEP §10.5): a load-balancing scheme where an idle CPU pulls a job from a busy CPU's queue. MQMS + work-stealing is the standard Linux approach.
- **Cache affinity** (OSEP §10.5): a scheduler's preference for keeping a thread on the CPU it last ran on, to preserve warm cache lines.

### Concurrency deep-dive (M22, M28, M29, M30)

- **ASID** (Address Space Identifier, M28, OSEP §19.5 + §19.7): a small integer tag (typically 8 bits, matching MIPS R4000 [H93]) added to each TLB entry identifying which address space the entry belongs to. Introduced in OSEP §19.5 as the solution to the context-switch TLB-flush problem; §19.7 makes the abstract idea concrete with the MIPS R4000 entry format (which has both ASID field and a Global bit). Replaces the per-context-switch `FlushTlb()` with a per-ASID flush; entries for other ASIDs (or kernel global entries) survive.
  _Avoid_: PID (PID is 32 bits on Linux; ASID is the smaller TLB-tag-sized equivalent)
- **Global bit / G bit** (M28, OSEP §19.7): the TLB-entry flag that, when set, makes the entry match any ASID. Used for kernel mappings that live in every address space.
- **Mesa vs Hoare semantics** (M29, OSEP §30.1): two flavours of CV wakeup. Mesa (the C# `Monitor` default) signals as a hint — the waker keeps the lock until it leaves the critical section; the waiter doesn't immediately run. Hoare semantics transfer the lock atomically. Almost every system uses Mesa.
- **Spurious wakeup** (M29, OSEP §30.2 TIP): a wakeup from a CV that has no producer-side reason. POSIX allows them; C# `Monitor.Wait` can return spuriously. The defensive idiom is `while (!predicate) { lockObj.Wait(); }` — never `if (!predicate)`.
- **Covering condition** (M29, OSEP §30.3): a CV on which `Broadcast()` is used to wake all waiters because the waker can't tell which one can make progress. The defensive `while` loop on each waiter re-checks and re-sleeps the ones that aren't ready.
- **Deadlock** (M30, OSEP §32.3 [C+71]): a state where N threads each hold some locks and wait for others to release theirs, forming a cycle. No thread can make progress.
- **Coffman conditions** (M30, OSEP §32.3 [C+71]): the four preconditions for deadlock — mutual exclusion, hold-and-wait, no preemption, circular wait. Removing any one breaks deadlock.
- **Lock ordering** (M30, OSEP §32.3 prevention): assigning each lock a global numeric ID and requiring every thread to acquire locks in ascending ID order. Breaks circular wait. OSEP §32.3 cites the Linux mm/filemap.c [T+94] file as a real example with 10 partial orderings. The OSEP §32.3 TIP shows the canonical implementation using lock *address* order.
- **Atomic batch acquire / `AcquireAll`** (M30, OSEP §32.3 prevention): holding a meta-monitor while acquiring every lock a thread needs before releasing any. Breaks hold-and-wait.
- **Preemption via trylock + backoff** (M30, OSEP §32.3 prevention): try-lock + release-all-on-timeout (using `pthread_mutex_trylock` in POSIX or `Monitor.TryEnter` in .NET). Breaks no-preemption. OSEP explicitly notes this "doesn't really add preemption" but uses trylock to allow self-preemption; livelock is the new risk.
- **Banker's algorithm** (M30, OSEP §32.3 avoidance [D64]): a runtime safety check that admits a resource request only if the resulting state still has a safe sequence (every thread can complete). Requires every thread to declare its maximum resource needs up front. The classic avoidance algorithm; rarely used in practice because real systems don't know max needs.
  _Avoid_: Banker (without 's algorithm')

### Storage / Mini FS (M11, M12, M21)

- **Inode**: An on-disk index node. Holds a file's metadata (size, block pointers) and is addressed by an inode number.
  _Avoid_: i-node (hyphenated form), file metadata record
- **Superblock**: The on-disk structure that describes the filesystem layout (block size, total blocks, free-block count, inode count).
- **Bitmap**: A bit array where each bit tracks whether the corresponding resource (block or inode) is free (0) or allocated (1).
- **Journal / Write-ahead log**: An append-only log of pending block writes. After a crash, the recovery code replays committed transactions and skips uncommitted ones.
  _Avoid_: WAL (use only in code or in headline where the acronym is conventional)
- **Mini FS (minifs)**: This lab's hand-rolled teaching filesystem. In-memory + optional `minifs.img` backing file.
- **Cylinder group / Block group** (M21, OSEP §41.3): FFS's unit of disk organisation — the disk is divided into N groups so that related files can be placed close together, reducing seek distance. FFS's actual unit is the cylinder (a geometric construct), which the simulator approximates as a linear block group.
  _Avoid_: cylinder (when not citing the OSEP geometric model)
- **Filespan** (M21, OSEP §41.7): the max distance between any two data blocks of a file (or between the inode and any data block). Lower is better; FFS minimises it by locality.
- **Dirspan** (M21, OSEP §41.7): the max distance between a directory's inode + data and the inodes + data of all its files. Lower is better; FFS minimises it by placing new files in the same group as their parent directory.
- **Large-file exception** (M21, OSEP §41.6): the FFS rule that files larger than `N` blocks (default 12) rotate to a new block group every N blocks, so they don't fill any one group.

### RAID (M24)

- **RAID**: Redundant Array of Inexpensive Disks (OSEP §38). A layer below the filesystem that stripes, mirrors, or XOR-parities data across N physical disks so a single disk failure doesn't lose data. The filesystem sees a flat block array (OSEP §38.1); the RAID layout is invisible above it.
- **Stripe unit / chunk size** (M24, OSEP §38.4): the amount of contiguous data placed on one disk before moving on to the next. OSEP calls this "chunk size"; the chapter example uses 4 KB (one block) as the chunk size but notes "Most arrays use larger chunk sizes (e.g., 64 KB)". We model it as one block; real RAID uses larger chunk sizes for sequential workloads.
- **Stripe**: The set of blocks that share the same offset across N disks. In RAID 4/5 the stripe includes one parity block; in RAID 1 the stripe is two mirrors of the same logical block.
- **Parity**: A block computed as the XOR of the other blocks in the same stripe (OSEP §38.6, where the XOR "fundamental insight" is introduced). Storing one extra parity block per stripe lets the array recover from any single disk loss: the lost block equals the XOR of the surviving N-1 blocks.
- **Disk failure model** (OSEP §38.2): The textbook assumption that *any one* of the N disks may fail at a time. MTTF of an N-disk array is roughly MTTF of one disk divided by N. Recovery via XOR satisfies this single-failure-tolerance constraint.
- **RAID 0** (OSEP §38.4): Block-level striping, no redundancy. N disks = N× bandwidth but N× failure rate.
- **RAID 1** (OSEP §38.5): Full mirroring. Two disks hold identical copies of every block. Writes double, reads can pick either copy.
- **RAID 4** (OSEP §38.6): Block-level striping + one dedicated parity disk. Parity disk is the write bottleneck.
- **RAID 5** (OSEP §38.7): Block-level striping + rotating parity. No single disk is the bottleneck — parity writes spread across the array. The figure-38.7 convention `parityDiskFor(stripe) = (stripe + 1) % N` is the canonical layout (per OSEP §38.7 figure 38.7 "RAID-5 With Rotated Parity").
- **Rotating parity**: The RAID 5 pattern of placing parity stripe `s` on disk `(s + 1) % N` so every disk plays both data and parity roles across the array.

### LFS (M25)

- **LFS** (Log-Structured File System): a filesystem that turns all writes into sequential I/O by buffering updates in memory and flushing them in large segments to unused disk locations (OSEP §43). Reads still go through direct pointers; the trick is making the *write* path sequential so the disk's transfer bandwidth isn't wasted on seeks.
- **Segment**: the unit of LFS I/O — a fixed-size batch of updates (data blocks, inodes, imap pieces) flushed to disk as one contiguous write (OSEP §43.2). Real LFS uses MB-scale segments; the M25 simulator uses 4–8 block segments.
- **Inode map (imap)**: the `inodeNum → diskAddress` indirection that LFS uses to find inodes scattered across the disk (OSEP §43.5). Pieces of the imap ride along in each segment flush; the checkpoint region points to the latest imap piece.
- **Checkpoint region (CR)**: a fixed known location on disk (segment 0 in the M25 simulator) that holds pointers to the latest imap pieces + log head (OSEP §43.6). The CR is the "anchor" for LFS — without it the filesystem can't find any of its imap chunks.
- **Segment summary block**: a header at the start of each segment that records `(inodeNum, offset)` for every block in the segment (OSEP §43.10). The cleaner uses the summary to determine which blocks are live and which are dead.
- **Block liveness**: a block at `(segment, slot)` is live if the segment summary's `(inode, offset)` matches the current inode's offset-pointer for that file. Otherwise it's dead — eligible for cleaning.
- **Garbage / dead blocks**: old inodes + old data blocks left behind after a rewrite. The cleaner reclaims them.
- **Segment cleaner**: the background process that picks cold segments (fewest live blocks), compacts the live blocks into a new segment, and frees the old segment (OSEP §43.9 + §43.11).
- **Recursive update problem**: the issue that without indirection, updating an inode would force an update to its parent directory, which forces an update to *its* parent, all the way to the root (OSEP §43.8). LFS solves this with the imap: a directory still points to the same inode number even after the inode moves on disk.
- **Hot/cold segregation** (OSEP §43.11, [RO91]): the cleaning policy of distinguishing hot segments (frequently-overwritten — wait before cleaning) from cold segments (stable — clean sooner). The M25 simulator uses the simpler "coldest segment first" policy.
- **Segment-size cost model** (M31, OSEP §43.3): the rule that the minimum segment size to achieve a fraction `F` of peak bandwidth is `D = (F / (1-F)) × R_peak × T_position` (equation 43.6). With T_position = 10 ms, R_peak = 100 MB/s, F = 0.9 → D = 9 MB. The slice reproduces this arithmetic as `SegmentSizer.OptimalBytes(peakBandwidth, positionTime, fraction)`.
- **Write amplification** (M31, M26): ratio of physical disk writes to logical LFS / SSD writes. The cleaner + segment-size choice both drive it.
- **Two-CR alternating writes** (M31, OSEP §43.12 [R92]): the recovery technique where two checkpoint regions (CR0 + CR1) sit at fixed offsets on disk; the writer alternates on each flush using the three-block protocol (header with timestamp, then body, then trailer with timestamp). Recovery reads both, takes the one with matching header/trailer timestamps as the live state. Avoids the deadlock where the writer can't update the CR because the segment it would have used is being flushed.

### SSD / Flash (M26)

- **Flash page**: the smallest unit of flash I/O — typically 4 KB (OSEP §44.2). The M26 simulator models one-byte pages so the state transitions are visible in the smoke trace.
- **Flash block / erase block**: the unit of flash erase — typically 128 KB or 256 KB, containing many pages (OSEP §44.2). Erasing a block is expensive (~ms); programming a page is cheaper (~100s of µs); reading a page is fastest (~10s of µs).
- **Page states** (OSEP §44.3): `INVALID → ERASED → VALID`. The M26 simulator adds a fourth logical state, `DEAD`, meaning the page's data has been overwritten elsewhere and is eligible for GC.
- **FTL** (Flash Translation Layer): the firmware inside an SSD that turns client `read(LBA)` / `write(LBA)` calls into the underlying read/program/erase sequence (OSEP §44.5). The FTL keeps a mapping table `LBA → physical page` in memory.
- **Log-structured FTL** (OSEP §44.7): the standard FTL approach — writes append to the next free page in a "log block"; the mapping table redirects the LBA to wherever the data ended up. Avoids the read-modify-write cost of direct-mapped FTLs and spreads write wear across blocks.
- **Mapping table**: `LBA → physical page` index (OSEP §44.7). Page-level mapping (one entry per page) is the most flexible but uses the most memory — a 1 TB SSD with 4 KB pages needs 1 GB of mapping memory (OSEP §44.9).
- **Garbage collection** (OSEP §44.8): the background process that reclaims dead pages. Picks a block with many dead pages, migrates the live pages to the log, and erases the block so it can be reused. Garbage collection drives write amplification when too aggressive.
- **Write amplification**: ratio of physical writes (to flash) to logical writes (from the client). A naïve GC scheme can write each logical byte 2–4 times.
- **Wear leveling** (OSEP §44.10): the FTL's responsibility to spread erase cycles evenly across all blocks so no single block wears out before the others. The log-structured design + GC do most of this for free; active wear leveling also migrates cold data.
- **TRIM** (OSEP §44.12): a hint the filesystem sends to the SSD when a file is deleted — "you can forget about this LBA range". Without TRIM, the SSD keeps old data around as "garbage" because it can't tell which pages are still logically in use.
- **Over-provisioning**: the practice of reserving some flash capacity (typically 7–28%) for GC headroom, hidden from the client. Real SSDs do this; the M26 simulator does not.
- **Page-level FTL** (M26, OSEP §44.7): every LBA maps to one physical page; the mapping table has one entry per page. Maximum flexibility, highest RAM cost (~1 GB / TB at 4 KB pages; OSEP §44.9 calls this "impractical for 1 TB devices").
- **Block-level FTL** (M32, OSEP §44.9 [KK+02]): every logical block maps to one physical block; the page offset is preserved. The mapping table is `1 / pagesPerBlock` the size of page-level. Writes that don't fill a block go through a small block-level log buffer.
- **Hybrid (log-block) FTL** (M32, OSEP §44.9 [KK+02]): the FTL keeps a few erased **log blocks** with per-page mappings, plus per-block mappings for the rest. Implements switch merge (best case, no extra I/O), partial merge (1 extra read per merged page), and full merge (drives up write amplification).
- **DFTL** (Demand-based FTL, [GP07], deferred): a refinement that page-maps only the cached mapping entries; the rest live on flash. Out of scope here.

### Data integrity (M27)

- **Checksum**: a small summary of a data block (typically 4–8 bytes for a 4 KB block) used to detect corruption (OSEP §45.3). The client recomputes the checksum on read and compares against the stored value; a mismatch means the block was corrupted since it was written.
- **XOR checksum**: per-byte XOR across the block, mod 256. Cheap; misses bit-pattern collisions (two bits in the same position within each unit changing cancel out).
- **Additive checksum**: per-byte sum across the block, mod 256. Cheap; misses reorderings because addition is commutative.
- **Fletcher checksum**: two accumulators s1 (Σ d_i mod 255) and s2 (Σ s1_i mod 255). Strictly stronger than XOR/additive at slightly higher cost (OSEP §45.3 [F82]).
- **CRC** (cyclic redundancy check): polynomial-division-based checksum; even stronger than Fletcher but more complex. Real systems use CRC-32 or CRC-64.
- **Latent sector error** (LSE): a disk sector that's been damaged in some way (head crash, cosmic ray) and unreadable (OSEP §45.1). The disk returns an error; recovery uses RAID redundancy.
- **Block corruption**: a silent fault — the disk returns the wrong data without indicating a problem (OSEP §45.1). Detected only by checksumming.
- **Physical ID**: the disk + block number stored with each checksum (OSEP §45.5). Detects misdirected writes — when a controller writes data to the wrong location.
- **Misdirected write**: a write that lands at the wrong location (OSEP §45.5). Detected by physical-ID mismatch.
- **Lost write**: a write that the device reports as complete but never actually persists (OSEP §45.6). Detected by a per-block write sequence counter.
- **Scrubbing**: the background process of reading every block on disk and verifying its checksum (OSEP §45.7). Catches bit rot in rarely-accessed blocks.
- **Fail-stop vs fail-partial**: the traditional disk-failure model assumed either the whole disk works or the whole disk fails (fail-stop). Modern disks exhibit *partial* failures — LSEs and corruption on individual blocks (fail-partial [P+05]).
- **Scrubbing schedule** (M33, OSEP §45.7 + §45.8): the periodic invocation of the scrubber — every `intervalMinutes`, covering `batchSize` blocks per pass, throttled so it doesn't starve foreground I/O. OSEP §45.7 notes "Typical systems schedule scans on a nightly or weekly basis"; §45.8 quantifies the space (~0.19% for 8-byte per 4 KB) and CPU overheads that motivate why scrubbing isn't free.
- **Scrubbing catch probability** (M33, OSEP §45.8): the probability that a per-block failure is caught before it spreads. Closed-form: `1 - e^(-λ · t)` where λ = per-block MTTF and t = mean time between scrubber passes of a given block.

### I/O devices (M34)

- **Device register** (M34, OSEP §36.2): a small piece of memory or I/O port that the CPU reads/writes to communicate with a device. OSEP figure 36.3 shows the canonical 3-register device: status, command, data.
- **MMIO** (Memory-Mapped I/O, M34, OSEP §36.6): mapping device registers into the regular address space so the CPU accesses them with normal load/store instructions. The standard on modern hardware (PCIe, ARM SoCs). OSEP: "the hardware makes device registers available as if they were memory locations. To access a particular register, the OS issues a load (to read) or store (to write) the address; the hardware then routes the load/store to the device instead of main memory."
- **PIO** (Programmed I/O, M34, OSEP §36.3 + §36.6): the older x86 model — special `in`/`out` instructions reach an isolated I/O address space. OSEP §36.3: "When the main CPU is involved with the data movement (as in this example protocol), we refer to it as programmed I/O (PIO)." We don't model it directly because .NET can't issue PIO; the M34 simulator exposes the access pattern only.
- **DMA** (Direct Memory Access, M34, OSEP §36.5): the device reads/writes main memory directly without going through the CPU. OSEP: "A DMA engine is essentially a very specific device within a system that can orchestrate transfers between devices and main memory without much CPU intervention." Burns fewer CPU cycles than PIO for large transfers.
- **Canonical protocol** (M34, OSEP §36.3): the four-step device-driver dance — poll `Status` until not busy → write `Data` → write `Command` → poll `Status` until done. "The protocol has four steps."
- **Interrupt-driven I/O** (M34, OSEP §36.4): the device raises a hardware signal (IRQ) when it needs the CPU's attention. The OS issues the request, puts the calling process to sleep, and context-switches to another task. "When the device is finally finished with the operation, it will raise a hardware interrupt, causing the CPU to jump into the OS at a predetermined interrupt service routine (ISR) or more simply an interrupt handler." Beats busy-polling for I/O-bound workloads; OSEP §36.4 TIP notes that for *fast* devices, polling may still be better because the cost of interrupt handling + context switching outweighs the savings.

### Scheduling algorithms (M13, M13.2, M13.3)

- **MLFQ** (Multi-Level Feedback Queue): A scheduler that demotes CPU-bound jobs and promotes I/O-bound ones across multiple priority queues, with periodic priority boost to prevent starvation.
- **Stride scheduling**: A proportional-share scheduler. Each job gets a stride inversely proportional to its ticket count; the lowest-pass job runs next. Deterministic.
- **Lottery scheduling**: A proportional-share scheduler. Each job gets tickets; the scheduler draws a random ticket to pick the next job. Probabilistic.

### Crypto / Security (M23.1–.6)

- **PBKDF2**: Password-Based Key Derivation Function 2 (RFC 8018). A deliberately slow keyed hash designed to be tunable in cost via an iteration count. The M23 default of 100k iterations matches the OWASP 2024 minimum for password storage.
- **Salt**: A per-user random byte string concatenated to the password before PBKDF2 hashing. Defeats precomputed rainbow tables since identical passwords yield different hashes for different users (OSEP §54.4).
- **Constant-time compare**: A byte-array equality check that does not short-circuit on the first differing byte. Defends against timing side-channels in password verification.
  _Avoid_: timing-safe compare, secure compare
- **Fail-safe defaults** (OSEP §53.4): an authorization system that defaults to denying access on errors, returns identical responses for distinct failure modes so attackers can't enumerate valid users, and never reveals information that a properly authenticated user wouldn't need.
- **AES-GCM**: An authenticated-encryption mode that bundles confidentiality (AES-256) with integrity (a 128-bit authentication tag). GCM tag failure raises an exception; the cipher fails closed.
  _Avoid_: AES (without -GCM — that name is the underlying block cipher, not the AEAD)
- **Nonce**: A number used once. AES-GCM requires a unique 12-byte nonce per encrypt call. Reusing a `(key, nonce)` pair leaks `c1 ⊕ c2 == p1 ⊕ p2` (OSEP §56.5 + §56.6).
  _Avoid_: IV (in the GCM context; AES-GCM uses the term nonce specifically)
- **At-rest encryption**: Protecting data while it sits in storage (disk, RAM, backup) so that stealing the storage doesn't yield plaintext (OSEP §56.7). Requires that the decryption key NOT be in the same place as the encrypted data — for a single-process lab, that means the key lives only in process RAM.
- **RBAC** (OSEP §55.6, Ferraiolo-Kuhn [FK92]): Access control decision based on the *role* a user has, not their identity. Simplest case: User vs Admin. Lets one role assignment propagate to every user in that role.
  _Avoid_: ACL (an ACL is per-resource; RBAC is per-role — distinct mechanisms)
- **Public-key cryptography** (OSEP §56.3): Use of an asymmetric keypair {private, public}. Signing with the private key produces a value verifiable by anyone with the public key; encryption with the public key produces a value decryptable only by the holder of the private key.
  _Avoid_: asymmetric encryption (overloaded — refers only to the encryption half)
- **Keypair**: The {private key, public key} pair generated together for public-key cryptography.
  _Avoid_: key pair (two words), key (without qualifier, when in PK context)
- **Session key**: A symmetric key derived for a single session (e.g. via HKDF in the TLS-style handshake). Short-lived; distinct from long-term identity keys.
- **TOTP** (RFC 6238, OSEP §54.5 "what you have"): A 6-digit code derived from HMAC-SHA256(shared_secret, floor(unix_time / 30)). The canonical authenticator-app algorithm. Replay-protected by a small clock-skew window (typically ±1 step) plus server-side `seen` tracking (deferred in our slice).
- **HKDF-SHA256** (RFC 5869): A two-step (Extract + Expand) key-derivation function built on HMAC-SHA256. Used in TLS (RFC 8446) to derive per-session keys from a shared secret + handshake transcript.
- **X.509 / certificate** (OSEP §57.3): The standard PK certificate format. Binds an entity's public key to an identity via a trusted CA signature. Not implemented in this lab; OSEP §57.3 warns the PK auth problem is incomplete without it.

### Lock-free (M22)

- **CAS** (Compare-And-Swap): An atomic hardware primitive that updates a memory location only if it still holds an expected value. Foundation of lock-free algorithms.
  _Avoid_: compare-and-swap (write as two words only in code or RFC-style citations)
- **Lock-free**: An algorithm that makes system-wide progress without holding a lock, typically via CAS retry loops.
  _Avoid_: wait-free (a strictly stronger property; not claimed here), non-blocking (ambiguous)
- **Treiber stack**: A lock-free stack using CAS on the head pointer. Suffers from ABA without hazard pointers / epoch reclamation.
- **ABA**: A CAS failure mode where a value cycles A→B→A between the read and the CAS, so the CAS succeeds but the logical state has changed.

## Runtime Behavior

The host binds to TCP port `8080` on all IPv4 interfaces and begins listening.

### Default mode (worker pool)

The accept loop calls `WorkerPool.Enqueue(clientSocket)`. The pool:

1. Owns 8 long-lived worker threads (created once at startup).
2. Workers wait under a lock on `Monitor.Wait(PoolLock)` while the queue is empty.
3. `Enqueue` adds the socket under the lock and calls `Monitor.Pulse` to wake one worker.
4. The worker dequeues, releases the lock, and runs `HandleClient` for that connection.

Each `HandleClient` invocation:

1. Allocates a 1 MB receive buffer.
2. Loops `Socket.Receive` until the buffer contains `\r\n\r\n` + (optional) `Content-Length` bytes, capped at 1 MB.
3. Decodes the request as UTF-8 and logs the raw bytes.
4. Parses method, path, version, headers.
5. If path is `/slow`, sleeps 30 seconds to simulate blocking I/O.
6. Builds a response: static file from `wwwroot` (default), or one of the demo routes below.
7. Sends the response, logs size, closes the socket.
8. Per-client `SocketException` and generic `Exception` are logged; one bad client does not stop the host.

Routes (canonical runtime list; *why* each route exists lives in the matching ADR — 0008 → /auth/*, 0009 → /crypto/encrypt|decrypt|keygen|tamper-demo|nonce-reuse-demo|dump, 0010 → /auth/grant|role|/protected/secret, 0011 → /crypto/rsa-keygen|sign|verify|import-pubkey, 0012 → /crypto/handshake, 0013 → /crypto/totp-demo, 0014 → /raid/run, 0015 → /lfs/run, 0016 → /ssd/run, 0017 → /integrity/run):
- `/slow` → 404 after 30 s sleep
- `/race` → runs 1_000_000 non-atomic increments on `RequestStats.UnsafeCounter` (race demo); returns cumulative value
- `/race-safe` → same loop under `lock (RequestStats.SafeCounterLock)`; deterministic
- `/stats` → process threads + working set + private bytes + total requests
- `/qstats` → worker count + queue length + total requests
- `/auth/register?user=X&pass=Y` → M23; returns 200 (registered) or 409 (username taken) or 400 (empty user/pass). Plaintext password never stored.
- `/auth/login?user=X&pass=Y` → M23; returns 200 (match) or 401 ("invalid credentials"). The 401 body is **byte-identical** for "no such user" and "wrong password" (OSEP §53.4 fail-safe defaults). Verify uses constant-time compare; "missing user" path also runs a dummy PBKDF2 so the wall-clock cost is the same on both branches.
- `/auth/dump` → M23; lists every stored `{username, createdAt, salt, hash}`. Proves only hash + salt are persisted.
- `/auth/run?scenario=...` → M23 demos. Scenarios: `register`, `hashattack` (two users, same plaintext, different salts + hashes), `dictionary` (5 common passwords PBKDF2-verified against a stored hash, ~70 ms each), `login`, `clear`.
- `/crypto/keygen` → M23.2; rotate the in-memory 256-bit AES key. Returns old + new hex. (OSEP §56.7 "well chosen ... symmetry".)
- `/crypto/encrypt?name=X&msg=Y` → M23.2; store slot X with plaintext `msg`. Returns hex nonce + ciphertext + tag. Plaintext is wiped from the working buffer before the route returns. Authenticated AES-256-GCM.
- `/crypto/decrypt?name=X` → M23.2; decrypt + verify. If the auth tag mismatches, returns 401 with the `CryptographicException` text — **never** silently returns garbage.
- `/crypto/tamper-demo` → M23.2; encrypt a known plaintext, flip bit 5 of byte 7 of the ciphertext, attempt decrypt. The auth tag fails closed. Reproducible smoke evidence in `m23-at-rest-encryption/s1-at-rest.md`.
- `/crypto/nonce-reuse-demo` → M23.2; prove `c1 ⊕ c2 == p1 ⊕ p2` is `True` when the same `(key, nonce)` pair encrypts two messages. The OSEP §56.5/§56.6 lesson in 2 lines.
- `/crypto/dump` → M23.2; list every slot's at-rest form `{nonce, len, tag}`. Proves plaintext is not in storage.
- `/auth/grant?user=X&role=admin` → M23.3; promote / demote an existing user. (No auth-gate in this slice.)
- `/auth/role?user=X` → M23.3; read a user's role. Returns "unknown" not "no such user" (fail-safe defaults, OSEP §53.4).
- `/protected/secret?user=X&pass=Y` → M23.3; Admin-gated route. 200 + secret body for Admin + correct password, byte-identical 403 on any failure.
- `/crypto/rsa-keygen` → M23.4; generate 2048-bit RSA keypair, return hex SPKI public key. Private stays in process memory.
- `/crypto/sign?msg=X` → M23.4; sign `msg` with the in-memory private key. Returns 256-byte hex signature.
- `/crypto/verify?msg=X&sig=Y[&pubkey=Z]` → M23.4; verify the signature. `?pubkey=` lets you verify against an externally-distributed public key (OSEP §57.3 foreshadow).
- `/crypto/import-pubkey` → M23.4; switch the active public key to `?msg=HEX`. Mirror of "I got your key from somewhere else".
- `/crypto/handshake` → M23.5; one-shot simplified TLS-style handshake: generates two nonces, derives a 32-byte session key via HKDF-SHA256, uses that key to encrypt + decrypt a sample payload via the M23.2 AEAD. Proves the key is a working AES-256 key.
- `/crypto/totp-demo?msg=HEXSECRET` → M23.6; compute the 6-digit TOTP for the given secret at the current time, then verify it under four conditions (current, ±1 step skew, ±2 steps outside window, wrong code).
- `/raid/run?level=0|1|4|5&disks=N&blocks=M&failed=K` → M24; write a deterministic payload across `M` stripes, optionally fail disk `K`, then read everything back. Output is the disk grid + write/read traces. RAID 0 fails with one disk loss; RAID 1 mirrors; RAID 4/5 recover via XOR.
- `/lfs/run?scenario=create|rewrite|clean&segments=N&blocks=M&files=K` → M25; create K files (one block each), then optionally rewrite some of them (to introduce dead blocks), then optionally run the segment cleaner. Output is the disk grid (segment summary + live/dead markers) + write/read/clean traces.
- `/ssd/run?scenario=write|gc|wear&blocks=N&pages=K` → M26; write a deterministic payload of LBAs (log-structured FTL), then optionally rewrite some LBAs (to introduce dead pages) and run GC, then optionally dump the wear report. Output is the LBA→page mapping table + block-state grid (V/D/E) + wear histogram (erase counts per block).
- `/integrity/run?scenario=compute|corrupt|scrub&blocks=N&blockSize=M` → M27; compute all three checksums (XOR/additive/Fletcher) on a fixed payload, then optionally inject a silent corruption + show detection, then optionally write N blocks + inject faults + run the scrubber + report failures. Output is the block grid (sequence, three checksums, data preview) + scenario trace.

### `--async` mode (event-based)

The accept loop calls `serverSocket.AcceptAsync(ct)`. Each accepted connection becomes a `Task` driven by `ReceiveAsync` / `SendAsync`. The same routes and helpers apply. No worker pool, no per-connection OS thread — the runtime `ThreadPool` multiplexes all waiting `Task`s over a small set of workers. Stop with Ctrl+C.

## Learning Workflow

This repo uses two learning levels:

- **Milestone**: a larger server capability that changes what the server can do. Milestones are roadmap units.
- **Lesson slice**: a small build-first exercise inside a milestone, sized for one focused learning session. Lesson slices are execution units.

The roadmap is also grouped into four OSTEP-aligned phases in `docs/adr/0003-four-phase-ostep-roadmap.md`. All four phases are complete (M1–M7). Phases explain the learning progression; milestones and lesson slices remain the execution structure.

Each lesson slice should include:

1. OSTEP concept (from NotebookLM or book).
2. Small server behavior to build.
3. C#/.NET mechanism.
4. Observable experiment.
5. Short learning note.

A lesson slice is 30–90 minutes of focused work: read a concept, build a tiny behavior, run an experiment, and capture what was observed. Milestones exist so the server's evolution direction stays visible; lesson slices exist so daily work is concrete and completable.

Every lesson slice must follow `docs/learning/README.md`: start with a concrete question, query OSTEP NotebookLM, design the experiment before code, build one observable behavior, run the experiment, write the note, and pass the 10-rule checklist. The three-question test for every slice is: what is the OS doing, which .NET API exposes it, and where does it break at scale?

The current OSTEP-connected notebook is in NotebookLM: `Operating Systems: Three Easy Pieces` (id `74bcbca0-6161-48cd-92bb-9dd39032794e`, 69 sources).

Phase 1 learning docs (The Process & The Byte Stream):

- `docs/learning/m1-raw-socket-server/s1-raw-socket-server.md`
- `docs/learning/m2-http-request/s1-http-request.md`
- `docs/learning/m3-static-file-server/s1-static-file-server.md`
- `docs/learning/m1-raw-socket-server/s2-robust-request-receive.md`

Phase 2 learning docs (Threads: Multiple Points of Execution):

- `docs/learning/m4-thread-per-connection/s1-single-thread-blocking.md`
- `docs/learning/m4-thread-per-connection/s2-thread-per-connection.md`
- `docs/learning/m4-thread-per-connection/s3-scheduling-non-determinism.md`
- `docs/learning/m4-thread-per-connection/s4-shared-address-space.md`
- `docs/learning/m4-thread-per-connection/s5-race-condition-prep.md`
- `docs/learning/m4-thread-per-connection/s6-thread-per-connection-limits.md`

Phase 3 + 4 learning docs:

- `docs/learning/m5-race-lab/s1-race-lab.md` (race fixed with `lock`)
- `docs/learning/m6-bounded-worker-pool/s1-bounded-worker-pool.md` (bounded pool + producer/consumer; M8 bounded queue appended as section 6.3)
- `docs/learning/m8-bounded-queue/s1-bounded-queue.md` (M8 full learning note)
- `docs/learning/m9-reader-writer-lock/s1-reader-writer-lock.md` (M9 full learning note)
- `docs/learning/m10-threadpool-cap/s1-threadpool-cap.md` (M10 full learning note)
- `docs/learning/m11-raw-syscall-demo/s1-raw-syscall-demo.md` (M11 full learning note)
- `docs/learning/m12-mini-file-system/overview.md` (M12 mini-FS plan + learning note for slices 12.1–12.4)
- `docs/learning/m7-async-event-based/s1-async-event-based.md` (event-based server)
- `docs/learning/m13-mlfq/` (M13 MLFQ overview + slice doc)
- `docs/learning/m13.2-stride-lottery/` (M13.2 Stride + Lottery overview + slice doc)
- `docs/learning/m13.3-multicpu-scheduling/` (M13.3 multi-CPU scheduling overview + slice doc)
- `docs/learning/m14-pager/` (M14 pager overview + slice doc)
- `docs/learning/m16-tlb/` (M16 TLB overview + slice doc)
- `docs/learning/m17-multi-level-pt/` (M17 multi-level PT overview + slice doc)
- `docs/learning/m18-replacement/` (M18 replacement overview + slice doc)
- `docs/learning/m19-complete-vm/` (M19 complete VM / COW overview + slice doc)
- `docs/learning/m20-dining-philosophers/` (M20 dining philosophers overview + slice doc)
- `docs/learning/m21-ffs/` (M21 FFS placement overview + slice doc)
- `docs/learning/m22-lock-free/` (M22 lock-free CAS overview + slice doc)
- `docs/learning/m23-auth/` (M23 password auth overview + slice doc)
- `docs/learning/m23-at-rest-encryption/` (M23.2 at-rest encryption overview + slice doc)
- `docs/learning/m23-rbac/` (M23.3 RBAC overview + slice doc)
- `docs/learning/m23-pkcrypto/` (M23.4 PK sign/verify overview + slice doc)
- `docs/learning/m23-handshake/` (M23.5 TLS-style handshake overview + slice doc)
- `docs/learning/m23-totp/` (M23.6 TOTP overview + slice doc)
- `docs/learning/m24-raid/` (M24 RAID overview + slice doc)
- `docs/learning/m25-lfs/` (M25 LFS overview + slice doc)
- `docs/learning/m26-ssd/` (M26 SSD overview + slice doc)
- `docs/learning/m27-integrity/` (M27 data integrity overview + slice doc)

All twelve roadmap slices + post-roadmap extensions + the VM paging chain + the multi-CPU/dining/FFS/lock-free quartet + the M23 Part IV suite (.1 password + .2 at-rest + .3 RBAC + .4 PK + .5 handshake + .6 TOTP) + M24 (RAID 0/1/4/5) + M25 (LFS segments + imap + CR + cleaner) + M26 (SSD FTL + GC + wear tracking) + M27 (data integrity: checksums + physical ID + write sequence + scrubber) have learning notes with smoke-test output captured inline. M8 (the first post-roadmap extension) has its own learning note and a 6.3 section appended to the M6 note. The M16-M19 chain + M13.2 + M13.3 + M20 + M21 + M22 + M23 + M24 + M25 + M26 + M27 all have an `overview.md` and a slice doc under their own folder. **Part III Persistence is now closed** — Ch. 38-45 all have working code in this repo.

## Design Intent

Prefer preserving the educational, low-level socket-server character of the project unless a task explicitly asks for a higher-level framework. When adding behavior, make the network lifecycle easy to see and reason about.

The repo follows **build-first learning** (per `docs/learning/README.md`): each new behavior is a 30-90 min slice with one observable OS phenomenon, a smoke test, and a learning note. Chapters of OSEP are read on demand when a slice needs them, not end-to-end.

**Source provenance**: slice docs cite specific OSEP sections. A two-pass doc sweep was done in 2026-09 against the source PDFs (`pages.cs.wisc.edu/~remzi/OSTEP/*.pdf`):

1. **Pass 1 (M9–M12)**: verified Ch. 30 (condition variables), 31 (semaphores + 31.5 reader-writer), 33 (event-based), 39 (files & directories), 40 (vsfs). Several errors found and corrected: §30.4 misattribution for the "while not if" / "two CVs" / "hold lock while signaling" lessons (those are §30.1 / §30.2); the "vsfs = xv6" mistake in M12 (vsfs is OSEP's own design, xv6 is a separate OS); the bogus "log" claim in M12 (vsfs has no log; that's Ch. 42).

2. **Pass 2 (M1–M5)**: verified Ch. 4 (process + 4.4 states), 6 (LDE), 13 (address space), 26 (concurrency), 27 (thread API), 28 (locks), 36 (I/O). One error found: s4.3 cited §26.4 figure 26.7 (the race example) for scheduler non-determinism, but the right citation is §26.2 t0.c + figures 26.3/26.4/26.5 (the non-deterministic ordering example). The M5 race-lab citations for §28.1-2 / §28.7-9 / §28.12-14 are accurate to the chapter's actual section breakdown.

After both passes, every "Key point from OSEP §X.Y" should match the cited chapter's actual table of contents. Future readers should still treat the citations as a pointer to where the lesson lives, not as an authoritative cross-reference, but the section numbers should now be correct.

Useful directions that fit the project:

- Extend concurrency via the next-milestones roadmap in `docs/adr/0004-extend-broad-concurrency-roadmap.md`. M8 (bounded queue + 503), M9 (reader-writer lock + cache), M10 (ThreadPool cap), M11 (raw open/read/close syscall demo), M12 (mini file system slices 12.1–12.7), M13 (MLFQ scheduler), M13.2 (Stride + Lottery), M13.3 (multi-CPU), M14 (linear page table), M16 (TLB), M17 (multi-level page tables), M18 (replacement policies), M19 (COW + swap), M20 (dining philosophers), M21 (FFS), M22 (lock-free CAS), the M23 Part IV suite (.1 password + .2 at-rest + .3 RBAC + .4 PK + .5 handshake + .6 TOTP), and M15 (ArrayPool in async mode) are done.
- Add `ArrayPool<byte>` to lower per-connection memory in async mode (would change the M7 numbers from 172 MB toward M6's 21 MB).
- Add a `Retry-After` header to the M8 503 response so clients can back off intelligently.
- Extend the **OSEP coverage gaps** in the OSEP Coverage section: paging (Ch. 14-17, §19.5+§19.7 ASID — spec-only in M28, §20.4 inverted PTs, §21.4-§21.6 approximated LRU + dirty pages, §23.1 VMS demand-zeroing + RSS + segmented FIFO + second-chance list, §23.2 Linux 2Q + huge pages + 4-level PTs + NX + ASLR + KPTI), full FS (Ch. 36 §36.2-§36.6 canonical device + protocol + interrupts + DMA + PIO/MMIO — spec-only in M34; Ch. 36 §36.7-§36.10 device-driver abstraction + IDE case study + historical notes deferred; Ch. 41 §41.7 sub-blocks + parameterized placement deferred; Ch. 43 §43.3 segment-size math + §43.12 two-CR alternating writes — spec-only in M31; §43.12 roll-forward recovery deferred; Ch. 44 §44.9 block-level + hybrid FTL mapping — spec-only in M32; Ch. 45 §45.7 periodic scrubbing schedule — spec-only in M33; §45.8 ZFS-style end-to-end checksum tree deferred), security (Ch. 53.5 system-call access, Ch. 54.5 PK × TOTP composition, Ch. 54.6 biometrics, Ch. 54.7 sudo / setuid, Ch. 55 ACLs per file / capabilities / mandatory mode / Android permission model, Ch. 56.3 hybrid encryption, Ch. 56.5 PK key selection, Ch. 57.3 X.509 cert chains, Ch. 57.6 key-revocation / replay protection, hardware enclaves §53 TPM), concurrency (Ch. 30 §30.1-§30.3 CVs — spec-only in M29, Ch. 32.2 atomicity/order bugs in M23, Ch. 32.3 deadlock prevention/avoidance — spec-only in M30, Ch. 29 deeper lock-free data structures — DFTL, Michael-Scott queue, hazard pointers). Each new chapter group should get its own ADR before any slices start.
- Extract small concepts such as request receiving, response formatting, and connection handling.
- Add focused tests around pure logic if response formatting or request parsing is introduced.
- Keep console output clear because it is part of the learning feedback loop.

Directions that change the project meaning and should be explicit decisions:

- Replacing sockets with ASP.NET Core, Kestrel, or `HttpListener`.
- Adding production features such as TLS, keep-alive, middleware, dependency injection, or background worker infrastructure.
- Removing the `--async` flag and committing to one concurrency model.

## Commands

Build the solution:

```powershell
dotnet build MiniWebServer.sln
```

Run the host (default = bounded worker-pool mode):

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Run the host in event-based mode (single-threaded accept loop, `Task` per connection):

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj -- --async
```

The host uses the `wwwroot` directory copied beside the executable, so it works even when `dotnet run --project` is started from a different current directory.

Try it from another terminal while the host is running:

```powershell
curl http://localhost:8080/
curl -i http://localhost:8080/missing.txt
curl http://localhost:8080/slow
curl http://localhost:8080/stats
curl http://localhost:8080/qstats
```

Run parser tests:

```powershell
dotnet run --project tests/MiniWebServer.Host.Tests/MiniWebServer.Host.Tests.csproj
```

## Known Limitations

- Port `8080` is hard-coded.
- The async-mode accept loop has no `CancellationToken` for graceful shutdown — Ctrl+C still terminates the process; no in-flight requests are drained.
- The worker pool queue (`Queue<Socket>`) is unbounded. A sustained burst can grow memory without limit.
- `ThreadPool` size in async mode is unbounded by default. Cap it via `ThreadPool.SetMaxThreads` if you want a measured-backpressure story.
- Static file reads use `File.ReadAllBytes(...)`, so large files are loaded into memory all at once. Adding `ArrayPool<byte>` + streaming would lower memory in both modes.
- Content type support is minimal (HTML, CSS, JS, plain text).
- No TLS, no keep-alive, no chunked transfer encoding, no HTTP/2.
- Each connection allocates its own 1 MB receive buffer (`ServerConfig.MaxRequestBytes`).
- The language-service file `src/MiniWebServer.Host/MiniWebServer.Host.csproj.lscache` is generated by C# Dev Kit and is not source logic.
