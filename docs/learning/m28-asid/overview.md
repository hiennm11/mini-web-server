# Milestone 28: ASID-Tagged TLB
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
Why does context switching on a real CPU force a full TLB flush, and how does ASID eliminate that cost? What's the cost of the address-space identifier bits in a TLB entry, and how do MIPS / x86 / ARM split the ASID space?
## Scope
A small extension to the M16 TLB simulator (`src/MiniWebServer.Host/MiniPager/Tlb.cs`) that adds an **ASID field** to every TLB entry and a **Global** flag for kernel entries. The slice:
- Tags each TLB entry with an 8-bit ASID (matching MIPS R4000 [SH94]); the TLB lookup becomes `(vpn, asid)` instead of `vpn` alone.
- Adds a `TLB_ENTRY_GLOBAL` bit; entries with this bit set match any ASID — the kernel's mappings live in every address space.
- Replaces `FlushTlb()` (which currently blows away every entry on context switch) with `FlushTlb(asid)` that only flushes entries belonging to one address space; `FlushTlb()` keeps the existing "blow everything" behaviour for kernel changes.
- Demonstrates the cost difference: `/tlb/run?scenario=flushall-vs-flushasid&context_switches=N` reports how many TLB entries survived each context switch under each scheme.
## Slice
- **[s1-asid-tagged-tlb.md](./s1-asid-tagged-tlb.md)** - ASID-tagged TLB entries + global flag + per-ASID flush; observable survival of TLB entries across context switches.
## OSTEP coverage
- **Ch. 19 §19.4 Address Space Identifiers (ASIDs)** ([SH94]): the canonical mitigation for TLB flush cost on context switch. ASID field, global bit, per-ASID flush, and the classic hardware table-walk + software-loaded TLB distinction (we already use the latter).
- **Ch. 19 §19.5 (deferred)**: multilevel TLBs (L1 + L2 with different associativities) and hardware-managed TLBs.
- **Cross-reference**: M16 `docs/learning/m16-tlb/overview.md` established the TLB primitive; this slice is a backward-compatible extension of the same surface.
## Files
- `src/MiniWebServer.Host/MiniPager/Tlb.cs` — add `Asid` field to `TlbEntry`, `IsGlobal` flag, change `Lookup(Vpn, Asid)`, add `Flush(Asid?)`.
- `src/MiniWebServer.Host/MiniPager/Pager.cs` — pass the current `Asid` into `Tlb.Lookup` and `Tlb.Fill` on every translation; replace context-switch `FlushTlb()` with `FlushTlb(asid)`.
- `src/MiniWebServer.Host/Program.cs` — `/tlb/run` route handles the new `flushall-vs-flushasid` scenario.
## Implementation deviations from OSEP
- **ASID width = 8 bits**, matching MIPS R4000 / RISC-V `MODE_SV39`. Real x86-64 uses 12 bits. Pick 8 because the simulator already uses a 16-bit VPN; widening the ASID would push the entry beyond 64 bytes.
- **ASID rollover = no-op**: when the 8-bit ASID wraps around, the simulator does not flush. Real hardware flushes on rollover to avoid aliasing — a future slice could add `AsidVersion` (the §19.4 extension).
- **No PCID on the guest side**: x86's PCID (Process-Context ID) is a later refinement with the same intent; not modeled.
## What this slice does NOT do
- **Multilevel TLB** (OSEP §19.5) — L1 + L2 split with different associativities. The single-level TLB is enough to demonstrate the ASID concept.
- **Hardware-managed TLB** — the M16 design is software-loaded (caller writes entries on page fault); we keep that.
- **ASID rollover flush** — see deviations.
- **PCID virtualization extensions** (x86 INVPCID) — out of scope.
## Where this leads
- M17 multi-level page tables + this ASID tagging = a more realistic VM: TLB entries can survive a context switch between processes, restoring "warm TLB" performance on the next run.
- §19.5 multilevel TLB is the next natural extension if the lab adds TLB-miss latency benchmarks.
