namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Swap space — a fixed-size region that holds evicted pages.
///
/// OSEP §21.1 "Swap Space":
///   "We simply reserve some space on disk for swapping. When memory
///    pressure arises, the OS can evict some pages from memory to that
///    swap space, freeing memory for other pages. When a swapped-out
///    page is accessed, the OS will need to bring it back into memory
///    from the swap space."
///
/// OSEP §21.6 "Behind the Scenes: Full VMM" — a swapped page lives in
/// the swap space; the PTE's PFN field points to the swap slot (or holds
/// a sentinel indicating "present in swap slot N"). For our simulator,
/// we encode the swap slot in the PFN by using a high-bit flag.
///
/// In this simulator the swap is just a byte[] parallel to PhysicalMemory
/// (1 MB for frames, 1 MB for swap). A real OS would put swap on disk.
/// </summary>
public sealed class Swap
{
    public const int SWAP_SLOTS = 256;          // matches frame count for simplicity
    public const int SLOT_SIZE = 4096;          // one page per slot

    private readonly byte[] _slots;             // SWAP_SLOTS * SLOT_SIZE bytes
    private readonly bool[] _occupied;          // is slot N in use?

    public int Capacity => SWAP_SLOTS;
    public int UsedSlots { get; private set; }

    public Swap()
    {
        _slots = new byte[SWAP_SLOTS * SLOT_SIZE];
        _occupied = new bool[SWAP_SLOTS];
    }

    /// <summary>
    /// OSEP §21.4 "Page-Fault Control Flow" step 7: write the evicted
    /// frame to swap. Returns the swap slot index where the data was stored.
    /// </summary>
    public int WriteOut(byte[] frame)
    {
        if (frame.Length != SLOT_SIZE)
            throw new ArgumentException($"frame must be {SLOT_SIZE} bytes", nameof(frame));
        // Find a free slot (FIFO swap allocation, simplest).
        int slot = -1;
        for (int i = 0; i < SWAP_SLOTS; i++)
        {
            if (!_occupied[i])
            {
                slot = i;
                break;
            }
        }
        if (slot == -1)
        {
            // All slots full — shouldn't happen if we evict before swap in.
            throw new InvalidOperationException("swap space full; no free slot");
        }
        Array.Copy(frame, 0, _slots, slot * SLOT_SIZE, SLOT_SIZE);
        _occupied[slot] = true;
        UsedSlots++;
        return slot;
    }

    /// <summary>
    /// OSEP §21.4 step 9: read the evicted frame back from swap.
    /// </summary>
    public void ReadIn(int slot, byte[] frame)
    {
        if (slot < 0 || slot >= SWAP_SLOTS)
            throw new ArgumentOutOfRangeException(nameof(slot));
        if (!_occupied[slot])
            throw new InvalidOperationException($"swap slot {slot} not in use");
        Array.Copy(_slots, slot * SLOT_SIZE, frame, 0, SLOT_SIZE);
    }

    /// <summary>Mark a swap slot as free (after the page is read back into memory).</summary>
    public void Free(int slot)
    {
        if (slot < 0 || slot >= SWAP_SLOTS)
            throw new ArgumentOutOfRangeException(nameof(slot));
        _occupied[slot] = false;
        UsedSlots--;
    }

    public bool IsOccupied(int slot) => slot >= 0 && slot < SWAP_SLOTS && _occupied[slot];
}
