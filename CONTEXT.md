# Mini Web Server Context

## Status Snapshot

Updated 2026-10-07 (M34 device drivers added — Ch. 36 §36.2-§36.6). Every roadmap milestone is now implemented. Legend: ✅ built + experimented + noted · 🟡 planned · ⬜ future.

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
| M28 (28.1) | ASID-tagged TLB: per-entry ASID + Global bit, per-ASID flush that survives context switches | Ch. 19 (§19.5 context-switch issue, §19.7 MIPS R4000 ASID + G bit) | ✅ |
| M29 (29.1) | Condition variable primitive with its own wait queue + lost-wakeup repro + one-CV deadlock vs two-CV fix + covering condition | Ch. 30 (§30.1-§30.3) | ✅ |
| M30 (30.1) | Deadlock: the §32.3 deadlock reproduced, plus one prevention per Coffman condition (lock ordering / batch acquire / trylock+backoff) and Banker's avoidance | Ch. 32 §32.3 | ✅ |
| M31 (31.1, 31.2) | LFS segment-size cost model (§43.3 eq. 43.6 + its inverse) and two-CR alternating writes with header/body/trailer crash recovery | Ch. 43 (§43.3, §43.12) | ✅ |
| M32 (32.1) | Block-level + hybrid (log-block) FTL with switch/partial/full merge | Ch. 44 §44.9 | ✅ |
| M33 (33.1) | Periodic scrubbing schedule driven by the §45.8 overhead argument | Ch. 45 (§45.7, §45.8) | ✅ |
| M34 (34.1) | Device drivers: canonical protocol, interrupts, DMA, PIO vs MMIO | Ch. 36 (§36.2-§36.6) | ✅ |
| M35 | Scheduling baselines: FIFO / SJF / STCF + response time (the §8-§10 comparison point) | Ch. 7 | 🟡 planned (ADR 0025) |
| M36 | Free-space management: stack / linked-list / bitmap mechanisms, first / best / next fit, buddy | Ch. 17 | 🟡 planned (ADR 0025) |
| M37 | Disk geometry + disk scheduling (FCFS / SSTF / SCAN) | Ch. 37 | 🟡 planned (ADR 0025) |
| M15 | ArrayPool&lt;byte&gt; in async mode (receive + response buffers) | n/a (perf) | ✅ |

**Milestones**: M1 ✅–M15 ✅, M13.2 ✅, M13.3 ✅, M16 ✅, M17 ✅, M18 ✅, M19 ✅, M20 ✅, M21 ✅, M22 ✅, M23 ✅ (incl. .1 password + .2 at-rest + .3 RBAC + .4 PK + .5 handshake + .6 TOTP), M24 ✅ (RAID 0/1/4/5 with XOR recovery), M25 ✅ (LFS segments + imap + CR + cleaner), M26 ✅ (SSD FTL + GC + wear tracking), M27 ✅ (data integrity: checksums + physical ID + write sequence + scrubber), M28 ✅ (ASID-tagged TLB), M29 ✅ (condition variables), M30 ✅ (deadlock prevention + Banker's avoidance), M31 ✅ (LFS segment sizing + two-CR recovery), M32 ✅ (block-level + hybrid FTL), M33 ✅ (scrubbing schedule), M34 ✅ (canonical device + PIO/DMA + interrupts). M8–M15 are the post-roadmap extensions per `docs/adr/0004-extend-broad-concurrency-roadmap.md` (M8–M12), `docs/adr/0006-mini-scheduler-mlfq-proportional-multicpu.md` (M13), `docs/adr/0007-mini-pager-mmu-tlb-multilevel-replacement.md` (M14). The original 12-slice roadmap is closed, as is the `docs/learning/ROADMAP.md` table that followed it (M16-M34). **Latest**: M34 (device drivers). **Next**: M35 (Ch. 7 scheduling baselines), then M36 (Ch. 17 free-space) and M37 (Ch. 37 disk geometry) — ADR 0025; exclusions are Ch. 5, 14, 15, 16, 48-50, all with stated reasons. See `docs/adr/0008`–`0025` for ADRs. Each yielded a small slice with its own overview + smoke trace in `docs/learning/`.
**Tests**: 98 passing (14 pre-existing + 11 RAID + 6 LFS + 5 SSD + 7 Integrity + 4 TLB + 8 CV + 4 deadlock + 7 LFS-extensions + 12 FTL + 8 scrubbing + 12 device).
**Code/runtime**: `net10.0`. Two run modes selectable via `--async` flag: default = bounded worker pool (8 threads) + producer/consumer queue, async = `AcceptAsync` + `Task` per connection with `ReceiveAsync` / `SendAsync`. Port 8080, static files under `wwwroot`.
**Latest commit**: M34 (device drivers). New file `src/MiniWebServer.Host/MiniScheduler/Device.cs`: a hand-clocked `Device` with §36.2's three registers, §36.3's four-step polling protocol, a DMA channel, and an interrupt callback, plus `DeviceCostModel` and `DeviceDemos`. The device is clocked by hand through `Tick()` rather than by a background thread — every protocol in these sections is a claim about *when* the CPU looks, and a thread would make that ordering depend on scheduling luck. Four scenarios on `/device/run`: `canonical-protocol`, `pio-vs-dma`, `interrupt-vs-poll`, `mmio`. **Three spec corrections**: `IntPtr hostAddr` is meaningless in a simulator, so the DMA API takes the `byte[]` that *is* the simulated host memory; "PIO means zero interrupts" is not §36.3's definition, which is about who moves the bytes; and the spec dropped §36.4's actual conclusion that polling beats interrupts on fast devices — `interrupt-vs-poll` makes it measurable (polling wins at 1 tick, the interrupt at 500). OSEP states no numbers in Ch. 36, so the cost constants are invented and named as such; the measured DMA crossover is 140 bytes. A review pass then found six defects, most seriously that a zero-latency device never reported completion and so hung every caller in §36.3's polling loop. See `docs/adr/0024-m34-device-drivers.md`.

## OSTEP Coverage

OSEP v1.10 has 57 numbered chapters. The repo maps **the core three pieces** (Virtualization + Concurrency + Persistence) end-to-end, with detailed §-sub-section citations in each milestone's `overview.md`:

| Piece | Coverage | Roadmap slices |
|---|---|---|
| **Virtualization** | Ch. 4 (§4.1 process, §4.4 states Running/Ready/Blocked); Ch. 6 (§6.1 direct execution, §6.2 syscalls, §6.3 timer interrupt); Ch. 8 (§8.1-§8.5 MLFQ); **Ch. 9 lottery + stride scheduling**; **Ch. 10 multi-CPU scheduling (SQMS / MQMS / work stealing)**; Ch. 13 (address space, implicit); Ch. 18 (linear page table); **Ch. 19 TLB** (§19.1-§19.3 basic + miss handling, §19.5 context switch + ASID, §19.7 MIPS R4000 example with ASID + Global bit — **M28**); **Ch. 20 multi-level page tables**; Ch. 21 + Ch. 22 replacement policies; Ch. 23 complete VM (COW + swapping); Ch. 26-27 (thread = point of execution, thread API); Ch. 33 (event-based) | M1, M2, M3, M4, M7, M13, M13.2, M13.3, M14, M16, M17, M18, M19, M28 |
| **Concurrency** | Ch. 26 (§26.4 figure 26.7 the race); Ch. 27 (thread API); Ch. 28 (§28.1 lock abstraction, §28.7 test-and-set, §28.9 CAS, §28.16 two-phase); Ch. 29 (§29.1 concurrent counters, §29.2 concurrent linked lists — lock-free CAS via `AtomicIncrement` + lock-free list insert); Ch. 30 (**§30.1** CV definition + Mesa/Hoare + lost-wakeup bug, **§30.2** producer/consumer — one-CV broken vs two-CV correct, **§30.3** covering conditions + broadcast); Ch. 31 (§31.4 bounded buffer, §31.5 reader-writer, **§31.6 dining philosophers**); Ch. 32 (Ch. 32.2 non-deadlock bugs, **§32.3 deadlock bugs + the four Coffman conditions + prevention per condition + Banker's avoidance**); Ch. 33 (events) | M4, M5, M6, M7, M8, M9, M10, M20, M22, M29, M30 |
| **Persistence** | Ch. 36 (§36.1 system architecture, **§36.2 canonical device, §36.3 canonical protocol, §36.4 interrupts, §36.5 DMA, §36.6 explicit instructions vs MMIO** — M34; TCP receive loop uses kernel async I/O); Ch. 38 (§38.1 interface, §38.2 fault model, §38.3 evaluation axes, **§38.4 RAID 0 striping, §38.5 RAID 1 mirroring, §38.6 RAID 4 dedicated parity, §38.7 RAID 5 rotating parity**, §38.8 comparison summary, §38.9 RAID 6 mention); Ch. 39 (§39.3 open, §39.4 read/write, §39.13 rmdir); Ch. 40 (§40.2 vsfs layout, §40.3 inode, §40.4 directory, §40.5 free space, §40.6 access path, §40.7 caching); **Ch. 41 FFS (§41.3 cylinder/block groups, §41.4 locality policies, §41.6 large-file exception, §41.7 filespan + dirspan metrics)**; Ch. 42.3 (data journaling, recovery, batching, circular log, [Tricky Case: Block Reuse] deferred); **Ch. 43 LFS** (§43.2 write buffering + segments, §43.5 inode map, §43.6 checkpoint region, §43.7 read path, §43.9 garbage collection, §43.10 segment summary block + liveness, §43.11 cleaner policy) **+ §43.3 segment-size cost model + §43.12 two-CR alternating writes — M31**; **Ch. 44 Flash SSDs** (§44.3 raw flash operations, §44.7 log-structured FTL with page-level mapping, §44.8 GC, §44.10 wear leveling, §44.12 TRIM) **+ §44.9 block-level + hybrid FTL — M32**; **Ch. 45 Data integrity** (§45.1 LSEs + corruption, §45.3 XOR/additive/Fletcher checksums, §45.4 verify-on-read, §45.5 physical ID, §45.6 write sequence, §45.7 scrubber) **+ §45.8 overheads argument motivating the scrubbing schedule — M33** | M1, M2, M3, M11, M12.1–12.7, M21, M24, M25, M26, M27, M31, M32, M33, M34 |
| **Security** | Ch. 53.4 Saltzer-Schroeder; **Ch. 54.5 TOTP / what-you-have** (RFC 6238, HMAC-SHA256, ±1-step window); Ch. 54.4 password storage; Ch. 55.6 RBAC + protected route; Ch. 56.2 AES-256-GCM at-rest; Ch. 56.3 RSA-2048 sign/verify; Ch. 56.4 hashes + integrity; Ch. 56.5 nonce / brute force / key selection; Ch. 56.6 cryptography + OSes; Ch. 56.7 at-rest; **Ch. 57.5 TLS-style handshake** (HKDF-SHA256 session key) — Ch. 53.5 system-call primitives, Ch. 54.6 biometrics, Ch. 54.7 sudo/setuid, Ch. 55 ACLs per file / capabilities / mandatory mode, Ch. 56.3 hybrid encryption, Ch. 57.3 X.509 cert chains, Ch. 57.6 replay protection / SSH / HTTPS, hardware enclaves (TPM) | M23 (.1 password + .2 at-rest + .3 RBAC + .4 PK + .5 handshake + .6 TOTP) |

**Coverage**: OSTEP v1.10 has 57 numbered chapters, 12 of which are one-page dialogues (Ch. 1, 3, 11, 12, 24, 25, 34, 35, 46, 47, 51, 52) with no implementation content. Of the **45 content chapters, 35 have working code here — 78%**. Denominated over all 57 it is 61%. Covered: Ch. 2, 4, 6, 8-10, 13, 18-23, 26-33, 36, 38-45, 53-57. Not addressed: Ch. 5 (Process API — `fork`/`exec`/`wait`, out of scope for .NET), **Ch. 7 (Scheduling: Introduction — FIFO / SJF / STCF / response time / Round Robin; see the correction below; planned as M35)**, Ch. 14-17 (14 memory API, 15 base+bounds, 16 segmentation, 17 free-space — **M36**), **Ch. 37 (Hard Disk Drives — geometry + disk scheduling; M37)**, Ch. 48-50 (distributed systems / NFS / AFS). M35-M37 are recorded in ADR 0025 and would take this to 38 of 45 (84%). The lab focuses on the **core three pieces** (Virtualization + Concurrency + Persistence) plus Part IV Security. §-sub-section coverage is noted in each milestone's `overview.md`.

**Correction — Process API is Ch. 5, not Ch. 7.** Earlier revisions of this document, `ROADMAP.md`, and ADR 0008 all described the unimplemented `fork`/`exec`/`wait` chapter as "Ch. 7 Process API" and cited ADR 0006 as saying "Ch. 7 process API". Ch. 7 in OSEP v1.10 is *Scheduling: Introduction* — FIFO, SJF, STCF, response time, Round Robin, and I/O interleaving — and it is **not** implemented. Two different chapters were conflated under one number, which hid a real gap: the repo builds MLFQ (§8), lottery/stride (§9), and multi-CPU (§10), but never the baseline scheduling policies they are compared against. The verboten "Process API" chapter is Ch. 5.

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
- **M16 TLB** — Ch. 19 (§19.1 simple example, §19.2 TLB as cache, §19.3 who handles the TLB miss, §19.5 TLB issue: context switch; §19.4 "TLB Contents" + §19.7 MIPS R4000 example with ASID + Global bit landed in M28). See `m16-tlb/overview.md`.
- **M28 ASID-tagged TLB** — Ch. 19 §19.5 context-switch issue (the naive fix is a full flush on every switch; ASID lets the OS scope the flush to one address space) + §19.7 MIPS R4000 ASID + Global bit (`G` set ⇒ the ASID is ignored on lookup, so kernel mappings survive every per-ASID flush). Model: `TlbEntry.Asid` (byte, MIPS width) + `TlbEntry.IsGlobal`; `Lookup(asid, vpn)`; `Flush(int? asid)` — null keeps M16's full flush for kernel PTE edits, a value removes only that ASID's non-global entries; `Pager.CurrentAsid` bumps per `CreateProcess`. Multilevel TLB, ASID-rollover flush, x86 PCID/INVPCID, and reserved kernel-ASID ranges are deferred. See `m28-asid/overview.md` and `docs/adr/0018-m28-asid-tlb.md`.
- **M29 Condition variables** — Ch. 30 §30.1 (CV as an explicit queue; `wait()`'s atomic release-and-park; Mesa vs Hoare — "Virtually every system ever built employs Mesa semantics"; the lost-wakeup bug and the state variable that fixes it, figure 30.4) + §30.2 (producer/consumer: one CV is broken even with `while` because "a consumer should not wake other consumers, only producers"; two CVs fix it by design, figures 30.11/30.12; the "use while, not if" TIP) + §30.3 (covering conditions — broadcast when the waker does not know whom to wake, figure 30.15). Implemented as a real CV with its own FIFO queue rather than a `Monitor` wrapper, because two CVs over one monitor share one queue and the §30.2 fix becomes inexpressible (ADR 0019). Timed wait, Hoare semantics, and `WorkerPool` migration deferred. See `m29-cv/overview.md` and `docs/adr/0019-m29-condition-variables.md`.
- **M30 Deadlock prevention & avoidance** — Ch. 32 §32.3: the four Coffman conditions (mutual exclusion, hold-and-wait, no preemption, circular wait — "If any of these four conditions are not met, deadlock cannot occur") and one prevention technique per condition: lock ordering (the §32.3 TIP, ordered by an explicit id rather than the lock address), atomic batch acquire behind a global `prevention` lock, and trylock + random backoff with the chapter's own livelock caveat. Plus Dijkstra's Banker's algorithm as *avoidance*. **Citation boundary**: §32.3 names Banker's and calls it "only useful in very limited environments" but does not restate it — the tables and safety algorithm are [D64] via the standard OS literature (ADR 0020). Detect-and-recover and the partial-ordering problem are deferred. See `m30-deadlock/overview.md`.
- **M31 LFS extensions** — Ch. 43 §43.3 "How Much To Buffer?" (the cost model: every write pays a fixed positioning cost, so the segment must be large enough to amortise it; equations 43.1-43.6 give `D = (F/(1-F)) × R_peak × T_position`, and the chapter's worked example of 100 MB/s + 10 ms + F=0.9 → 9 MB) + §43.12 "Crash Recovery And The Log" (two CRs at either end of the disk written alternately with the header/body/trailer timestamp protocol; "LFS will always choose to use the most recent CR that has consistent timestamps"). **Correction to the original spec**: the write-amplification model has no interior minimum — §43.9's cleaner reads M segments and writes N<M, so `(T_position/R_peak + 1 + liveRatio)/liveRatio` cancels segment size out and both cost terms fall as segments grow (ADR 0021). Roll-forward recovery and wiring the CR into M25's flush path are deferred. See `m31-lfs-extensions/overview.md`.
- **M32 Block-level FTL** — Ch. 44 §44.9 "Mapping Table Size": the 1-TB / 4-KB-page / 4-byte-entry figure that makes page-level mapping "impractical"; block-based mapping and its `Size_block/Size_page` reduction; hybrid log/data tables with switch, partial and full merge. Implemented in `src/MiniWebServer.Host/MiniScheduler/FtlMapping.cs`. **Three corrections to the original spec**: the mapping table is sized by *capacity*, not by write count (it is indexed by logical address space, so it is `capacity/page_size` entries however many writes arrive); the spec's 256-pages-per-block is not in OSEP — §44.9 says blocks "can be 256KB or larger", which with a 4 KB page is 64 pages/block and a 16 MB table, not 4 MB; and a `LogBuffer` on the block-level FTL would have made it a hybrid FTL and erased the trade-off the chapter is about. Measured on 200 scattered writes: page-level 1.00×, hybrid 3.21×, block-level 63.36×; the same block-level strategy costs 1.28× on sequential writes, because §44.9's penalty is specific to writes smaller than a block. See `m32-ssd-extensions/overview.md` and ADR 0022.
- **M33 Scrubbing schedule** — Ch. 45 §45.7 "By periodically reading through every block of the system... Typical systems schedule scans on a nightly or weekly basis" + §45.8 overheads ("an 8-byte checksum per 4 KB data block, for a 0.19% on-disk space overhead"; background-scrubbing I/O that "can be tuned"). Implemented in `src/MiniWebServer.Host/MiniScheduler/Scrubber.cs`: an incremental sweep whose cursor survives between passes and wraps at the end of the disk, a `Schedule`/`Stop` pair where `Stop` joins the worker, and `CatchProbability` giving `P(caught) = exp(-T/MTTF)` with `T` the per-block sweep period. **Two corrections to the original spec**: the sweep period is `interval × ceil(N/batch)` (the spec's `interval × N/batch` makes a *larger* batch lengthen the period, which is backwards); and the "Poisson-arrival model" was attributed to §45.8, which states no probability formula at all — the model is derived here and the derivation is recorded in the code and ADR 0023. Measured on a 1000-block disk at 100 000 h MTBF: a 24 h whole-disk schedule catches 99.976%, a weekly whole-disk schedule catches 99.832%, a 24 h schedule covering 1% per pass catches 97.629%. A review pass then found five further defects - most seriously a "weekly" candidate built as `interval*7/24` (a 7-*hour* scan labelled weekly, scoring *better* than nightly), an unatomic `Schedule`/`Stop` that could orphan a worker under concurrent calls, a `Stop` that discarded its join result and reported success on timeout, durations `WaitHandle.WaitOne` cannot express, and inputs that closed the connection instead of answering 400. See `m33-integrity-extensions/overview.md` and ADR 0023.
- **M34 Device drivers** — Ch. 36 §36.2 the three-register canonical device, §36.3 the four-step polling protocol and the definition of PIO, §36.4 interrupts (including the chapter's own claim that polling wins on fast devices), §36.5 DMA, §36.6 explicit I/O instructions vs memory-mapped I/O. Implemented in `src/MiniWebServer.Host/MiniScheduler/Device.cs`: a hand-clocked `Device` (a background thread would make the ordering - the whole subject of these sections - depend on scheduling luck), `DeviceCostModel`, and `DeviceDemos`. **Three corrections to the original spec**: `IntPtr hostAddr` is meaningless and unsafe in a simulator, so the DMA API takes the `byte[]` that *is* the simulated host memory; "PIO means zero interrupts" is not the chapter's definition, which is about who moves the bytes; and the spec dropped §36.4's actual conclusion that polling beats interrupts on fast devices, which `interrupt-vs-poll` makes measurable (polling wins at 1 tick, the interrupt at 500). OSEP Ch. 36 states no numbers at all, so the cost constants are invented and named as such; what the chapter fixes is the *shape* - PIO's cost grows with bytes because the CPU copies "one word at a time", DMA's does not. Measured DMA crossover: 140 bytes. A review pass then found six defects, most seriously that a zero-latency device never reported completion and so hung every caller in §36.3's polling loop, and that the port-access path skipped the bounds check the memory-mapped path had. See `m34-device-drivers/overview.md` and ADR 0024.
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
- **ASID rollover is not flushed (M28)**: when the 8-bit ASID wraps after 256 process creations the simulator lets entries alias rather than flushing. Real hardware flushes (or bumps a version register, [SH94]) on rollover. Documented in `m28-asid/overview.md`.
- **M16 keeps the naive full flush**: M16 flushes the whole TLB on every context switch; M28 adds the scoped alternative alongside it. Both policies stay reachable so `/tlb/run` can show the survival difference. Documented in `m16-tlb/overview.md` + `m28-asid/overview.md`.
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

- **Part I Virtualization**: Ch. 5 process API (`fork`/`exec`/`wait` — out of scope for .NET), **Ch. 7 Scheduling: Introduction — FIFO / SJF / STCF / response time / Round Robin / I/O interleaving, not implemented; the repo goes straight from Ch. 6 to MLFQ (§8), so the baseline the later policies are compared against is missing** (see the correction above), Ch. 14 memory API (`malloc`/`free` — superseded by the GC), Ch. 15 address translation (base+bound relocation), Ch. 16 segmentation; **Ch. 17 free-space management — not covered: M12's bitmap records what is free but `MiniFs` allocates first fit by default rather than by decision, so §17.2-§17.4's mechanisms and strategies are absent; this is M36**; §19.6 TLB replacement + §19.5 multilevel TLB + §19.7 ASID rollover flush + x86 PCID, §20.4 inverted page tables, §21.4-§21.6 stack property + approximated LRU + dirty pages, §23.1 VMS demand-zeroing + RSS + segmented FIFO + second-chance list, §23.2 Linux 2Q + huge pages + 4-level PTs + NX + ASLR + KPTI
- **Part II Concurrency**: Ch. 32.2 atomicity/order bugs (M29 provides the CV primitive but doesn't extend M23's crypto/auth paths with atomicity/order-bug fixes); Ch. 32.3 detect-and-recover (wait-for graph + cycle detection — the third school, not implemented); partial lock ordering (two locks cannot show Linux `mm/filemap.c`'s ten groups); Ch. 29 deeper lock-free data structures beyond §29.1-§29.2 (DFTL, Michael-Scott queue, hazard pointers) deferred; Ch. 31 in depth beyond §31.4-§31.6.
- **Part III Persistence**: Ch. 36 §36.7-§36.10 device-driver abstraction + IDE case study + historical notes (M34 covers §36.2-§36.6 only); Ch. 43 §43.12 roll-forward recovery (M31 covers the two-CR protocol, not the log replay); Ch. 43 §43.13 WAFL/ZFS/btrfs comparison + §43.11 hot/cold segregation beyond M25's coldest-segment-first; Ch. 44 §44.9 hybrid merge classification is simplified (by chunk count rather than by where the sibling pages live); Ch. 44 §44.6 direct-mapped FTL + §44.10 active wear leveling; Ch. 45 §45.1 LSE clustering ("Most disks with LSEs have less than 50" bad sectors — M33's catch model assumes a uniform MTTF); §45.8 ZFS-style end-to-end checksum tree (M27 already deferred). The non-spec-only Ch. 38-45 coverage is closed: M12 covers Ch. 40 vsfs + Ch. 42.3 journaling; M21 covers Ch. 41 FFS placement; M24 covers Ch. 38 RAID levels 0/1/4/5; M25 covers Ch. 43 LFS segments + imap + CR + cleaner; M31 covers Ch. 43 §43.3 segment sizing + §43.12 two-CR recovery; M26 covers Ch. 44 SSD FTL + GC + wear tracking; M32 covers §44.9 block-level + hybrid FTL; M27 covers Ch. 45 data integrity; M33 covers §45.7 + §45.8 scheduling. **Part III Persistence is closed apart from the §-level deferrals listed above**; M34 covers Ch. 36 §36.2-§36.6.
- **Part IV Security**: Ch. 53 §53.5 (system calls + access control primitives), Ch. 54 §54.6 (biometrics) + §54.7 (non-human auth: sudo, setuid), Ch. 55 in depth (ACLs per file, capabilities, mandatory vs discretionary, Android permission model), Ch. 56.3 hybrid encryption (sign-then-encrypt example), Ch. 57 entire except §57.5 key-derivation (X.509 chains, MITM, SSH, HTTPS), key-revocation (§57.6), hardware enclaves (§53 TPM) — M23.1 (password) + M23.2 (at-rest) + M23.3 (RBAC) + M23.4 (PK sign/verify) + M23.5 (TLS-style handshake key derivation) + M23.6 (TOTP) cover most of the canonical Part IV content; remaining is mostly operational/extended PK + biometrics + sudo-equivalent.

The repo is best understood as an **OS concepts lab for the core three pieces**, not a full reproduction of the textbook. Concurrency sweep: race observable → race fixed → pool → async → dining philosophers → lock-free CAS → condition variables (the sweep now has a real CV, and every one of its failure modes runnable) → deadlock prevention + avoidance (every Coffman condition now runnable, each with the technique that breaks it). Persistence sweep: raw I/O → vsfs + journaling → FFS placement → RAID 0/1/4/5 → LFS segments + cleaner → segment sizing + two-CR recovery → SSD FTL + GC + wear → checksums + physical ID + scrubber. Virtualization sweep: thread/process abstraction → MLFQ → proportional-share → multi-CPU → linear paging → TLB → multi-level page tables → replacement policy → COW fork + swap → ASID-tagged TLB (the sweep now survives a context switch instead of being flushed).

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
- `src/MiniWebServer.Host/MiniPager/Tlb.cs` (M16 + M28) is the software-managed TLB. M16 supplied the lookup/insert/evict core; M28 added `TlbEntry.Asid` + `TlbEntry.IsGlobal`, made `Lookup` ASID-aware, and split `Flush()` (full) from `Flush(int? asid)` (scoped).
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` (M28) drives the `flushall-vs-flushasid` demo: runs two processes through the same context-switch schedule under both flush policies and reports per-switch survival counts.
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariable.cs` (M29) is the condition variable: a private FIFO wait queue plus `Wait(lockObj)` / `Signal()` / `Broadcast()` / `WaitWhile(lockObj, predicate)`. Deliberately not a `Monitor` wrapper — two CVs over one monitor share one queue, which would make §30.2's producer/consumer split inexpressible. `Wait` throws `SynchronizationLockException` if the caller does not hold the lock.
- `src/MiniWebServer.Host/MiniScheduler/ConditionVariableDemos.cs` (M29) runs the four OSEP Ch. 30 scenarios (`lost-wakeup`, `single-cv`, `two-cv`, `covering-condition`) behind `/cv/run`. The broken cases hang by design, so every scenario runs under a bounded join and reports the timeout as data.
- `src/MiniWebServer.Host/MiniScheduler/DeadlockSim.cs` (M30) is the deadlock lab: `ResourceLock` (an ordering id + the `object` monitor), the five `naive|ordering|batch|preempt|banker` scenarios behind `/deadlock/run`, and the `Banker` class (safe-sequence admission). Both the broken case and the trylock fix are staged behind a barrier, because unstaged neither reliably reproduces.
- `src/MiniWebServer.Host/MiniScheduler/SegmentSizer.cs` (M31) holds two independent pieces: `SegmentSizer` (equation 43.6 and the effective-rate formula it inverts, kept separate so a test can round-trip one into the other) and `DualCheckpointRegion` (the §43.12 two-CR alternation with header/body/trailer timestamps). Timestamps are a monotonic counter, not wall-clock.
- `src/MiniWebServer.Host/MiniScheduler/LfsExtensionsDemos.cs` (M31) runs the five §43.3/§43.12 scenarios behind `/lfs/run`, dispatched before the M25 simulator is constructed.
- `src/MiniWebServer.Host/MiniScheduler/FtlMapping.cs` (M32) holds the §44.9 mapping strategies: `FtlMappingCost` (pure capacity arithmetic, so the 1 TB figures need no device), `RawFlash` (program/erase rules and wear counts shared by all three), `PageLevelFtl` / `BlockLevelFtl` / `HybridFtl` behind one `IFtlStrategy`, and `HybridMergeDemo`. M26's `Ssd` keeps its own copy of the flash rules because it *is* a page-level FTL rather than a layer above one.
- `src/MiniWebServer.Host/MiniScheduler/FtlDemos.cs` (M32) formats the three `/ssd/run` scenarios added for §44.9, dispatched before the M26 simulator is constructed.
- `src/MiniWebServer.Host/MiniScheduler/Scrubber.cs` (M33) holds the §45.7 scheduling layer: `Scrubber` (incremental sweep with a persistent wrapping cursor, plus `Schedule`/`Stop` where `Stop` joins the worker), `CatchProbability` (the derived `exp(-T/MTTF)` model and the sweep-period arithmetic), and `ChecksumOverhead` (§45.8's space figures). M27's `IntegrityStore` is untouched.
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
- **Condition variable**: A synchronization primitive that lets a thread atomically release a lock and block until another thread signals it. Paired with a **predicate** (shared state guarded by the same lock) that the waiter re-checks on every wakeup. M6 hand-rolls the pattern with `Monitor.Wait`/`Monitor.Pulse` for the producer/consumer queue; M29 promotes it to a first-class primitive.
  _Avoid_: condvar, condition (alone), notification (the signal is a hint, not a guarantee of handoff)
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

### Pager / VM (M14, M16–M19, M28)

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
- **ASID** (Address Space Identifier, M28, OSEP §19.5 + §19.7): a small integer tag (typically 8 bits, matching MIPS R4000 [H93]) added to each TLB entry identifying which address space the entry belongs to. Introduced in OSEP §19.5 as the solution to the context-switch TLB-flush problem; §19.7 makes the abstract idea concrete with the MIPS R4000 entry format (which has both an ASID field and a Global bit). Replaces the per-context-switch full flush with a per-ASID flush; entries for other ASIDs (or kernel global entries) survive.
  _Avoid_: PID (PID is 32 bits on Linux; ASID is the smaller TLB-tag-sized equivalent), PCID (that is x86's separate 12-bit mechanism, not modeled)
- **Global bit / G bit** (M28, OSEP §19.7): the TLB-entry flag that, when set, makes the entry match any ASID. Used for kernel mappings that live in every address space. Also makes the entry immune to a per-ASID flush — that survival is the whole point of the flag.
- **Per-ASID flush** (M28): invalidating only the entries whose `Asid` matches the switching-away address space and whose `IsGlobal` is false. Distinct from the **full flush**, which drops every entry and is what a kernel PTE edit requires (the kernel's mapping changed, so stale copies must not survive).
  _Avoid_: flush all (ambiguous between the two policies; the route labels them `flushall` vs `flushasid`)

### Scheduling (M13, M13.2, M13.3)

- **SQMS** (Single-Queue Multiprocessor Scheduling, OSEP §10.4): one shared ready queue for all CPUs. Simple but suffers from cache-line contention.
- **MQMS** (Multi-Queue Multiprocessor Scheduling, OSEP §10.5): one ready queue per CPU. Better cache behaviour but can leave one CPU idle while another is overloaded.
- **Work-stealing** (OSEP §10.5): a load-balancing scheme where an idle CPU pulls a job from a busy CPU's queue. MQMS + work-stealing is the standard Linux approach.
- **Cache affinity** (OSEP §10.5): a scheduler's preference for keeping a thread on the CPU it last ran on, to preserve warm cache lines.

### Concurrency deep-dive (M22, M29, M30)

- **Condition variable wait queue** (M29, OSEP §30.1): the explicit queue a CV owns, distinct from the lock's. Two CVs over the same lock are **two queues** in POSIX and must be: that separation is what lets a consumer signal "a slot is free" without waking another consumer.
  _Avoid_: monitor wait queue (that is .NET's fused mutex+CV object, keyed by lock — see ADR 0019)
- **Predicate** (M29, OSEP §30.1): the shared state, guarded by the same lock, that a CV wait is really about. "The sleeping, waking, and locking all are built around it." A CV without one is the lost-wakeup bug.
- **Mesa vs Hoare semantics** (M29, OSEP §30.1): two flavours of CV wakeup. Mesa (the C# `Monitor` default) signals as a hint — the waker keeps the lock until it leaves the critical section; the waiter doesn't immediately run. Hoare semantics transfer the lock atomically. Almost every system uses Mesa.
- **Spurious wakeup** (M29, OSEP §30.2 TIP): a wakeup from a CV that has no producer-side reason. POSIX allows them; C# `Monitor.Wait` can return spuriously. The defensive idiom is a `while` loop — never `if`. M29's `WaitWhile` takes the predicate in the "still blocked" sense (`while (predicate) Wait()`), matching `SpinWait.SpinWhile` and OSEP's own `while (count == 0) pthread_cond_wait(...)`.
- **Covering condition** (M29, OSEP §30.3): a CV on which `Broadcast()` is used to wake all waiters because the waker can't tell which one can make progress. The defensive `while` loop on each waiter re-checks and re-sleeps the ones that aren't ready.
- **Deadlock** (M30, OSEP §32.3 [C+71]): a state where N threads each hold some locks and wait for others to release theirs, forming a cycle. No thread can make progress.
- **Coffman conditions** (M30, OSEP §32.3 [C+71]): the four preconditions for deadlock — mutual exclusion, hold-and-wait, no preemption, circular wait. "If any of these four conditions are not met, deadlock cannot occur." Each prevention technique breaks exactly one.
- **Lock ordering** (M30, OSEP §32.3 prevention): giving every lock a total-order key and requiring threads to acquire in ascending key order. Breaks circular wait. OSEP §32.3 calls it "probably the most practical prevention technique" and points at Linux `mm/filemap.c` [T+94] for a partial ordering (10 lock groups). The chapter's TIP derives the key from the lock *address*; M30 uses `ResourceLock.Id`, since a C# `object` has no usable address ordering. A convention, not a guarantee — §32.3 notes "a sloppy programmer can easily ignore the locking protocol".
- **Atomic batch acquire / `AcquireAll`** (M30, OSEP §32.3 prevention): taking every lock a thread needs behind one global `prevention` lock, so no thread is ever part-way through a set. Breaks hold-and-wait. §32.3 names the cost: "likely to decrease concurrency as all locks must be acquired early on".
- **Preemption via trylock + backoff** (M30, OSEP §32.3 prevention): take the first lock, `try` the second, and on failure release the first and retry after a random delay. Breaks no-preemption — though §32.3 is explicit this "doesn't really add preemption ... but rather uses the trylock approach to allow a developer to back out of lock ownership". Livelock is the residual risk; the random delay is the chapter's cure.
- **Livelock** (M30, OSEP §32.3): threads "repeatedly attempting this sequence and repeatedly failing ... progress is not being made". Not a deadlock — every thread is runnable — but the system achieves nothing.
- **Safe sequence** (M30, Dijkstra 1964 [D64]): an ordering of all threads in which each can obtain its full remaining need from what the earlier ones release. A state is *safe* iff one exists; that is the test avoidance applies at runtime.
- **Banker's algorithm** (M30, Dijkstra 1964 [D64]): avoidance rather than prevention — admit a request only if the resulting state is safe. Needs each thread's maximum claim in advance, which is why §32.3 calls such approaches "only useful in very limited environments".
  _Avoid_: Banker (without 's algorithm), citing §32.3 for the tables — the chapter names the algorithm and calls it limited, but does not restate it.

### Storage / Mini FS (M11, M12, M21)

- **Inode**: An on-disk index node. Holds a file's metadata (size, block pointers) and is addressed by an inode number.
  _Avoid_: i-node (hyphenated form), file metadata record
- **Superblock**: The on-disk structure that describes the filesystem layout (block size, total blocks, free-block count, inode count).
- **Bitmap**: A bit array where each bit tracks whether the corresponding resource (block or inode) is free (0) or allocated (1).
- **Free-space allocation** (OSEP Ch. 17, planned M36): the policy that decides *which* free block a request takes. M12 and M21 own bitmaps — the structure that records what is free — but neither picks a block; both scan for the first clear bit. The allocation *policy* (first fit / best fit / next fit / buddy) is the gap, and it is what §17.3 and §17.4 are about.
  _Avoid_: free-list (a specific §17.2 *mechanism* for storing free blocks — stack, linked list, or bitmap — not the policy that consumes it), bitmap allocation (the bitmap is how free blocks are recorded; allocation is the decision made from it)
- **Fragmentation** (OSEP §17.3): **external** — free blocks exist but none is contiguous enough for the request, so total free space overstates what is allocatable. **Internal** — a block is allocated but only partly used. Only external fragmentation is intrinsic to a free-block filesystem; internal fragmentation is the cost of fixed-size blocks. Free-space policy is largely a war against the external kind.
- **Buddy system** (OSEP §17.4): allocation by powers of two, where each block's buddy is its XOR partner and freeing a block coalesces it with its buddy if the buddy is also free. Gives O(log n) allocation and cheap coalescing, at the cost of internal fragmentation when sizes are not powers of two.
  _Avoid_: buddy allocator (same thing, but prefer the chapter's name when citing §17.4)
- **External fragmentation** / **internal fragmentation**: see **Fragmentation**. The split matters because only the external kind is something a *policy* can reduce, and conflating them makes "our allocator fragments" untestable.
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

### LFS (M25, M31)

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
- **Segment-size cost model** (M31, OSEP §43.3): the rule that the minimum segment size to achieve a fraction `F` of peak bandwidth is `D = (F / (1 - F)) × R_peak × T_position` (equation 43.6). With T_position = 10 ms, R_peak = 100 MB/s, F = 0.9 → D = 9 MB. The chapter's follow-up is the point: 95% needs 19 MB and 99% needs 99 MB, because `F/(1-F)` blows up.
  _Avoid_: optimal segment size (the formula sizes the *write path* only; it makes no total-cost claim)
- **Two-CR alternation** (M31, OSEP §43.12): "LFS actually keeps two CRs, one at either end of the disk, and writes to them alternately", with a header/body/trailer timestamp protocol so a crash mid-update is detectable as `header ≠ trailer`. Recovery mounts "the most recent CR that has consistent timestamps".
  _Avoid_: CR redundancy (the point is redundancy of *validity*, not of data), checkpoint mirroring

### SSD / Flash (M26)

- **Flash page**: the smallest unit of flash I/O — typically 4 KB (OSEP §44.2). The M26 simulator models one-byte pages so the state transitions are visible in the smoke trace.
- **Flash block / erase block**: the unit of flash erase — typically 128 KB or 256 KB, containing many pages (OSEP §44.2). Erasing a block is expensive (~ms); programming a page is cheaper (~100s of µs); reading a page is fastest (~10s of µs).
- **Page states** (OSEP §44.3): `INVALID → ERASED → VALID`. The M26 simulator adds a fourth logical state, `DEAD`, meaning the page's data has been overwritten elsewhere and is eligible for GC.
- **FTL** (Flash Translation Layer): the firmware inside an SSD that turns client `read(LBA)` / `write(LBA)` calls into the underlying read/program/erase sequence (OSEP §44.5). The FTL keeps a mapping table `LBA → physical page` in memory.
- **Log-structured FTL** (OSEP §44.7): the standard FTL approach — writes append to the next free page in a "log block"; the mapping table redirects the LBA to wherever the data ended up. Avoids the read-modify-write cost of direct-mapped FTLs and spreads write wear across blocks.
- **Mapping table**: `LBA → physical page` index (OSEP §44.7). Page-level mapping (one entry per page) is the most flexible but uses the most memory — a 1 TB SSD with 4 KB pages needs 1 GB of mapping memory (OSEP §44.9).
- **Garbage collection** (OSEP §44.8): the background process that reclaims dead pages. Picks a block with many dead pages, migrates the live pages to the log, and erases the block so it can be reused. Garbage collection drives write amplification when too aggressive.
- **Write amplification**: ratio of physical writes (to the device) to logical writes (from the client). One concept, shared by LFS and SSD/flash: in both, garbage collection/cleaning is what drives it, and a naïve scheme can write each logical byte 2–4 times. In LFS (M25, M31) the cleaner + segment-size choice set the multiplier; in flash (M26) the GC merge strategy sets it.
- **Wear leveling** (OSEP §44.10): the FTL's responsibility to spread erase cycles evenly across all blocks so no single block wears out before the others. The log-structured design + GC do most of this for free; active wear leveling also migrates cold data.
- **TRIM** (OSEP §44.12): a hint the filesystem sends to the SSD when a file is deleted — "you can forget about this LBA range". Without TRIM, the SSD keeps old data around as "garbage" because it can't tell which pages are still logically in use.
- **Over-provisioning**: the practice of reserving some flash capacity (typically 7–28%) for GC headroom, hidden from the client. Real SSDs do this; the M26 simulator does not.
- **Page-level FTL** (M26, OSEP §44.7): every LBA maps to one physical page; the mapping table has one entry per page. Maximum flexibility, highest RAM cost (~1 GB / TB at 4 KB pages; OSEP §44.9 calls this "impractical for 1 TB devices").
- **Block-level FTL** (M32, OSEP §44.9 [KK+02]): every logical block maps to one physical block; the page offset is preserved. The mapping table is `1 / pagesPerBlock` the size of page-level. Writes that don't fill a block go through a small block-level log buffer.
- **Hybrid (log-block) FTL** (M32, OSEP §44.9 [KK+02]): the FTL keeps a few erased **log blocks** with per-page mappings, plus per-block mappings for the rest. Implements switch merge (best case, no extra I/O), partial merge (1 extra read per merged page), and full merge (drives up write amplification).
- **DFTL** (Demand-based FTL, [GP07], deferred): a refinement that page-maps only the cached mapping entries; the rest live on flash. Out of scope here.
- **Log table / data table** (M32, OSEP §44.9): the hybrid FTL's two tables. The **log table** holds per-*page* pointers for writes that have landed in the small set of reserved **log blocks**; the **data table** holds per-*block* pointers for everything already cleaned. Lookup consults the log table first. The log table is the only per-page memory a hybrid adds, and it is bounded by the log-block budget rather than by capacity — that boundedness is what makes a hybrid affordable.
  _Avoid_: block buffer, write buffer (both ambiguous: "write buffer" in LFS §43.2 is a different concept, and a buffer of data is not a table of pointers)
- **Switch merge** (M32, OSEP §44.9): the best case — the log block already holds every page of its chunk, in the right offsets, so the data table is repointed at it and **nothing is copied**. Still requires erasing the block it replaced; zero *copying* is not zero I/O.
- **Partial merge** (M32, OSEP §44.9): the log block holds part of a chunk; the remaining pages are read from their *sibling* locations in the data blocks and copied in.
- **Full merge** (M32, OSEP §44.9): the worst case — the log block holds few enough pages of its chunk that siblings must be pulled from *many* other blocks. This is what drives write amplification, and §44.9 notes frequent full merges "can seriously harm performance".
  _Avoid_: merge generically without the kind (M32's `PagesCopiedOnMerge` is only meaningful per kind)

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
- **Scrubbing schedule** (M33, OSEP §45.7 + §45.8): the periodic invocation of the scrubber — every `interval`, covering `batchSize` blocks per pass, with a throttle between passes so it doesn't starve foreground I/O. A pass resumes where the previous one stopped and wraps at the end of the disk; a pass that restarted at block 0 would never reach the tail. OSEP §45.7 notes "Typical systems schedule scans on a nightly or weekly basis"; §45.8 quantifies the space (~0.19% for 8-byte per 4 KB) and CPU overheads that motivate why scrubbing isn't free.
- **Scrubbing catch probability** (M33): the probability that a latent corruption in a block is detected before the next corruption of that same block masks it. Derived, not quoted — OSEP states no formula. Errors on one block arrive as a Poisson process of rate `λ = 1/MTTF`, so the gap `G` to the next corruption is exponential; the corruption is caught iff a scrub visit lands first, i.e. `G > t` where `t` is the per-block sweep period. Hence `P(caught) = P(G > t) = e^(-t/MTTF)`. The limits are the check: `t → 0` gives 1, `t → ∞` gives 0. (The complement `1 - e^(-λt)` is the probability of *missing* the corruption, which is the trap this formula is easy to fall into.)

### I/O devices (M34)

- **Device register** (M34, OSEP §36.2): a small piece of memory or I/O port that the CPU reads/writes to communicate with a device. OSEP figure 36.3 shows the canonical 3-register device: status, command, data.
- **MMIO** (Memory-Mapped I/O, M34, OSEP §36.6): mapping device registers into the regular address space so the CPU accesses them with normal load/store instructions. The standard on modern hardware (PCIe, ARM SoCs). OSEP: "the hardware makes device registers available as if they were memory locations. To access a particular register, the OS issues a load (to read) or store (to write) the address; the hardware then routes the load/store to the device instead of main memory."
- **PIO** (Programmed I/O, M34, OSEP §36.3): the CPU moves every byte itself. OSEP: "When the main CPU is involved with the data movement (as in this example protocol), we refer to it as programmed I/O (PIO)." Note this is a statement about *who copies*, not about how completion is signalled — PIO and interrupts are not opposites. M34 models both `CanonicalRead` (memory-mapped register access) and `CanonicalReadViaPorts` (port reads) at identical cost, per §36.6: "There is not some great advantage to one approach or the other."
- **DMA** (Direct Memory Access, M34, OSEP §36.5): the device reads/writes main memory directly without going through the CPU. OSEP: "A DMA engine is essentially a very specific device within a system that can orchestrate transfers between devices and main memory without much CPU intervention." Burns fewer CPU cycles than PIO for large transfers.
- **Canonical protocol** (M34, OSEP §36.3): the four-step device-driver dance — poll `Status` until not busy → write `Data` → write `Command` → poll `Status` until done. "The protocol has four steps."
- **Interrupt-driven I/O** (M34, OSEP §36.4): the device raises a hardware signal (IRQ) when it needs the CPU's attention. The OS issues the request, puts the calling process to sleep, and context-switches to another task. "When the device is finally finished with the operation, it will raise a hardware interrupt, causing the CPU to jump into the OS at a predetermined interrupt service routine (ISR) or more simply an interrupt handler." Beats busy-polling for I/O-bound workloads; OSEP §36.4 TIP notes that for *fast* devices, polling may still be better because the cost of interrupt handling + context switching outweighs the savings.
- **Polling** (M34, OSEP §36.3-§36.4): the CPU re-reads the status register in a loop until the device is no longer busy. Costs one register read per check, so the CPU time grows linearly with device latency; burns the CPU while it spins; needs no interrupt hardware. Cheap on a fast device, expensive on a slow one, because the fixed interrupt cost it avoids is constant while the polling it substitutes for is not.
  _Avoid_: busy-wait (informal), spin (which in this repo also means `SpinWait`)
- **Hybrid (poll-then-interrupt) strategy** (M34, OSEP §36.4): "it may be best to use a hybrid that polls for a little while and then, if the device is not yet finished, uses interrupts." The chapter calls this "the best of both worlds", but in M34's cost model the hybrid is never *strictly* cheapest: on a slow device it pays the interrupt's fixed cost *after* polling first, so it costs 49 cycles against the interrupt's 41 at 500 ticks of latency. It does win both comparisons that hold — never worse than polling, never worse than the interrupt.
  _Avoid_: hybrid FTL (that is M32's Ch. 44 concept, an unrelated use of the same word), best-of-both as a factual claim rather than a quotation

### Scheduling algorithms (M13, M13.2, M13.3)

- **MLFQ** (Multi-Level Feedback Queue, M13.1, OSEP §8): a scheduler that demotes CPU-bound jobs and promotes I/O-bound ones across multiple priority queues, with a periodic priority boost to prevent starvation.
- **Stride scheduling** (M13.2, OSEP §9.6): a proportional-share scheduler. Each job gets a stride inversely proportional to its ticket count; the lowest-pass job runs next. Deterministic.
- **Lottery scheduling** (M13.2, OSEP §9.1-§9.4): a proportional-share scheduler. Each job gets tickets; the scheduler draws a random ticket to pick the next job. Probabilistic.

### Scheduling baselines (Ch. 7 — planned, M35)

Not yet implemented. Named here because §8-§10 present themselves as improvements over these, and the repo has the improvements without the baseline.

- **FIFO / SJF / STCF** (Ch. 7 §7.3-§7.5): the policies whose trade-offs §8's MLFQ exists to escape. No knowledge of the future — the point is that they degrade badly.
- **Response time** (Ch. 7 §7.6): time from job arrival to first running. The metric §7.6 introduces because throughput and average turnaround cannot distinguish an interactive job from a batch one. Distinct from **turnaround time**, which is arrival to completion; a long-running job can have excellent turnaround and terrible response time.
- **Disk scheduling** (Ch. 7 §7.8 and Ch. 37 §37.5): the same round-robin idea applied to disk requests — FCFS, SSTF, SCAN. Ch. 7 introduces I/O interleaving as a scheduling problem; Ch. 37 gives the device geometry that makes the ordering matter.
  _Avoid_: "disk scheduling" for only the Ch. 37 half (Ch. 7 introduces the policies, Ch. 37 gives the geometry — naming one chapter and not the other hides which half is missing). See ADR 0025.

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

Routes (canonical runtime list; *why* each route exists lives in the matching ADR — 0008 → /auth/*, 0009 → /crypto/encrypt|decrypt|keygen|tamper-demo|nonce-reuse-demo|dump, 0010 → /auth/grant|role|/protected/secret, 0011 → /crypto/rsa-keygen|sign|verify|import-pubkey, 0012 → /crypto/handshake, 0013 → /crypto/totp-demo, 0014 → /raid/run, 0015 → /lfs/run, 0016 → /ssd/run, 0017 → /integrity/run, 0018 → /tlb/run, 0019 → /cv/run, 0020 → /deadlock/run, 0021 → /lfs/run?scenario=cost-model|segment-size-sweep|dual-cr-recovery|cr-alternation|cr-crash-during-write):
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
- `/lfs/run?scenario=cost-model|segment-size-sweep` → M31 §43.3; `cost-model` prints the segment size needed for F = 90/95/99% of peak (9 / 19 / 99 MB for the chapter's disk) alongside the fraction actually achieved. `segment-size-sweep` reports effective bandwidth and write amplification per segment size; the cost is monotonically **decreasing** — positioning is amortised over more data, and the cleaner's `(1-liveRatio)/liveRatio` term does not depend on segment size at all.
- `/lfs/run?scenario=dual-cr-recovery|cr-alternation|cr-crash-during-write` → M31 §43.12; `dual-cr-recovery` alternates three CR writes and mounts the newest consistent one; `cr-alternation` shows the CR0/CR1 pattern over N writes; `cr-crash-during-write` tears the in-progress CR (header written, trailer not) and recovers from the other. Neither CR written, or both torn, returns 400.
- `/ssd/run?scenario=write|gc|wear&blocks=N&pages=K` → M26; write a deterministic payload of LBAs (log-structured FTL), then optionally rewrite some LBAs (to introduce dead pages) and run GC, then optionally dump the wear report. Output is the LBA→page mapping table + block-state grid (V/D/E) + wear histogram (erase counts per block).
- `/integrity/run?scenario=compute|corrupt|scrub&blocks=N&blockSize=M` → M27; compute all three checksums (XOR/additive/Fletcher) on a fixed payload, then optionally inject a silent corruption + show detection, then optionally write N blocks + inject faults + run the scrubber + report failures. Output is the block grid (sequence, three checksums, data preview) + scenario trace.
- `/tlb/run?scenario=flushall-vs-flushasid&tlb=N&context_switches=N` → M28; fill a `tlb`-entry TLB with two processes' working sets plus global kernel entries, then run `N` context switches under both policies. Output is a per-switch table: `flushall_survive` is always 0 (M16 behaviour), `flushasid_survive` keeps the incoming process's working set + the global entries, with a `flushasid_breakdown` (`P1`/`P2`/`G` counts, `removed=`).
- `/cv/run?scenario=lost-wakeup|single-cv|two-cv|covering-condition` → M29; run one OSEP Ch. 30 case and print its trace. `lost-wakeup` shows the broken join hanging beside the state-variable form completing; `single-cv` shows the §30.2 one-CV deadlock (staged trace, all threads parked); `two-cv` runs the correct solution and reports the verified-once sum plus max buffer depth; `covering-condition` shows broadcast letting the satisfiable waiter through while the unsatisfiable one re-parks. Broken cases hang by design and are reported as `HUNG` under a bounded join, never wedging the server. Unknown scenario → 400.
- `/deadlock/run?scenario=naive|ordering|batch|preempt|banker` → M30; run one OSEP §32.3 case. All five use the same workload (4 threads, 2 locks, half asking each order). `naive` deadlocks (staged behind a barrier, reported as `DEADLOCK`); `ordering` clears it via a total order on `ResourceLock.Id`; `batch` via a global prevention lock, reporting the serialisation cost; `preempt` via trylock + random backoff, reporting how many times the contended branch fired. `banker` grants a request only when the state stays safe and refuses one that would not. Unknown scenario → 400.

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
- `docs/learning/m28-asid/` (M28 ASID-tagged TLB overview + slice doc)
- `docs/learning/m29-cv/` (M29 condition variables overview + slice doc)
- `docs/learning/m30-deadlock/` (M30 deadlock prevention + avoidance overview + slice doc)
- `docs/learning/m31-lfs-extensions/` (M31 segment sizing + two-CR recovery, 2 slice docs)
- `docs/learning/m32-ssd-extensions/` … `docs/learning/m34-device-drivers/` (M32 block/hybrid FTL, M33 scrubbing schedule, M34 device drivers — each overview + slice doc, all implemented)

All twelve roadmap slices + post-roadmap extensions + the VM paging chain + the multi-CPU/dining/FFS/lock-free quartet + the M23 Part IV suite (.1 password + .2 at-rest + .3 RBAC + .4 PK + .5 handshake + .6 TOTP) + M24 (RAID 0/1/4/5) + M25 (LFS segments + imap + CR + cleaner) + M26 (SSD FTL + GC + wear tracking) + M27 (data integrity: checksums + physical ID + write sequence + scrubber) + M28 (ASID-tagged TLB) + M29 (condition variables) + M30 (deadlock prevention + avoidance) + M31 (LFS segment sizing + two-CR recovery) have learning notes with smoke-test output captured inline. M8 (the first post-roadmap extension) has its own learning note and a 6.3 section appended to the M6 note. The M16-M19 chain + M13.2 + M13.3 + M20 + M21 + M22 + M23 + M24 + M25 + M26 + M27 + M28 + M29 + M30 + M31 + M32 + M33 + M34 all have an `overview.md` and a slice doc under their own folder. **Part III Persistence is closed** — Ch. 38-45 all have working code in this repo, and Ch. 36 §36.2-§36.6 landed as M34.

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
- Extend the **OSEP coverage gaps** in the OSEP Coverage section: paging (Ch. 14-17, §19.5 multilevel TLB + §19.7 ASID rollover flush + x86 PCID, §20.4 inverted PTs, §21.4-§21.6 approximated LRU + dirty pages, §23.1 VMS demand-zeroing + RSS + segmented FIFO + second-chance list, §23.2 Linux 2Q + huge pages + 4-level PTs + NX + ASLR + KPTI), full FS (Ch. 36 §36.7-§36.10 device-driver abstraction + IDE case study + historical notes deferred; Ch. 41 §41.7 sub-blocks + parameterized placement deferred; §43.12 roll-forward recovery deferred; §45.8 ZFS-style end-to-end checksum tree deferred), security (Ch. 53.5 system-call access, Ch. 54.5 PK × TOTP composition, Ch. 54.6 biometrics, Ch. 54.7 sudo / setuid, Ch. 55 ACLs per file / capabilities / mandatory mode / Android permission model, Ch. 56.3 hybrid encryption, Ch. 56.5 PK key selection, Ch. 57.3 X.509 cert chains, Ch. 57.6 key-revocation / replay protection, hardware enclaves §53 TPM), concurrency (Ch. 32.2 atomicity/order bugs in M23, Ch. 32.3 detect-and-recover — wait-for graph + cycle detection, Ch. 29 deeper lock-free data structures — DFTL, Michael-Scott queue, hazard pointers). Each new chapter group should get its own ADR before any slices start.
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
