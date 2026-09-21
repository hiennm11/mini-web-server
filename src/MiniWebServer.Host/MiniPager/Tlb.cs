namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// One entry in the TLB. Maps a VPN to a PFN with metadata.
///
/// OSEP §19.4 "TLB Contents: What's In There?":
///   VPN | PFN | other bits
///   - valid bit: whether the entry has a valid translation.
///   - protection bits: read/write/execute.
///   - address-space identifier (ASID): to disambiguate between processes.
///   - dirty bit: marked when the page has been written to.
///
/// OSEP §19.5 "Issue: Context Switches":
///   ASID is the field that disambiguates which process owns an entry.
///   Without ASID (or a flush on context switch), VPN=10 could match
///   either Process A's mapping or Process B's mapping.
///
/// OSEP §19.7 "A Real TLB Entry" (MIPS R4000 example):
///   Real TLBs have many more bits (G, ASID, C, D, V, page mask).
///   Our TLB is a simplified subset.
/// </summary>
public struct TlbEntry
{
    public int Vpn;
    public int Pfn;
    public bool Valid;
    // OSEP §19.5: which address space owns this entry. MIPS R4000 uses
    // 8 bits (matches RISC-V SV39). Stored as byte to reflect the
    // hardware width; call sites that have a wider PID cast at the
    // boundary (the simulator's PID never exceeds 255 in practice).
    public byte Asid;
    // OSEP §19.7 "A Real TLB Entry" (MIPS R4000): when set, the entry
    // matches any ASID on lookup. Used for kernel mappings that live
    // in every address space.
    public bool IsGlobal;
    // OSEP §19.4: protection bits (read/write/exec).
    public byte Prot;

    public static readonly TlbEntry Empty = new() { Valid = false, Pfn = -1 };
}

/// <summary>
/// A Translation Lookaside Buffer (TLB).
///
/// OSEP §19.1 "TLB Basic Algorithm" (Figure 19.1):
///   1. Extract VPN from virtual address.
///   2. TLB_Lookup(VPN): if hit, compute PA from PFN + offset, done.
///   3. On miss, consult the page table to find the PTE.
///   4. If PTE.Valid is false -> SEGFAULT.
///   5. TLB_Insert(VPN, PFN, protection).
///   6. Retry the instruction.
///
/// OSEP §19.3 "Who Handles The TLB Miss?":
///   - Hardware-managed TLB (CISC like x86): hardware walks the page table.
///   - Software-managed TLB (RISC like MIPS): hardware raises exception,
///     OS trap handler walks the page table, uses privileged instructions
///     to update the TLB, returns from trap.
///
///   Our TLB is **software-managed**: we explicitly walk the page table
///   on a TLB miss and call TLB_Insert ourselves. Closer to MIPS R4000
///   than to x86.
///
/// OSEP §19.6 "Issue: Replacement Policy":
///   When the TLB is full, we need to evict an entry. Choices:
///   - LRU: evict least-recently-used.
///   - Random: evict a random entry (MIPS uses this).
///   Our default is Random (simpler).
/// </summary>
public sealed class Tlb
{
    private readonly TlbEntry[] _entries;
    private readonly Random _rng;
    public int Capacity { get; }
    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Evictions { get; private set; }
    public IReadOnlyList<TlbEntry> Entries => _entries;

    /// <summary>Last access tick (for LRU). Not used by Random policy.</summary>
    private readonly long[] _lastUsed;

    public Tlb(int capacity, int seed = 42)
    {
        if (capacity < 1) throw new ArgumentException("capacity >= 1", nameof(capacity));
        Capacity = capacity;
        _entries = new TlbEntry[capacity];
        _lastUsed = new long[capacity];
        _rng = new Random(seed);
        for (int i = 0; i < capacity; i++)
        {
            _entries[i] = TlbEntry.Empty;
            _lastUsed[i] = 0;
        }
    }

    /// <summary>
    /// OSEP §19.1: TLB lookup. Returns (hit, pfn).
    /// On hit, increments Hits + updates lastUsed.
    /// On miss, increments Misses (the caller should then walk the page
    /// table and call Insert).
    ///
    /// Slice 28.1 (ASID-tagged TLB): a non-global entry matches only
    /// when its <see cref="TlbEntry.Asid"/> equals the caller's ASID.
    /// A global entry (<see cref="TlbEntry.IsGlobal"/> == true) matches
    /// regardless of ASID — used for kernel mappings that live in
    /// every address space. OSEP §19.7 (MIPS R4000).
    /// </summary>
    public bool Lookup(int asid, int vpn, out int pfn)
    {
        byte wantAsid = (byte)asid;
        for (int i = 0; i < _entries.Length; i++)
        {
            if (!_entries[i].Valid || _entries[i].Vpn != vpn) continue;
            // OSEP §19.7: G bit set → match any ASID. Otherwise the
            // entry's ASID must match the caller's.
            if (_entries[i].IsGlobal || _entries[i].Asid == wantAsid)
            {
                pfn = _entries[i].Pfn;
                _lastUsed[i] = Misses + Hits;
                Hits++;
                return true;
            }
        }
        pfn = -1;
        Misses++;
        return false;
    }

    /// <summary>
    /// OSEP §19.1 step 5: TLB_Insert(VPN, PTE.PFN, PTE.ProtectBits).
    /// If the TLB is full, evict an entry first (Random policy).
    ///
    /// Slice 28.1: <paramref name="isGlobal"/> defaults to false
    /// (a per-process mapping). Kernel code passes true for mappings
    /// that should be visible to every ASID (OSEP §19.7 G bit).
    /// </summary>
    public void Insert(int asid, int vpn, int pfn, byte prot = 0, bool isGlobal = false)
    {
        // Look for an invalid slot first (OSEP §19.5: "we usually find
        // an invalid slot in the TLB upon a miss; only after the OS
        // has been running for a while will the TLB become full").
        int slot = -1;
        for (int i = 0; i < _entries.Length; i++)
        {
            if (!_entries[i].Valid)
            {
                slot = i;
                break;
            }
        }

        // If full, evict by random replacement (OSEP §19.6 MIPS choice).
        if (slot == -1)
        {
            slot = _rng.Next(_entries.Length);
            Evictions++;
        }

        _entries[slot] = new TlbEntry
        {
            Vpn = vpn,
            Pfn = pfn,
            Valid = true,
            Asid = (byte)asid,
            IsGlobal = isGlobal,
            Prot = prot,
        };
        _lastUsed[slot] = Misses + Hits;
    }

    /// <summary>
    /// Slice 28.1 explicit "fill a specific slot" used by the demo
    /// to seed the TLB with predictable entries across ASIDs. Thin
    /// wrapper around <see cref="Insert"/>; named to match the
    /// slice doc's vocabulary.
    /// </summary>
    public void Fill(int vpn, int pfn, int asid, bool isGlobal)
        => Insert(asid, vpn, pfn, prot: 0, isGlobal: isGlobal);

    /// <summary>
    /// OSEP §19.5 "Issue: Context Switches":
    ///   "One approach is to simply flush the TLB on context switches,
    ///    thus emptying it before running the next process."
    /// Flush sets all entries to invalid.
    /// </summary>
    public void Flush()
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            _entries[i] = TlbEntry.Empty;
            _lastUsed[i] = 0;
        }
    }

    /// <summary>
    /// Slice 28.1: per-ASID flush (OSEP §19.5 + §19.7).
    ///
    /// <list type="bullet">
    /// <item><c>asid == null</c> — full TLB flush (all entries, global
    /// or not). Used when the kernel edits page-table entries and
    /// cannot trust any cached translation.</item>
    /// <item><c>asid != null</c> — flush only entries that belong to
    /// that ASID and are NOT global. Global entries (kernel mappings)
    /// and entries owned by other ASIDs survive. This is the
    /// hardware-supported alternative to blowing the whole TLB on
    /// every context switch.</item>
    /// </list>
    /// </summary>
    /// <returns>The number of entries removed.</returns>
    public int Flush(int? asid)
    {
        if (asid is null)
        {
            int n = 0;
            for (int i = 0; i < _entries.Length; i++) if (_entries[i].Valid) n++;
            Flush();
            return n;
        }
        byte target = (byte)asid.Value;
        int removed = 0;
        for (int i = 0; i < _entries.Length; i++)
        {
            var e = _entries[i];
            if (e.Valid && !e.IsGlobal && e.Asid == target)
            {
                _entries[i] = TlbEntry.Empty;
                _lastUsed[i] = 0;
                removed++;
            }
        }
        return removed;
    }

    /// <summary>Count of currently-valid entries (used by smoke reports).</summary>
    public int ValidCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _entries.Length; i++) if (_entries[i].Valid) n++;
            return n;
        }
    }

    /// <summary>Count of currently-valid entries owned by <paramref name="asid"/>.</summary>
    public int CountOwnedBy(int asid)
    {
        byte target = (byte)asid;
        int n = 0;
        for (int i = 0; i < _entries.Length; i++)
        {
            var e = _entries[i];
            if (e.Valid && !e.IsGlobal && e.Asid == target) n++;
        }
        return n;
    }

    /// <summary>Count of currently-valid global entries.</summary>
    public int GlobalCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _entries.Length; i++) if (_entries[i].Valid && _entries[i].IsGlobal) n++;
            return n;
        }
    }

    /// <summary>Reset hit/miss/eviction counters (useful when comparing runs).</summary>
    public void ResetStats()
    {
        Hits = 0;
        Misses = 0;
        Evictions = 0;
    }

    /// <summary>Hit rate as a fraction [0, 1]. Returns 0 if no accesses yet.</summary>
    public double HitRate => (Hits + Misses) == 0 ? 0.0 : (double)Hits / (Hits + Misses);

    public TlbStats Stats() => new(Hits, Misses, Evictions, HitRate);
}

public sealed record TlbStats(int Hits, int Misses, int Evictions, double HitRate);
