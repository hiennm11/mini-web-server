# Slice 28.1: ASID-Tagged TLB
> **What it does** — extends M16's TLB with an ASID field + a global bit; replaces the context-switch flush with a per-ASID flush; demonstrates the TLB-warmth survival across context switches via a `/tlb/run?scenario=flushall-vs-flushasid` route.
## Surface change (diff against M16)
- `TlbEntry` gains two fields:
  - `byte Asid` — the address-space identifier that owns this entry. `0` is reserved for "no owner / kernel".
  - `bool IsGlobal` — when `true`, the entry matches any ASID. The kernel's own mappings use this.
- `Tlb.Lookup(Vpn v, byte asid)` returns a hit when either `entry.Asid == asid` or `entry.IsGlobal`. No hit on a non-global entry that belongs to a different ASID.
- `Tlb.Fill(Vpn v, Pfn p, ...)` records the caller's ASID; the global bit is a parameter.
- `Tlb.Flush(int? asid)` — when `asid is null`, nukes the entire TLB (M16 behaviour, retained for kernel page-table edits). When `asid is not null`, removes only the entries with `Asid == asid && !IsGlobal`; global entries and other ASIDs survive.
- `Pager.cs` tracks a `byte CurrentAsid` field that increments on every `Fork()`. On context switch, the pager calls `Tlb.Flush(_currentAsid)` instead of `Tlb.Flush()`.
## Files
- `src/MiniWebServer.Host/MiniPager/Tlb.cs` — structural change.
- `src/MiniWebServer.Host/MiniPager/Pager.cs` — `CurrentAsid` field + context-switch update.
- `src/MiniWebServer.Host/Program.cs` — `/tlb/run` route handles `flushall-vs-flushasid` scenario.
## Smoke evidence
- `/tlb/run?scenario=flushall-vs-flushasid&context_switches=10` returns a body showing, for each context switch: (a) surviving entries under the old `FlushTlb()` (always 0) and (b) surviving entries under the new `FlushTlb(_currentAsid)` (typically the global entries + entries from the *next* ASID's working set if any).
- `curl /tlb/run?...` shows in the JSON-ish output that global entries survive all flushes; per-ASID entries do not survive their owner's flush but are not disturbed by other ASIDs' flushes.
- Existing M16 tests still pass (`Tlb.cs` lookup call sites updated to pass `0` as the default ASID).
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds two `Run(...)` cases:
  - `Run("tlb asid isolates address spaces", ...)` — fill entries for ASID 1 and 2; flush ASID 1; assert ASID 2's entries survive; flush ASID 2; assert none survive.
  - `Run("tlb global entries survive all flushes", ...)` — fill a global entry; flush ASID 1; assert global entry survives; flush with `null` (blow everything); assert global entry is gone.
## Source documents
- `docs/learning/m16-tlb/overview.md` — predecessor M16 surface.
- `docs/learning/m28-asid/overview.md` — milestone scope.
- OSTEP Ch. 19 §19.4 — ASID rationale.
