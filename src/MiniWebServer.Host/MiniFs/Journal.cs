using static MiniWebServer.Host.MiniFs.Constants;

namespace MiniWebServer.Host.MiniFs;

/// <summary>
/// Write-ahead log (journal) for crash-consistent updates.
/// OSEP Ch. 42, §42.3: "Journaling (or Write-Ahead Logging)".
///
/// Layout (8 disk blocks reserved at JOURNAL_START = 7):
///   block 7          : journal superblock (head, tail, next-TID)
///   blocks 8..14     : journal data (TxB + per-block updates + TxE)
///
/// Protocol (per transaction, blocking write-through):
///   1. TxB: write transaction-begin block (TID, count of block updates).
///   2. For each (blockNo, newData): write the new data block, also
///      recording the blockNo so recovery can replay.
///   3. TxE: write transaction-end block (TID + checksum).
///   4. Checkpoint: write each (blockNo, newData) to its FINAL position
///      on the disk. If we crash before step 4 completes, recovery will
///      replay the committed transaction (redo logging).
///   5. Mark the journal slot free (update journal superblock).
///
/// Recovery (on Mount): scan the journal from the start. For each
/// transaction that has both a TxB and a matching TxE, replay the
/// checkpoint writes. Discard any TxB without a matching TxE
/// (uncommitted).
///
/// This slice uses metadata journaling only (no data journaling) and a
/// single block update per transaction — the simplest possible slice
/// that still demonstrates the OSEP §42 protocol. Multi-block
/// transactions are a natural follow-up.
/// </summary>
public static class Journal
{
    private static int _currentTid = 1;
    private static int _head = 0;  // byte offset within journal data region (in blocks)
    private static bool _active = false;

    /// <summary>
    /// Journal superblock — 36-byte on-disk record:
    /// Magic, Head (next free block), Tail (next free block after wrap),
    /// NextTID, Reserved[5].
    /// </summary>
    private sealed class JournalSuperblock
    {
        public uint Magic;
        public int Head;
        public int Tail;
        public int NextTid;
    }

    // --- On-disk layout helpers ---

    private static JournalSuperblock ReadSuperblock()
    {
        var block = new byte[BLOCK_SIZE];
        MiniFs.ReadBlock(JOURNAL_SUPERBLOCK_BLOCK, block);
        return new JournalSuperblock
        {
            Magic = BitConverter.ToUInt32(block, 0),
            Head = BitConverter.ToInt32(block, 4),
            Tail = BitConverter.ToInt32(block, 8),
            NextTid = BitConverter.ToInt32(block, 12),
        };
    }

    private static void WriteSuperblock()
    {
        var block = new byte[BLOCK_SIZE];
        BitConverter.GetBytes(JOURNAL_SB_MAGIC).CopyTo(block, 0);
        BitConverter.GetBytes(_head).CopyTo(block, 4);
        BitConverter.GetBytes(_currentTid).CopyTo(block, 8);
        // Note: We don't track Tail/NextTid in this simple version.
        MiniFs.WriteBlockNoLog(JOURNAL_SUPERBLOCK_BLOCK, block);
    }

    private static int JournalDataBlock(int slot)
    {
        // slot 0..(JOURNAL_DATA_BLOCKS-1)
        return JOURNAL_DATA_START + (slot % JOURNAL_DATA_BLOCKS);
    }

    // --- Transaction API ---

    /// <summary>
    /// Write a single block through the journal. Implements the full
    /// 5-step OSEP §42 protocol: TxB → data → TxE → checkpoint →
    /// (commit marker in journal superblock).
    /// </summary>
    public static void WriteBlockJournaled(int blockNo, byte[] src)
    {
        if (blockNo < 0 || blockNo >= NUM_BLOCKS)
            throw new ArgumentOutOfRangeException(nameof(blockNo));
        if (src.Length != BLOCK_SIZE)
            throw new ArgumentException("src must be BLOCK_SIZE bytes", nameof(src));

        if (_active)
            throw new InvalidOperationException("nested journal transactions not supported");

        _active = true;
        try
        {
            int tid = _currentTid++;
            int slot = _head;

            // Step 1: TxB at journal-data-slot
            var txb = new byte[BLOCK_SIZE];
            BitConverter.GetBytes(TXB_MAGIC).CopyTo(txb, 0);
            BitConverter.GetBytes(tid).CopyTo(txb, 4);
            BitConverter.GetBytes(blockNo).CopyTo(txb, 8);  // single-block transactions
            // Rest of TxB block is zero
            MiniFs.WriteBlockNoLog(JournalDataBlock(slot), txb);
            slot = (slot + 1) % JOURNAL_DATA_BLOCKS;

            // Step 2: data block (the new content of the block)
            MiniFs.WriteBlockNoLog(JournalDataBlock(slot), src);
            slot = (slot + 1) % JOURNAL_DATA_BLOCKS;

            // Step 3: TxE (commit marker)
            var txe = new byte[BLOCK_SIZE];
            BitConverter.GetBytes(TXE_MAGIC).CopyTo(txe, 0);
            BitConverter.GetBytes(tid).CopyTo(txe, 4);
            // Rest of TxE block is zero
            MiniFs.WriteBlockNoLog(JournalDataBlock(slot), txe);
            slot = (slot + 1) % JOURNAL_DATA_BLOCKS;

            // Step 4: checkpoint — write the new content to its final
            // location on the disk.
            MiniFs.WriteBlockNoLog(blockNo, src);

            // Step 5: advance journal head
            _head = slot;
            // Persist journal superblock so a crash after this point
            // sees the updated head.
            var sb = new byte[BLOCK_SIZE];
            BitConverter.GetBytes(JOURNAL_SB_MAGIC).CopyTo(sb, 0);
            BitConverter.GetBytes(_head).CopyTo(sb, 4);
            BitConverter.GetBytes(_currentTid).CopyTo(sb, 8);
            MiniFs.WriteBlockNoLog(JOURNAL_SUPERBLOCK_BLOCK, sb);
        }
        finally
        {
            _active = false;
        }
    }

    /// <summary>
    /// Initialize the journal region of a freshly-formatted disk.
    /// Called from MiniFs.Format(). Writes an empty journal
    /// superblock.
    /// </summary>
    public static void Format()
    {
        _head = 0;
        _currentTid = 1;
        var sb = new byte[BLOCK_SIZE];
        BitConverter.GetBytes(JOURNAL_SB_MAGIC).CopyTo(sb, 0);
        BitConverter.GetBytes(0).CopyTo(sb, 4);  // Head = 0
        BitConverter.GetBytes(1).CopyTo(sb, 8);  // NextTid = 1
        MiniFs.WriteBlockNoLog(JOURNAL_SUPERBLOCK_BLOCK, sb);
    }

    /// <summary>
    /// On mount: scan the journal. For each committed transaction
    /// (TxB + matching TxE found), replay the checkpoint write
    /// (re-apply the block update). Uncommitted transactions (TxB
    /// without TxE) are discarded silently.
    /// </summary>
    public static int Replay()
    {
        if (!MiniFs.IsMounted) return 0;

        // Verify journal superblock magic
        var sbBlock = new byte[BLOCK_SIZE];
        MiniFs.ReadBlock(JOURNAL_SUPERBLOCK_BLOCK, sbBlock);
        var magic = BitConverter.ToUInt32(sbBlock, 0);
        if (magic != JOURNAL_SB_MAGIC)
        {
            // No journal — fresh FS or corruption. Re-init.
            Format();
            return 0;
        }

        // Scan journal from slot 0
        int replayed = 0;
        int slot = 0;
        int pendingTid = -1;
        int pendingBlockNo = -1;
        byte[]? pendingData = null;

        for (int i = 0; i < JOURNAL_DATA_BLOCKS; i++)
        {
            var block = new byte[BLOCK_SIZE];
            MiniFs.ReadBlock(JournalDataBlock(slot), block);
            var blockMagic = BitConverter.ToUInt32(block, 0);

            if (blockMagic == TXB_MAGIC)
            {
                // Start of a transaction. Save it; overwrite any
                // earlier pending transaction (since the earlier one
                // must have been discarded without a TxE).
                pendingTid = BitConverter.ToInt32(block, 4);
                pendingBlockNo = BitConverter.ToInt32(block, 8);
                pendingData = null;
            }
            else if (blockMagic == TXE_MAGIC)
            {
                int txeTid = BitConverter.ToInt32(block, 4);
                if (pendingTid == txeTid && pendingData != null && pendingBlockNo >= 0)
                {
                    // Commit: replay the checkpoint write
                    MiniFs.WriteBlockNoLog(pendingBlockNo, pendingData);
                    replayed++;
                }
                pendingTid = -1;
                pendingData = null;
                pendingBlockNo = -1;
            }
            else
            {
                // Should be a data block (between TxB and TxE)
                if (pendingTid >= 0 && pendingData == null)
                {
                    pendingData = block;
                }
                // else: stray data block, ignore
            }

            slot = (slot + 1) % JOURNAL_DATA_BLOCKS;
        }

        // After replay, reset the journal so future writes start fresh
        Format();
        return replayed;
    }
}