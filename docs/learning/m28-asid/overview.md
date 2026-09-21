# Milestone 28: ASID-Tagged TLB (Ch. 19 §19.5 + §19.7)
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
What does the OS do about stale TLB entries when switching from one process to another? What does an Address Space Identifier (ASID) buy us, and what does the Global bit do?
## Scope
A backward-compatible extension to the M16 TLB (`src/MiniWebServer.Host/MiniPager/Tlb.cs`) that adds:
- **ASID field** (8 bits, matching MIPS R4000 [H93]) to every TLB entry — identifies which address space the entry belongs to.
- **Global (G) bit** — when set, the entry matches any ASID; used for kernel mappings that live in every address space.
- **Per-ASID flush** — replaces M16's context-switch `FlushTlb()` (blow everything) with `FlushTlb(asid)` that only nukes entries belonging to one address space. Global entries and other ASIDs' entries survive.

The driver route `/tlb/run?scenario=flushall-vs-flushasid&context_switches=N` reports how many TLB entries survive each context switch under the old scheme vs. the new scheme.

## Slice
- **[s1-asid-tagged-tlb.md](./s1-asid-tagged-tlb.md)** - ASID field + Global bit + per-ASID flush; observable survival of TLB entries across context switches.

## OSTEP coverage
- **Ch. 19 §19.5** "TLB Issue: Context Switches" [OS+AD14]: the canonical problem statement. The naive solution is to flush the entire TLB on every context switch (which the M16 simulator does). The text introduces ASID as the hardware-supported solution: an extra field per TLB entry, plus a per-CPU register that holds the current ASID, lets the hardware distinguish entries from different processes sharing the TLB.
- **Ch. 19 §19.7** "A Real TLB Entry": the MIPS R4000 example, which makes the abstract idea concrete. The MIPS TLB entry has a `G` (Global) bit, an 8-bit ASID, plus a 19-bit VPN + 24-bit PFN + 3 coherence bits + dirty + valid. When `G` is set, the ASID is ignored on lookup.
- **Cross-reference**: M16 `docs/learning/m16-tlb/overview.md` established the TLB primitive; this slice is a backward-compatible extension of the same surface.

## Files
- `src/MiniWebServer.Host/MiniPager/Tlb.cs` — add `byte Asid`, `bool IsGlobal` to `TlbEntry`; change `Lookup(Vpn, Asid)`; add `Flush(int? asid)` overload.
- `src/MiniWebServer.Host/MiniPager/Pager.cs` — track a `byte CurrentAsid` field; bump on every `Fork()`; call `Tlb.Flush(_currentAsid)` instead of `Tlb.Flush()` on context switch.
- `src/MiniWebServer.Host/Program.cs` — `/tlb/run` route handles `flushall-vs-flushasid` scenario.

## Implementation deviations from OSEP
- **ASID width = 8 bits**, matching MIPS R4000 / RISC-V SV39. The simulator already uses a 16-bit VPN; widening the ASID to 12 bits (x86 PCID) would push the entry beyond 64 bytes.
- **ASID rollover is a no-op**: when the 8-bit ASID wraps around (after 256 forks), the simulator does not flush. Real hardware flushes on rollover to avoid aliasing. A future slice could add an `AsidVersion` register (the [SH94] extension).
- **PCID (x86)** is a later refinement of the same idea; not modeled.
- **No ASID-tagged software TLB miss handler**: the M16 TLB is software-loaded; this slice keeps that style. The handler reads the current ASID from a register (just a field on the Pager, in our case) and writes it into the new TLB entry.

## What this slice does NOT do
- **Multilevel TLB** (OSEP §19.5 mentions L1 + L2 split) — deferred; the single-level TLB is enough to demonstrate ASID.
- **ASID rollover flush** — see deviations.
- **x86 INVPCID** instruction (per-PCID invalidation) — out of scope.
- **ASID-tracked kernel mappings**: the slice uses the Global bit for kernel entries. Real systems often reserve a small range of ASIDs (e.g., 0–15) for kernel use and let user processes take the rest; the slice doesn't model this distinction.

## Where this leads
- M17 multi-level page tables + M28 ASID = a more realistic VM: TLB entries can survive a context switch between processes, restoring "warm TLB" performance on the next run.
- §19.5 mentions multilevel TLBs and ASID-trapped flush as the next natural extensions.
