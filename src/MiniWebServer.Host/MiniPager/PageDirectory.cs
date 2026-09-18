namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Page Directory Entry.
///
/// OSEP §20.1 "A Two-Level Page Table":
///   "the page directory, which has an entry for each page in the top
///    level of the page table. Each PDE contains a valid bit and a page
///    frame number, just like a PTE, but the page frame number here is
///    to a page of the page table, not to a page of user data."
///
/// In our 32-bit VA + 4 KB page setup:
///   - 10-bit PGD index → 1024 PDEs (one page of PDEs)
///   - 10-bit PT index  → 1024 PTEs per page table
///   - 12-bit offset
///
/// OSEP §20.4 "A Memory Trace" — the two-level structure reduces memory
/// usage from 4 MB (linear) to just the PGD page + the PT pages that
/// are actually populated. With 4 mapped pages that share a PGD entry,
/// we use 1 PGD slot + 1 PT page = 4 KB + 4 KB = 8 KB (vs 4 MB linear).
/// </summary>
public struct PageDirectoryEntry
{
    public bool Valid;
    public int PageTableFrame;  // physical frame holding a PageTable block

    public static readonly PageDirectoryEntry Empty = new() { Valid = false, PageTableFrame = -1 };
}

/// <summary>
/// Two-level page table.
///
/// OSEP §20.1 — Figure 20.3 "A Two-Level Page Table":
///   "the top-level directory page, the page-of-pages array, is used
///    to determine where (in physical memory) to find the page-of-pages
///    entries for a particular portion of the address space. The
///    page-of-pages entry then points to the actual page table entries
///    that contain the physical frame numbers for each piece of user data."
///
/// Our Pager integrates with both linear (slice 14.1) and two-level
/// (slice 17.1) page tables through the IPageTableLookup interface.
/// </summary>
public sealed class PageDirectory
{
    private readonly PageDirectoryEntry[] _entries;

    /// <summary>Number of PGD entries. 1024 for 32-bit VA + 4 KB pages.</summary>
    public int Length => _entries.Length;

    public PageDirectory(int numEntries)
    {
        _entries = new PageDirectoryEntry[numEntries];
        for (int i = 0; i < numEntries; i++)
            _entries[i] = PageDirectoryEntry.Empty;
    }

    public PageDirectoryEntry Get(int index) => _entries[index];

    public void Set(int index, PageDirectoryEntry pde) => _entries[index] = pde;

    /// <summary>
    /// OSEP §20.1 — count of populated PDEs (each has a backing PT page).
    /// Memory savings: with sparse address spaces, most PDEs are invalid
    /// and consume only the PGD slot, not a full PT page.
    /// </summary>
    public int PopulatedCount()
    {
        int n = 0;
        for (int i = 0; i < _entries.Length; i++) if (_entries[i].Valid) n++;
        return n;
    }
}

/// <summary>
/// Page table entries used in the inner PT page. Same as PTE for slice 14.1
/// but separated out so the two-level structure has its own type.
/// </summary>
public struct PageTableEntry
{
    public bool Valid;
    public int FrameNo;
    public bool Dirty;
    public bool Referenced;

    public static readonly PageTableEntry Empty = new() { Valid = false, FrameNo = -1 };
}

/// <summary>
/// Inner page table. 1024 PTEs per PT page for 32-bit VA + 4 KB pages.
///
/// OSEP §20.1 "the page-of-pages entries then point to the actual page
///  table entries that contain the physical frame numbers for each
///  piece of user data".
/// </summary>
public sealed class InnerPageTable
{
    private readonly PageTableEntry[] _entries;

    public int Length => _entries.Length;
    public int MappedCount()
    {
        int n = 0;
        for (int i = 0; i < _entries.Length; i++) if (_entries[i].Valid) n++;
        return n;
    }

    public InnerPageTable(int numEntries)
    {
        _entries = new PageTableEntry[numEntries];
        for (int i = 0; i < numEntries; i++)
            _entries[i] = PageTableEntry.Empty;
    }

    public PageTableEntry Get(int index) => _entries[index];
    public void Set(int index, PageTableEntry pte) => _entries[index] = pte;
}
