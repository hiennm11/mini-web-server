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
| 8 | **M22 Lock-free** | Ch. 32.3 (CAS) | M5, M6 | Small | ✅ |
| 9 | **M21 FFS** | Ch. 41 | M12 | Medium | ✅ |
| 10 | **M23.1 Password auth** | Ch. 53.4 + 54.4 | none | Medium | ✅ |
| 10b | **M23.2 At-rest encryption** | Ch. 56.2 + 56.7 | none | Medium | ✅ |
| 10c | **M23.3 RBAC** | Ch. 55.6 | M23.1 | Small | ✅ |
| 10d | **M23.4 PK crypto** | Ch. 56.3 | none | Medium | ✅ |
| 10e | **M23.5 TLS handshake** | Ch. 57.5 | M23.2 + M23.4 | Medium | ✅ |
| 10f | **M23.6 TOTP** | Ch. 54.5 | none | Small | ✅ |
| 11 | **M24 RAID** | Ch. 38 | M11 | Small | ✅ |
| 12 | **M25 LFS** | Ch. 43 | M12, M21 | Medium | ⬜ |
| 13 | **M26 Flash-based SSDs** | Ch. 44 | M12 | Small | ⬜ |
| 14 | **M27 Data integrity** | Ch. 45 | M12 | Small | ⬜ |

This covers Ch. 9, 10, 19, 20, 21, 22, 23, 31.6, 32.3, 38, 41, 43, 44, 45, 53-57 — the rest of OSEP after M1-M15.

**Done**: M13.2, M13.3, M16, M17, M18, M19 (the VM paging chain), M20 (dining philosophers), M21 (FFS), M22 (lock-free CAS), the M23 Part IV suite (.1 password + .2 at-rest + .3 RBAC + .4 PK sign/verify + .5 TLS-style handshake + .6 TOTP), M24 (RAID 0/1/4/5 with XOR recovery) — all have `overview.md` + slice doc under `docs/learning/`. Part IV remaining: only X.509 cert chains, biometrics, sudo-equivalent — strictly operational.

**Next**: **M25 LFS** (Ch. 43, depends on M12 + M21) — Part III Persistence slice, log-structured writes.

Excluded (not addressing in this roadmap):
- Ch. 7 Process API (`fork`/`exec`/`wait`) — out of scope for .NET
- Ch. 14-17 (base+bound, segmentation, free-space mgmt) — superseded by paging
- Ch. 36-37 device drivers & buses — too low-level for this lab

Each milestone will follow the established pattern: folder under `docs/learning/m{N}-{name}/` with `overview.md` + `s{N}.{S}-*.md` slices + a new ADR if needed.
