# Mini Web Server Context

## Status Snapshot

Updated 2026-09-18 (M23.2 added). Legend: ✅ built + experimented + noted · 🟡 planned · ⬜ future.

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
| M22 (22.1) | Lock-free CAS primitives: AtomicCounter + LockFreeStack (Treiber) | Ch. 32.3 | ✅ |
| M23 (23.1) | Password auth: PBKDF2-HMAC-SHA256 + 16-byte salt + timing-safe Verify + fail-safe defaults | Ch. 53.4, Ch. 54.4, Ch. 56.4 | ✅ |
| M23 (23.2) | At-rest encryption: AES-256-GCM + 12-byte nonce + tamper detection + nonce-reuse demo | Ch. 56.2, Ch. 56.4, Ch. 56.5, Ch. 56.6, Ch. 56.7 | ✅ |
| M15 | ArrayPool&lt;byte&gt; in async mode (receive + response buffers) | n/a (perf) | ✅ |

**Milestones**: M1 ✅–M15 ✅, M13.2 ✅, M13.3 ✅, M16 ✅, M17 ✅, M18 ✅, M19 ✅, M20 ✅, M21 ✅, M22 ✅, M23 ✅ (incl. .1 password + .2 at-rest). M8–M15 are the post-roadmap extensions per `docs/adr/0004-extend-broad-concurrency-roadmap.md` (M8–M12), `docs/adr/0006-mini-scheduler-mlfq-proportional-multicpu.md` (M13), `docs/adr/0007-mini-pager-mmu-tlb-multilevel-replacement.md` (M14). The original 12-slice roadmap is closed. **Latest**: M23.2 (At-rest encryption, OSEP §56.2 + §56.7). See `docs/adr/0008-m23-auth-password-hashing.md` (M23.1) + `docs/adr/0009-m23.2-at-rest-encryption.md` (M23.2). Each yielded a small slice with its own overview + smoke trace in `docs/learning/`.
**Tests**: 15 passing.
**Code/runtime**: `net10.0`. Two run modes selectable via `--async` flag: default = bounded worker pool (8 threads) + producer/consumer queue, async = `AcceptAsync` + `Task` per connection with `ReceiveAsync` / `SendAsync`. Port 8080, static files under `wwwroot`.
**Latest commit**: M23.2 (at-rest encryption). New `MiniCrypto/` directory adds `SymmetricCipher.cs` (AES-256-GCM via `System.Security.Cryptography.AesGcm`: 32-byte keys, 12-byte nonces randomly generated per call, 16-byte auth tags; `EncryptWithFixedNonce` is intentionally bad and only used by the nonce-reuse demo) and `AtRestStore.cs` (`ConcurrentDictionary<string, Slot>` storing only `{nonce, ciphertext, tag}`; `Put` wipes the plaintext buffer with `Array.Clear`; auth-tag failure on `Decrypt` is caught at the route boundary and returned as 401). HTTP routes: `/crypto/keygen` (rotate AES key), `/crypto/encrypt?name=X&msg=Y` (returns hex nonce + ciphertext + tag), `/crypto/decrypt?name=X` (auth-tag failures bubble up as 401), `/crypto/tamper-demo` (flips bit 5 of byte 7 of the ciphertext, attempt to decrypt raises `CryptographicException("The computed authentication tag did not match...")`), `/crypto/nonce-reuse-demo` (encrypts two messages with the same `0x42`-padded nonce and proves `c1 ⊕ c2 == p1 ⊕ p2` is `True` — the OSEP §56.5/§56.6 lesson in two lines), `/crypto/dump` (proves storage holds only nonce + len + tag). Smoke evidence in `docs/learning/m23-at-rest-encryption/s1-at-rest.md`. Build clean, 15/15 tests still pass.

## OSTEP Coverage

The book has ~50 chapters. Repo maps **the core three pieces** (Virtualization + Concurrency + Persistence) end-to-end, with detailed §-sub-section citations in each milestone's `overview.md`:

| Piece | Coverage | Roadmap slices |
|---|---|---|
| **Virtualization** | Ch. 4 (§4.1 process, §4.4 states Running/Ready/Blocked); Ch. 6 (§6.1 direct execution, §6.2 syscalls, §6.3 timer interrupt); Ch. 8 (§8.1-§8.5 MLFQ); **Ch. 9 lottery + stride scheduling**; **Ch. 10 multi-CPU scheduling (SQMS / MQMS / work stealing)**; Ch. 13 (address space, implicit); Ch. 18 (linear page table); **Ch. 19 TLB**; **Ch. 20 multi-level page tables**; **Ch. 21 + Ch. 22 replacement policies**; **Ch. 23 complete VM (COW + swapping)**; Ch. 26-27 (thread = point of execution, thread API); Ch. 33 (event-based) | M1, M2, M3, M4, M7, M13, M13.2, M13.3, M14, M16, M17, M18, M19 |
| **Concurrency** | Ch. 26 (§26.4 figure 26.7 the race); Ch. 27 (thread API); Ch. 28 (§28.1 lock abstraction, §28.7 test-and-set, §28.9 CAS, §28.16 two-phase); Ch. 30 (CVs); Ch. 31 (§31.4 bounded buffer, §31.5 reader-writer, **§31.6 dining philosophers**); Ch. 33 (events); **Ch. 32.3 lock-free CAS** | M4, M5, M6, M7, M8, M9, M10, M20, M22 |
| **Persistence** | Ch. 36 (I/O devices — TCP receive loop uses kernel async I/O); Ch. 39 (§39.3 open, §39.4 read/write, §39.13 rmdir); Ch. 40 (§40.2 vsfs layout, §40.3 inode, §40.4 directory, §40.5 free space, §40.6 access path, §40.7 caching); **Ch. 41 FFS (cylinder groups, locality, large-file exception, filespan/dirspan)**; Ch. 42.3 (data journaling, recovery, batching, circular log, [Tricky Case: Block Reuse] deferred) | M1, M2, M3, M11, M12.1–12.7, M21 |
| **Security** | Ch. 53.4 Saltzer-Schroeder design principles; Ch. 54.4 password storage (hash + salt + slow KDF); Ch. 56.2 symmetric cryptography (AES-256-GCM for at-rest); Ch. 56.4 cryptographic hashes + integrity; **Ch. 56.5 brute force / WEP / key selection / auth tags**; **Ch. 56.6 cryptography + OSes** (keys in process memory, not stable storage); **Ch. 56.7 at-rest encryption** — Ch. 55 ACLs/capabilities/RBAC, Ch. 54.5 auth-by-token, Ch. 54.6 biometrics, Ch. 56.3 PK, Ch. 56.5 PK key gen, **Ch. 57 TLS / SSL / certificates / MITM entirely deferred** | M23 (incl. M23.1 password + M23.2 at-rest) |

**Roughly 62% of OSEP chapters have working code in this repo**, with §-sub-section coverage noted in each milestone's `overview.md`.

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
- **M16 TLB** — Ch. 19 (§19.1 simple example, §19.2 TLB as cache, §19.3 who handles the TLB miss, §19.5 TLB issue: context switch; §19.4 ASID / §19.6 other TLB issues deferred). See `m16-tlb/overview.md`.
- **M17 Multi-level page table** — Ch. 20 (§20.1 simple example, §20.2 multi-level, §20.3 more than two levels; §20.4 invert page tables deferred). See `m17-multi-level-pt/overview.md`.
- **M18 Replacement** — Ch. 21 (§21.1 cache memory review, §21.2 average memory access time, §21.3 simple policies: optimal/FIFO/Random/LRU; §21.4 stack-property analysis, §21.5 approximating LRU, §21.6 considering dirty pages deferred). Ch. 22 (§22.1 background, §22.2 segment, §22.3 considering workloads, §22.4 considering write cost deferred — we apply policy at eviction only). See `m18-replacement/overview.md`.
- **M19 Complete VM** — Ch. 23 §23.1 VMS (demand zeroing deferred; COW implemented; segmented FIFO + second-chance list deferred; RSS per process deferred); §23.2 Linux (COW implemented; 2Q, huge pages, 4-level PTs, NX, ASLR, KPTI all deferred; TLB not re-impl'd since M16 already covers it). See `m19-complete-vm/overview.md`.
- **M20 Dining philosophers** — Ch. 31.6 (broken solution = deadlock; Dijkstra's fix = last philosopher reverses order). See `m20-dining-philosophers/overview.md`.
- **M22 Lock-free** — Ch. 32.3 "Mutual Exclusion" (CAS-based `AtomicIncrement` + lock-free list insert / Treiber stack). ABA mitigation by never freeing popped nodes; livelock mitigated by `SpinWait`. See `m22-lock-free/overview.md`.
- **M21 FFS** — Ch. 41 §41.3 (cylinder/block groups + per-group bitmaps), §41.4 (locality policies: dirs in low-density group + files in parent's group), §41.6 (large-file exception with N-block rotation), §41.7 (filespan + dirspan metrics). §41.7 sub-blocks + parameterized placement deferred. See `m21-ffs/overview.md`.
- **M15 arraypool** — performance only; closest OSEP reference is Ch. 40.7 (caching). See `m15-arraypool/overview.md`.
- **M23 Password auth** — Ch. 53.4 Saltzer-Schroeder fail-safe defaults (identical "invalid credentials" response for both "no such user" and "wrong password"; dummy PBKDF2 when username is missing so the response time doesn't leak it); Ch. 54.4 password storage: PBKDF2-HMAC-SHA256 with 100k iterations + 16-byte random salt + constant-time compare via `CryptographicOperations.FixedTimeEquals`; Ch. 56.4 cryptographic hash foundations (PBKDF2 is built on HMAC-SHA256). **Out of scope**: TLS (Ch. 57), RBAC/ACLs (Ch. 55), MFA, account lockout, persistence. See `m23-auth/overview.md` and `docs/adr/0008-m23-auth-password-hashing.md`.
- **M23.2 At-rest encryption** — Ch. 56.2 symmetric crypto (AES-256 via `System.Security.Cryptography.AesGcm`); Ch. 56.4 cryptographic hashes + integrity (GCM's 128-bit auth tag fails closed); Ch. 56.5 brute force + key selection (never use weak keys; the per-session 256-bit key is `RandomNumberGenerator`-sourced); Ch. 56.6 cryptography + OSes (key lives in process RAM only — a compromised OS reading our key is exactly the threat OSEP §56.6 names); Ch. 56.7 at-rest encryption (the chapter the slice is named after: if the device is stolen, the blocks are useless without the in-memory key). **Out of scope**: TLS handshake (Ch. 57), PK cryptography (Ch. 56.3), TPM-backed keys (Ch. 53 security enclaves), encrypting `minifs.img` blocks at the M12 layer. See `m23-at-rest-encryption/overview.md` and `docs/adr/0009-m23.2-at-rest-encryption.md`.

### Deviations from OSEP (consolidated)

- **MLFQ §8.4 anti-gaming Rule 4**: we implement §8.2 (R4a + R4b with `YieldsEarly` flag) not §8.4 (allotment tracking). Documented in `m13-mlfq/overview.md`.
- **Stride tie-breaking**: Stride's "lowest pass wins" tie is broken by first-found order (deterministic) rather than random — OSEP doesn't specify. Documented in `m13.2-stride-lottery/overview.md`.
- **Multi-CPU §10.1 cache coherence**: jobs are abstract work units; no real MSI/MESI protocol modeled. Documented in `m13.3-multicpu-scheduling/overview.md`.
- **M19 refcount for last-shareer-frees**: COW works for one-shareer (P1 writes → P1 gets private, P2 keeps shared). Multi-shareer (>2 procs sharing a frame) is documented but a per-frame refcount for "free on last unshare" is deferred. Documented in `m19-complete-vm/overview.md`.
- **Dining philosophers we run with thinkMs=0 eatMs=0 to force the deadlock**: in production each philosopher would normally spend real time between cycles, which would let some lucky ones escape the cycle. With thinking time, the deadlock is racy rather than deterministic.
- **Lock-free ABA**: we never free popped stack nodes; they leak. A production lock-free stack would use hazard pointers [Michael 2004] or epoch-based reclamation. Documented in `m22-lock-free/overview.md`.
- **Pager TranslateOutcome naming**: our `PageFault` label conflates OSEP's `SEGMENTATION_FAULT` (no valid PTE) with the literal "page fault" (valid + not present, needs swap). M14.5 will fix. Documented in `m14-pager/overview.md`.
- **TLB global flush on context switch**: we don't implement ASID (§19.4). M16.1 has a `FlushTlb()` method for context switches but no ASID-tagged entries. Documented in `m16-tlb/overview.md`.
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

Chapters **not yet implemented** (natural next slices):

- **Part I Virtualization**: Ch. 7 process API (out of scope for .NET), Ch. 14-17 base+bound / segmentation / free-space mgmt (superseded by paging), §19.4 ASID, §20.4 inverted page tables, §21.4-§21.6 stack property + approximated LRU + dirty pages, §23.1 VMS demand-zeroing + RSS + segmented FIFO + second-chance list, §23.2 Linux 2Q + huge pages + 4-level PTs + NX + ASLR + KPTI
- **Part II Concurrency**: Ch. 29 lock-free data structures — partial coverage in M22 (CAS primitives); Ch. 32.2 atomicity/order bugs (CV fixes for the original cases), Ch. 32.3 deadlock prevention/avoidance (lock ordering, hold-and-wait, etc.)
- **Part III Persistence**: Ch. 36-38 device drivers & RAID, Ch. 43 LFS, Ch. 44 flash, Ch. 45 data integrity — M12 covers Ch. 40 vsfs + Ch. 42.3 journaling; M21 covers Ch. 41 FFS placement
- **Part IV Security**: Ch. 53 §53.5 (system calls + access control primitives), Ch. 54 §54.5 (auth by what you have) + §54.6 (biometrics) + §54.7 (non-human auth: sudo, setuid), Ch. 55 in depth (ACLs, capabilities, RBAC, mandatory vs discretionary, Android permission model), Ch. 56.3 PK crypto + key signing, Ch. 56.5 PK key selection, Ch. 57 entire (TLS, certificates, MITM, SSH, HTTPS), key-revocation (§57.6), hardware enclaves (§53 TPM) — M23.1 covers §53.4 + §54.4 + §56.4, M23.2 covers §56.2 + §56.4 + §56.5 + §56.6 + §56.7; remaining Part IV deferred.

The repo is best understood as an **OS concepts lab for the core three pieces**, not a full reproduction of the textbook. The concurrency chapter sweep (race observable → race fixed → pool → async → dining philosophers → lock-free CAS) is now substantial; persistence is reduced to "serve files from a directory + journal for crash safety"; virtualization is now substantial (thread/process abstraction + MLFQ + proportional-share + multi-CPU + linear paging + TLB + multi-level page tables + replacement policy + COW).

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

- **Host**: The executable process that owns the socket server and runs continuously until stopped.
- **Server socket**: The listening TCP socket created by the host with `AddressFamily.InterNetwork`, `SocketType.Stream`, and `ProtocolType.Tcp`.
- **Endpoint**: The address and port the server binds to. The current endpoint is `IPAddress.Any` on port `8080`.
- **Listen backlog**: The maximum queue length for pending connections. The current backlog is `10`.
- **Client socket**: The accepted per-connection socket returned by `serverSocket.Accept()`.
- **Raw HTTP request**: Bytes received from a client socket and decoded as UTF-8 for console logging.
- **Parsed HTTP request**: A structured view of the raw request text containing method, path, version, and headers.
- **Request line**: The first line of an HTTP request, such as `GET /ostep HTTP/1.1`.
- **Header**: A `Name: Value` line after the request line, parsed into the request header dictionary.
- **Raw HTTP response**: A manually formatted HTTP/1.1 response string sent over the client socket.
- **Static file response**: A response generated by reading bytes from a file under `wwwroot`.
- **Web root**: The directory files can be served from. The source web root is `src/MiniWebServer.Host/wwwroot`; at runtime it is copied beside the host executable.
- **Path traversal**: A request path such as `/../CONTEXT.md` that tries to escape the web root. These requests return `404 Not Found`.
- **Connection close**: The server closes the client socket after sending the response.
- **PBKDF2**: Password-Based Key Derivation Function 2 (RFC 8018). A deliberately slow keyed hash designed to be tunable in cost via an iteration count. The M23 default of 100k iterations matches the OWASP 2024 minimum for password storage.
- **Salt**: A per-user random byte string concatenated to the password before PBKDF2 hashing. Defeats precomputed rainbow tables since identical passwords yield different hashes for different users (OSEP §54.4).
- **Constant-time compare**: A byte-array equality check that does not short-circuit on the first differing byte. `CryptographicOperations.FixedTimeEquals` in .NET. Defends against timing side-channels in password verification.
- **Fail-safe defaults** (OSEP §53.4): an authorization system that defaults to denying access on errors, returns identical responses for distinct failure modes so attackers can't enumerate valid users, and never reveals information that a properly authenticated user wouldn't need.
- **AES-GCM**: an authenticated-encryption mode that bundles confidentiality (AES-256) with integrity (a 128-bit authentication tag). GCM tag failure raises an exception; the cipher fails closed. `System.Security.Cryptography.AesGcm` in .NET.
- **Nonce**: a number used once. AES-GCM requires a unique 12-byte nonce per encrypt call. Reusing a `(key, nonce)` pair leaks `c1 ⊕ c2 == p1 ⊕ p2` (OSEP §56.5 + §56.6). M23.2 always uses a fresh nonce from `RandomNumberGenerator`.
- **At-rest encryption**: protecting data while it sits in storage (disk, RAM, backup) so that stealing the storage doesn't yield plaintext (OSEP §56.7). Requires that the decryption key NOT be in the same place as the encrypted data — for a single-process lab, that means the key lives only in process RAM.

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

Routes:

- `/` → `wwwroot/index.html` (200) or 404
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

All twelve roadmap slices + post-roadmap extensions + the VM paging chain + the multi-CPU/dining/FFS/lock-free quartet + M23.1 (password auth) + M23.2 (at-rest encryption) have learning notes with smoke-test output captured inline. M8 (the first post-roadmap extension) has its own learning note and a 6.3 section appended to the M6 note. The M16-M19 chain + M13.2 + M13.3 + M20 + M21 + M22 + M23 each have an `overview.md` and a slice doc under their own folder; M23.2 lives under `m23-at-rest-encryption/`.

## Design Intent

Prefer preserving the educational, low-level socket-server character of the project unless a task explicitly asks for a higher-level framework. When adding behavior, make the network lifecycle easy to see and reason about.

The repo follows **build-first learning** (per `docs/learning/README.md`): each new behavior is a 30-90 min slice with one observable OS phenomenon, a smoke test, and a learning note. Chapters of OSEP are read on demand when a slice needs them, not end-to-end.

**Source provenance**: slice docs cite specific OSEP sections. A two-pass doc sweep was done in 2026-09 against the source PDFs (`pages.cs.wisc.edu/~remzi/OSTEP/*.pdf`):

1. **Pass 1 (M9–M12)**: verified Ch. 30 (condition variables), 31 (semaphores + 31.5 reader-writer), 33 (event-based), 39 (files & directories), 40 (vsfs). Several errors found and corrected: §30.4 misattribution for the "while not if" / "two CVs" / "hold lock while signaling" lessons (those are §30.1 / §30.2); the "vsfs = xv6" mistake in M12 (vsfs is OSEP's own design, xv6 is a separate OS); the bogus "log" claim in M12 (vsfs has no log; that's Ch. 42).

2. **Pass 2 (M1–M5)**: verified Ch. 4 (process + 4.4 states), 6 (LDE), 13 (address space), 26 (concurrency), 27 (thread API), 28 (locks), 36 (I/O). One error found: s4.3 cited §26.4 figure 26.7 (the race example) for scheduler non-determinism, but the right citation is §26.2 t0.c + figures 26.3/26.4/26.5 (the non-deterministic ordering example). The M5 race-lab citations for §28.1-2 / §28.7-9 / §28.12-14 are accurate to the chapter's actual section breakdown.

After both passes, every "Key point from OSEP §X.Y" should match the cited chapter's actual table of contents. Future readers should still treat the citations as a pointer to where the lesson lives, not as an authoritative cross-reference, but the section numbers should now be correct.

Useful directions that fit the project:

- Extend concurrency via the next-milestones roadmap in `docs/adr/0004-extend-broad-concurrency-roadmap.md`. M8 (bounded queue + 503), M9 (reader-writer lock + cache), M10 (ThreadPool cap), M11 (raw open/read/close syscall demo), M12 (mini file system slices 12.1–12.7), M13 (MLFQ scheduler), M13.2 (Stride + Lottery), M13.3 (multi-CPU), M14 (linear page table), M16 (TLB), M17 (multi-level page tables), M18 (replacement policies), M19 (COW + swap), M20 (dining philosophers), M21 (FFS), M22 (lock-free CAS), M23.1 (password auth), M23.2 (at-rest encryption), and M15 (ArrayPool in async mode) are done.
- Add `ArrayPool<byte>` to lower per-connection memory in async mode (would change the M7 numbers from 172 MB toward M6's 21 MB).
- Add a `Retry-After` header to the M8 503 response so clients can back off intelligently.
- Extend the **OSEP coverage gaps** in the OSEP Coverage section: paging (Ch. 14-17, §19.4 ASID, §20.4 inverted PTs, §21.4-§21.6 approximated LRU + dirty pages, §23.1 VMS demand-zeroing + RSS + segmented FIFO + second-chance list, §23.2 Linux 2Q + huge pages + 4-level PTs + NX + ASLR + KPTI), full FS (Ch. 36-38 RAID, Ch. 41 §41.7 sub-blocks + parameterized placement, Ch. 43 LFS, Ch. 44 flash, Ch. 45 data integrity), security (Ch. 53.5 system-call access, Ch. 54.5 auth by what you have, Ch. 54.6 biometrics, Ch. 54.7 non-human auth, Ch. 55 ACLs/capabilities/RBAC, Ch. 56.3 PK, Ch. 57 TLS / SSL / certificates / MITM / SSH / HTTPS, key-revocation), concurrency (Ch. 32.2 atomicity/order bugs, Ch. 32.3 deadlock prevention/avoidance). Each new chapter group should get its own ADR before any slices start.
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
