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
/// Transaction format (multi-block, slice 12.6):
///   TxB: [TXB_MAGIC=0xAABBCCDD | TID | count | blockNo1 | blockNo2 | ...]
///   DATA_1: [4096 bytes of new content for blockNo1]
///   DATA_2: [4096 bytes of new content for blockNo2]
///   ...
///   TxE:  [TXE_MAGIC=0xDDCCBBAA | TID]
///
/// Protocol (per transaction, blocking write-through):
///   1. TxB: write transaction-begin block.
///   2. For each (blockNo, newData): write the new data block.
///   3. TxE: write transaction-end block (commit marker).
///   4. Checkpoint: write each (blockNo, newData) to its FINAL
///      position on disk. If we crash before step 4 completes,
///      recovery will replay the committed transaction (redo
///      logging).
///   5. Update journal superblock (advance head).
///
/// Recovery (on Mount): scan the journal from the start. For each
/// transaction that has both a TxB and a matching TxE, replay the
/// checkpoint writes in order. Discard any TxB without a matching
/// TxE (uncommitted).
///
/// Multi-block transactions (slice 12.6) let CreateFile and similar
/// multi-write operations become atomic across the inode bitmap +
/// inode + directory data updates.
/// </summary>
public static class Journal
{
    private static int _currentTid = 1;
    private static int _head = 0;  // next free slot in journal data region
    private static bool _txActive = false;
    private static readonly List<(int blockNo, byte[] data)> _txBuffer = new();

    // --- On-disk layout helpers ---

    private static int JournalDataBlock(int slot)
    {
        // slot 0..(JOURNAL_DATA_BLOCKS-1)
        return JOURNAL_DATA_START + (slot % JOURNAL_DATA_BLOCKS);
    }

    // --- Transaction API ---

    /// <summary>
    /// Open a multi-block transaction. All MiniFs.WriteBlock calls
    /// made while a transaction is active are buffered into the
    /// transaction instead of being journaled immediately.
    /// </summary>
    public static void Begin()
    {
        if (_txActive)
            throw new InvalidOperationException("nested journal transactions not supported");
        _txBuffer.Clear();
        _txActive = true;
    }

    /// <summary>
    /// Append a block update to the current transaction. Must be
    /// called between Begin() and Commit()/Abort().
    /// </summary>
    public static void Append(int blockNo, byte[] data)
    {
        if (!_txActive)
            throw new InvalidOperationException("Append called outside a transaction");
        if (blockNo < 0 || blockNo >= NUM_BLOCKS)
            throw new ArgumentOutOfRangeException(nameof(blockNo));
        if (data.Length != BLOCK_SIZE)
            throw new ArgumentException("data must be BLOCK_SIZE bytes", nameof(data));
        if (_txBuffer.Count >= MAX_BLOCKS_PER_TX)
            throw new InvalidOperationException("transaction too large");
        _txBuffer.Add((blockNo, data));
    }

    /// <summary>
    /// Commit the current transaction: write TxB + each DATA + TxE
    /// to the journal region, then checkpoint each update to its
    /// final position, then advance the journal head.
    /// </summary>
    public static void Commit()
    {
        if (!_txActive)
            throw new InvalidOperationException("Commit called outside a transaction");

        if (_txBuffer.Count == 0)
        {
            // Empty transaction — just clear the buffer
            _txBuffer.Clear();
            _txActive = false;
            return;
        }

        int tid = _currentTid++;
        int slot = _head;

        // Step 1: TxB
        var txb = new byte[BLOCK_SIZE];
        BitConverter.GetBytes(TXB_MAGIC).CopyTo(txb, 0);
        BitConverter.GetBytes(tid).CopyTo(txb, 4);
        BitConverter.GetBytes(_txBuffer.Count).CopyTo(txb, 8);
        for (int i = 0; i < _txBuffer.Count; i++)
            BitConverter.GetBytes(_txBuffer[i].blockNo).CopyTo(txb, 12 + i * 4);
        MiniFs.WriteBlockNoLog(JournalDataBlock(slot), txb);
        slot = (slot + 1) % JOURNAL_DATA_BLOCKS;

        // Step 2: each DATA block
        foreach (var (blockNo, data) in _txBuffer)
        {
            MiniFs.WriteBlockNoLog(JournalDataBlock(slot), data);
            slot = (slot + 1) % JOURNAL_DATA_BLOCKS;
        }

        // Step 3: TxE
        var txe = new byte[BLOCK_SIZE];
        BitConverter.GetBytes(TXE_MAGIC).CopyTo(txe, 0);
        BitConverter.GetBytes(tid).CopyTo(txe, 4);
        MiniFs.WriteBlockNoLog(JournalDataBlock(slot), txe);
        slot = (slot + 1) % JOURNAL_DATA_BLOCKS;

        // Step 4: checkpoint each update to its final position
        foreach (var (blockNo, data) in _txBuffer)
            MiniFs.WriteBlockNoLog(blockNo, data);

        // Step 5: advance journal head
        _head = slot;
        _txBuffer.Clear();
        _txActive = false;

        // Persist journal superblock so a crash after this point
        // sees the updated head. Slice 12.6: Tail=Head because every
        // tx is fully checkpointed during Commit, so the active
        // journal range is always empty at the moment we persist.
        var sb = new byte[BLOCK_SIZE];
        BitConverter.GetBytes(JOURNAL_SB_MAGIC).CopyTo(sb, 0);
        BitConverter.GetBytes(_head).CopyTo(sb, 4);
        BitConverter.GetBytes(_currentTid).CopyTo(sb, 8);
        BitConverter.GetBytes(_head).CopyTo(sb, 12);   // Tail
        MiniFs.WriteBlockNoLog(JOURNAL_SUPERBLOCK_BLOCK, sb);
    }

    /// <summary>
    /// Discard the current transaction without checkpointing. Used
    /// when a higher-level operation fails and we want to roll back
    /// the in-memory state.
    /// </summary>
    public static void Abort()
    {
        if (!_txActive) return;
        _txBuffer.Clear();
        _txActive = false;
    }

    /// <summary>Whether we're currently inside a Begin()/Commit() block.</summary>
    public static bool InTransaction => _txActive;

    /// <summary>
    /// Slice 12.6: returns the latest pending write for <paramref name="blockNo"/>
    /// in the current transaction, or null if there is no pending
    /// write. Used by MiniFs.ReadBlock so subsequent reads inside a
    /// multi-block transaction see the latest snapshot of a block
    /// that has already been queued for write.
    /// </summary>
    public static byte[]? GetPendingWrite(int blockNo)
    {
        if (!_txActive) return null;
        // Iterate backwards so the LATEST Append for the same blockNo wins.
        for (int i = _txBuffer.Count - 1; i >= 0; i--)
        {
            if (_txBuffer[i].blockNo == blockNo)
                return _txBuffer[i].data;
        }
        return null;
    }

    /// <summary>
    /// Backward-compatible single-block journal write. When called
    /// outside a transaction, this is a 1-block transaction. Used by
    /// code paths that don't need batching (e.g., Iinit's initial inode
    /// write during CreateFile is included in the multi-block tx).
    /// </summary>
    public static void WriteBlockJournaled(int blockNo, byte[] src)
    {
        if (_txActive)
        {
            Append(blockNo, src);
            return;
        }
        Begin();
        Append(blockNo, src);
        Commit();
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
        _txBuffer.Clear();
        _txActive = false;
        var sb = new byte[BLOCK_SIZE];
        BitConverter.GetBytes(JOURNAL_SB_MAGIC).CopyTo(sb, 0);
        BitConverter.GetBytes(0).CopyTo(sb, 4);  // Head = 0
        BitConverter.GetBytes(1).CopyTo(sb, 8);  // NextTid = 1
        BitConverter.GetBytes(0).CopyTo(sb, 12); // Tail = 0
        MiniFs.WriteBlockNoLog(JOURNAL_SUPERBLOCK_BLOCK, sb);
    }

    /// <summary>
    /// On mount: scan the journal from Tail to Head (slice 12.6).
    /// For each committed transaction (TxB + matching TxE found),
    /// replay the checkpoint writes in order. Uncommitted
    /// transactions (TxB without TxE) are discarded silently.
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

        int tail = BitConverter.ToInt32(sbBlock, 12);
        int head = BitConverter.ToInt32(sbBlock, 4);

        // Scan from Tail to Head modulo JOURNAL_DATA_BLOCKS.
        // Slots in [head..tail) modulo size are stale and skipped.
        int replayed = 0;
        int pendingTid = -1;
        int pendingCount = 0;
        int[] pendingBlockNos = new int[MAX_BLOCKS_PER_TX];
        var pendingData = new Dictionary<int, byte[]>();

        for (int i = 0; i < JOURNAL_DATA_BLOCKS; i++)
        {
            int slot = (tail + i) % JOURNAL_DATA_BLOCKS;
            if (slot == head) break;  // reached the active write position

            var block = new byte[BLOCK_SIZE];
            MiniFs.ReadBlock(JournalDataBlock(slot), block);
            var blockMagic = BitConverter.ToUInt32(block, 0);

            if (blockMagic == TXB_MAGIC)
            {
                pendingTid = BitConverter.ToInt32(block, 4);
                pendingCount = BitConverter.ToInt32(block, 8);
                for (int k = 0; k < pendingCount && k < MAX_BLOCKS_PER_TX; k++)
                    pendingBlockNos[k] = BitConverter.ToInt32(block, 12 + k * 4);
                pendingData.Clear();
            }
            else if (blockMagic == TXE_MAGIC)
            {
                int txeTid = BitConverter.ToInt32(block, 4);
                if (pendingTid == txeTid && pendingData.Count == pendingCount && pendingCount > 0)
                {
                    for (int k = 0; k < pendingCount; k++)
                    {
                        if (pendingData.TryGetValue(pendingBlockNos[k], out var data))
                            MiniFs.WriteBlockNoLog(pendingBlockNos[k], data);
                    }
                    replayed++;
                }
                pendingTid = -1;
                pendingCount = 0;
                pendingData.Clear();
            }
            else
            {
                int dataIdx = pendingData.Count;
                if (pendingTid >= 0 && dataIdx < pendingCount)
                {
                    int blockNo = pendingBlockNos[dataIdx];
                    pendingData[blockNo] = block;
                }
            }
        }

        // After replay, reset the journal so future writes start fresh
        Format();
        return replayed;
    }
}