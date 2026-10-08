# Slice 28.1: ASID-Tagged TLB
> **What it does** — extends M16's TLB with an ASID field + a Global bit; replaces the context-switch flush with a per-ASID flush; demonstrates the TLB-warmth survival across context switches via a `/tlb/run?scenario=flushall-vs-flushasid` route.
## Surface change (diff against M16)
- `TlbEntry` gains two fields:
  - `byte Asid` — the address-space identifier that owns this entry. The kernel conventionally uses `0`.
  - `bool IsGlobal` — when `true`, the entry matches any ASID. The kernel's own mappings use this.
- `Tlb.Lookup(int asid, int vpn, out int pfn)` returns a hit when either `entry.Asid == asid` or `entry.IsGlobal`. No hit on a non-global entry that belongs to a different ASID.
- `Tlb.Fill(int vpn, int pfn, int asid, bool isGlobal)` — records the caller's ASID; the global bit is a parameter.
- `Tlb.Flush(int? asid)` — when `asid is null`, nukes the entire TLB (M16 behaviour, retained for kernel page-table edits). When `asid is not null`, removes only the entries with `Asid == asid && !IsGlobal`; global entries and other ASIDs survive. Returns the number of entries removed.
- `Tlb.CountOwnedBy(int asid)` and `Tlb.GlobalCount` — report per-ASID and global populations so the demo can show survival per owner rather than only a total.
- `Pager.CurrentAsid` (a `byte` property) increments on every `CreateProcess(pid)`. The pager's own page-table edits still call the parameterless `Flush()`; the per-ASID policy is driven by the demo in `Workloads.cs`, which runs both policies over one shared starting snapshot.

## Files
- `src/MiniWebServer.Host/MiniPager/Tlb.cs` — structural change.
- `src/MiniWebServer.Host/MiniPager/Pager.cs` — `CurrentAsid` property + the `CreateProcess` bump.
- `src/MiniWebServer.Host/MiniPager/Workloads.cs` — `ContextSwitchFlushDemo`: warms both processes' TLB entries plus two kernel globals, snapshots, then replays the same switch sequence under `Flush()` and `Flush(prev)`.
- `src/MiniWebServer.Host/Program.cs` — `/tlb/run` route handles `flushall-vs-flushasid` scenario.

## Smoke evidence
- `/tlb/run?scenario=flushall-vs-flushasid&context_switches=10` returns a body showing, for each context switch:
  - surviving entries under the old `FlushTlb()` (always 0).
  - surviving entries under the new `FlushTlb(_currentAsid)` (typically the global entries + entries from the *next* ASID's working set if any).
- `curl /tlb/run?...` shows in the JSON-ish output that global entries survive all flushes; per-ASID entries do not survive their owner's flush but are not disturbed by other ASIDs' flushes.
- Existing M16 tests still pass (`Tlb.cs` lookup call sites updated to pass `0` as the default ASID).

## Tests
`tests/MiniWebServer.Host.Tests/Program.cs` adds 4 tests:
- `tlb asid isolates address spaces (slice 28.1)` — fill entries for ASID 1 and 2; flush ASID 1; assert ASID 2's entries survive; flush ASID 2; assert none survive.
- `tlb global entries survive per-ASID flush but not full flush (slice 28.1)` — fill a global entry; flush ASID 1; assert it survives; flush with `null`; assert it is gone.
- `tlb lookup with mismatched asid misses (slice 28.1)` — a non-global entry filled for one ASID must not answer another ASID's lookup, which is the property the flush scoping relies on.

## Source documents
- `docs/learning/m16-tlb/overview.md` — predecessor M16 surface.
- `docs/learning/m28-asid/overview.md` — milestone scope.
- OSEP Ch. 19 §19.5 — context switch + ASID motivation.
- OSEP Ch. 19 §19.7 — MIPS R4000 TLB entry with ASID + Global bit.
