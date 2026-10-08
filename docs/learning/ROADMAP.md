# Roadmap: Cover the rest of OSTEP

Remaining chapters and the slices that will cover them. Ordered roughly by "depends on what's already built" then by OSEP chapter number.

| # | Milestone | OSEP chapter | Depends on | Effort | Status |
|---|---|---|---|---|---|
| 1 | **M16 TLB** | Ch. 19 | M14 (pager) | Small | ✅ |
| 2 | **M17 Multi-level PT** | Ch. 20 | M14, M16 | Small | ✅ |
| 3 | **M18 Replacement policy** | Ch. 21 + Ch. 22 | M14, M16 | Medium | ✅ |
| 4 | **M19 Complete VM** | Ch. 23 | M14, M16, M17, M18 | Medium (capstone) | ✅ |
| 5 | **M13.2 Stride / Lottery** | Ch. 9 | M13 | Small | ✅ |
| 6 | **M13.3 Multi-CPU** | Ch. 10 | M13 | Medium | ✅ |
| 7 | **M20 Dining philosophers** | Ch. 31.6 | M5 (lock) | Small | ✅ |
| 8 | **M22 Lock-free** | Ch. 29 §29.1-§29.2 (CAS) | M5, M6 | Small | ✅ |
| 9 | **M21 FFS** | Ch. 41 | M12 | Medium | ✅ |
| 10 | **M23.1 Password auth** | Ch. 53.4 + 54.4 | none | Medium | ✅ |
| 10b | **M23.2 At-rest encryption** | Ch. 56.2 + 56.7 | none | Medium | ✅ |
| 10c | **M23.3 RBAC** | Ch. 55.6 | M23.1 | Small | ✅ |
| 10d | **M23.4 PK crypto** | Ch. 56.3 | none | Medium | ✅ |
| 10e | **M23.5 TLS handshake** | Ch. 57.5 | M23.2 + M23.4 | Medium | ✅ |
| 10f | **M23.6 TOTP** | Ch. 54.5 | none | Small | ✅ |
| 11 | **M24 RAID** | Ch. 38 | M11 | Small | ✅ |
| 12 | **M25 LFS** | Ch. 43 | M12, M21 | Medium | ✅ |
| 13 | **M26 Flash-based SSDs** | Ch. 44 | M12 | Small | ✅ |
| 14 | **M27 Data integrity** | Ch. 45 | M12 | Small | ✅ |
| 15 | **M28 ASID-tagged TLB** | Ch. 19 §19.5 + §19.7 | M16 | Small | ✅ |
| 16 | **M29 Condition variables** | Ch. 30 §30.1-§30.3 | M6 | Small | ✅ |
| 17 | **M30 Deadlock prevention** | Ch. 32 §32.3 | M20, M29 | Small | ✅ |
| 18 | **M31 LFS extensions** | Ch. 43 §43.3 + §43.12 first half | M25 | Small | ✅ |
| 19 | **M32 Block-level FTL** | Ch. 44 §44.9 | M26 | Small | ✅ |
| 20 | **M33 Scrubbing schedule** | Ch. 45 §45.7 + §45.8 | M27 | Small | ✅ |
| 21 | **M34 Device drivers** | Ch. 36 §36.2-§36.6 | M11 | Small | ✅ |

This covers Ch. 9, 10, 19, 20, 21, 22, 23, 29.1-§29.2 (lock-free CAS), 30 §30.1-§30.3 (CVs), 31.6, 32 §32.3 (deadlock prevention), 36 §36.2-§36.6 (device drivers), 38, 41, 43, 44, 45, 53-57 — the rest of OSEP after M1-M15.

**Done**: M13.2, M13.3, M16, M17, M18, M19 (the VM paging chain), M20 (dining philosophers), M21 (FFS), M22 (lock-free CAS — Ch. 29 §29.1-§29.2), the M23 Part IV suite (.1 password + .2 at-rest + .3 RBAC + .4 PK sign/verify + .5 TLS-style handshake + .6 TOTP), M24 (RAID 0/1/4/5 with XOR recovery), M25 (LFS segments + imap + CR + cleaner), M26 (SSD FTL + GC + wear), M27 (data integrity: XOR/Additive/Fletcher checksums + physical ID + write sequence + scrubber), M28 (ASID-tagged TLB + Global bit + per-ASID flush), M29 (condition variables: own wait queue + lost-wakeup + one-CV bug + two-CV fix + covering conditions), M30 (deadlock prevention + Banker's avoidance, Ch. 32 §32.3), M31 (LFS segment-size cost model + two-CR crash recovery, Ch. 43 §43.3 + §43.12) — all have `overview.md` + slice doc under `docs/learning/`. **Part III Persistence is closed.** The Virtualization paging chain is closed through §19.7; the Concurrency deep-dive is closed through §32.3. Part IV remaining: only X.509 cert chains, biometrics, sudo-equivalent — strictly operational.

**Spec-only**: none. Every milestone in this table is implemented.

**Done (M32-M34)**: M32 (block-level + hybrid FTL, Ch. 44 §44.9), M33 (scrubbing schedule, Ch. 45 §45.7 + §45.8), M34 (device drivers: canonical protocol + interrupts + DMA + PIO/MMIO, Ch. 36 §36.2-§36.6) — each with `overview.md` + slice doc + ADR 0022-0024.

**Next**: none. The table above is fully implemented. M35 landed as ADR 0026, M36 as ADR 0027, M37 as ADR 0028.

What remains is exclusion, not backlog: Ch. 5 (fork/exec/wait have no meaning in .NET), Ch. 14-16 (superseded by paging and the GC), and Ch. 48-50 (a different book-length topic). Chapter-level work is done; what is left is the §-level deferrals each `overview.md` records for itself, and those are choices already made rather than gaps.

| # | Milestone | OSEP chapter | Depends on | Effort | Status |
|---|---|---|---|---|---|
| 22 | **M35 Scheduling baselines** | Ch. 7 | M13 | Small | ✅ (ADR 0026) |
| 23 | **M36 Free-space management** | Ch. 17 | M12, M21 | Small | ✅ (ADR 0027) |
| 24 | **M37 Disk geometry + scheduling** | Ch. 37 | M21, M31 | Medium | ✅ (ADR 0028) |

M35 landed first because §8-§10 all present themselves as improvements over Ch. 7's baselines, and those baselines were absent — three schedulers whose advantages are asserted rather than measured. M36 also retires a false claim: `CONTEXT.md` and ADR 0007 say Ch. 17 is "implicitly covered by the journal (M12)", but M12's bitmap records what is free without choosing a block, which is §17's actual subject.

Excluded (not addressing in this roadmap):
- Ch. 5 Process API (`fork`/`exec`/`wait`) — out of scope for .NET. (Earlier revisions of this file called this "Ch. 7"; in OSTEP v1.10 Process API is Ch. 5 and Ch. 7 is Scheduling: Introduction.)
- Ch. 14 memory API (`malloc`/`free`) — superseded by the GC
- Ch. 15 address translation (base + bounds) — superseded by paging; a simulation would exist only to show what replaced it
- Ch. 16 segmentation — superseded by paging
- Ch. 48-50 (distributed systems / NFS / AFS) — a different book-length topic

Note on Ch. 36/37: buses are Ch. 36 §36.7-§36.9 and remain deferred with the rest of §36.7-§36.10. Ch. 37 is Hard Disk Drives, not buses — it was misrecorded as "Ch. 37 buses" in earlier revisions and is now M37 (ADR 0028).

Each milestone will follow the established pattern: folder under `docs/learning/m{N}-{name}/` with `overview.md` + `s{N}.{S}-*.md` slices + a new ADR if needed.
