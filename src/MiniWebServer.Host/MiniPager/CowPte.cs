namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Slice 19.1: Page table entry with copy-on-write support.
///
/// OSEP §23.1 VMS "Other Neat Tricks":
///   "Another cool optimization found in VMS (and again, in virtually
///    every modern OS) is **copy-on-write** (COW for short). The idea,
///    which goes at least back to the TENEX operating system [BB+72], is
///    simple: when the OS needs to copy a page from one address space
///    to another, instead of copying it, it can map it into the target
///    address space and mark it read-only in both address spaces. If both
///    address spaces only read the page, no further action is taken, and
///    thus the OS has realized a fast copy without actually moving any
///    data. If, however, one of the address spaces does indeed try to
///    write to the page, it will trap into the OS. The OS will then
///    notice that the page is a COW page, and thus (lazily) allocate a
///    new page, fill it with the data, and map this new page into the
///    address space of the faulting process."
///
/// OSEP §23.2 Linux VM:
///   "Linux performs lazy copy-on-write copying of pages upon fork(),
///    thus lowering overheads by avoiding unnecessary copying."
///
/// OSEP §23.2 "Page Cache" + 2Q:
///   "Even better: retaining portions of this file in memory isn't
///    useful, as they are never re-referenced before getting kicked
///    out of memory. The Linux version of the 2Q replacement algorithm
///    solves this problem by keeping two lists, and dividing memory
///    between them."
///
/// Our SwappablePte gains a SharedRefCount and OriginalFrameNo for COW.
/// The Pager tracks per-page ref-counts so COW can detect the last
/// reference and free the frame when no one else shares it.
/// </summary>
public struct CowPte
{
    public bool Valid;             // true if in physical memory
    public bool InSwap;            // true if evicted to swap (M18)
    public int FrameNo;            // valid only when Valid
    public int SwapSlot;           // valid only when InSwap
    public bool ReadOnly;          // COW: trigger copy-on-write on first write
    public bool Dirty;             // written since last flush
    public bool Referenced;        // accessed recently (LRU)

    public static readonly CowPte Empty = new() { Valid = false, InSwap = false, FrameNo = -1, SwapSlot = -1 };

    /// <summary>True if this PTE is shared with at least one other PTE (COW).</summary>
    public bool IsShared => ReadOnly && Valid;
}
