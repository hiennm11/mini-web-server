# ADR 0018: M28 ASID-Tagged TLB (OSEP Ch. 19 §19.5 + §19.7)

## Status

Accepted

## Date

2026-09-21

## Context

M16 (TLB) shipped a software-managed TLB that solves OSEP §19.1's basic algorithm but explicitly defers the context-switch problem. OSEP §19.5 calls out two solutions:

1. **Flush the TLB on every context switch** — the M16 approach. Safe but expensive: the next process starts cold and every translation becomes a page-table walk until the working set re-fills the TLB.
2. **ASID-tagged TLB entries** — the hardware-supported solution. Every TLB entry carries an Address Space Identifier; on lookup the hardware compares the caller's ASID against the entry's ASID. A global bit (G) exempts kernel mappings from the ASID check.

OSEP §19.7 makes the idea concrete with the MIPS R4000 TLB entry layout (G bit, 8-bit ASID, VPN, PFN, protection bits).

Without this slice, every context switch in the simulator destroys the TLB and the next run pays the full page-table-walk cost.

## Decision

We extend M16's `Tlb` + `Pager` with three additions:

### 1. `TlbEntry.IsGlobal` and `byte Asid`

The existing `TlbEntry` already has `Asid`; we widen it to `byte` (matching MIPS R4000 / RISC-V SV39) and add an `IsGlobal` field. Per the slice spec, kernel mappings use `IsGlobal=true` with a canonical ASID of 0.

### 2. ASID-aware lookup

`Tlb.Lookup(asid, vpn, out pfn)` now hits when either:
- `entry.Asid == asid`, or
- `entry.IsGlobal == true`.

Without ASID the previous M16 implementation matched every entry that had the same VPN — exactly the cross-process aliasing problem §19.5 warns about.

### 3. `Tlb.Flush(int? asid)` overload

`Flush(null)` retains the M16 behavior (wipe everything) and is documented as the kernel-PTE-edit path. `Flush(asid)` removes only entries with `Asid == asid && !IsGlobal`. Global entries and other ASIDs survive. Returns the count of removed entries for the smoke report.

### 4. `Pager.CurrentAsid` + auto-bump on `CreateProcess`

`Pager` tracks the current address space's ASID in a `byte CurrentAsid` field. Every `CreateProcess` (the simulator's fork boundary) increments it. The slice spec calls for a per-context-switch call to `Tlb.Flush(_currentAsid)`; in this simulator there is no explicit context-switch path, so we surface `CurrentAsid` for the demo route to drive.

### 5. `Tlb.Fill(vpn, pfn, asid, isGlobal)` helper

Thin wrapper over `Insert` that names the slice-spec vocabulary. The smoke route uses it to seed the TLB with predictable entries across ASIDs.

### 6. Demo route `/tlb/run?scenario=flushall-vs-flushasid`

Runs two parallel TLBs through the same context-switch schedule, one using `Flush()` and one using `Flush(asid)`. Reports the surviving-entry count after each switch, plus the breakdown of P1 / P2 / global entries.

## Consequences

### Positive

- **Closes OSEP §19.5 + §19.7** — the canonical ASID + Global-bit semantics are now demonstrated end-to-end.
- **Warm TLB across context switches** — the demo shows that the new policy preserves the incoming process's working set + the kernel's global entries, exactly what the OS pays for adding ASID hardware.
- **Backward compatible** — the existing M16 workload + test surface still works: `Insert(asid, vpn, pfn)` defaults `isGlobal=false`, and `Lookup(asid, vpn, out pfn)` accepts the existing `int` ASID.
- **No new dependencies** — pure struct + counter changes.

### Negative

- **`Pager.EvictFrame` keeps full TLB flush** — eviction is a kernel-PTE-edit path (the simulator invalidates the victim's cached translation wholesale). A finer-grained per-PTE invalidation would model OSEP §19.5 more precisely but is not required for the slice's acceptance criteria.
- **No ASID rollover flush** — when the 8-bit ASID wraps around (after 256 forks), the simulator does not flush. Real hardware flushes on rollover to avoid aliasing; deferred (matches M28 deviations).
- **PCID (x86) not modeled** — out of scope.
- **Per-victim invalidation not implemented** — eviction still flushes the whole TLB to keep the swap-out path simple.

## Verification

- Build clean (`dotnet build`).
- Existing M16 tests still pass.
- New tests cover: ASID isolation across two processes, Global-bit survives per-ASID flush but not full flush, mismatched ASID misses, `Pager.CurrentAsid` bumps on `CreateProcess`.
- Smoke: `/tlb/run?scenario=flushall-vs-flushasid&context_switches=10` shows the survival-count comparison.

## Source Documents

- `docs/learning/m28-asid/overview.md` — milestone scope + OSEP citations.
- `docs/learning/m28-asid/s1-asid-tagged-tlb.md` — slice doc + acceptance criteria.
- `docs/learning/m16-tlb/overview.md` — predecessor M16 surface (defers this slice explicitly).
- OSEP Ch. 19 §19.5 "Issue: Context Switches".
- OSEP Ch. 19 §19.7 "A Real TLB Entry" (MIPS R4000).
- ADR 0007 (M14/M16/M17/M18 paging chain) for context.
