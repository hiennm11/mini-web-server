using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// FFS placement simulator (OSEP Ch. 41).
///
/// OSEP §41.4 "Policies: How To Allocate Files and Directories":
///   "The basic mantra is simple: keep related stuff together (and its
///    corollary, keep unrelated stuff far apart). Thus, to obey the mantra,
///    FFS has to decide what is 'related' and place it within the same block
///    group; conversely, unrelated items should be placed into different
///    block groups. ... For files, FFS does two things. First, it makes sure
///    (in the general case) to allocate the data blocks of a file in the
///    same group as its inode, thus preventing long seeks between inode and
///    data ... Second, it places all files that are in the same directory
///    in the cylinder group of the directory they are in."
///
/// OSEP §41.6 "The Large-File Exception":
///   "After some number of blocks are allocated into the first block group
///    (e.g., 12 blocks, or the number of direct pointers available within
///    an inode), FFS places the next 'large' chunk of the file (e.g., those
///    pointed to by the first indirect block) in another block group
///    (perhaps chosen for its low utilization). Then, the next chunk of
///    the file is placed in yet another different block group, and so on."
///
/// Our simulator:
///   - N block groups (default 8), each with K inodes + M data blocks.
///   - Files placed in same group as parent dir (FFS §41.4 policy).
///   - Directories placed in the group with the most free inodes
///     (FFS §41.4 directory policy: "low number of allocated directories
///     ... and a high number of free inodes").
///   - Large files (size > largeFileThreshold, default 12 per OSEP §41.7):
///     first 12 blocks in same group as inode, then rotate to other groups
///     in 12-block chunks.
///   - Reports layout grid + filespan + dirspan metrics
///     (OSEP homework §41.7 question 3 + 5).
/// </summary>
public sealed class FFS
{
    public readonly int GroupCount;
    public readonly int InodesPerGroup;
    public readonly int BlocksPerGroup;
    public readonly int LargeFileThreshold;

    private readonly List<BlockGroup> _groups;
    private readonly Dictionary<string, FfsFile> _files = new(StringComparer.Ordinal);
    private int _nextInodeId = 0;
    private int _nextBlockId = 0;

    public FFS(int groupCount = 8, int inodesPerGroup = 5, int blocksPerGroup = 16, int largeFileThreshold = 12)
    {
        if (groupCount < 1) throw new ArgumentOutOfRangeException(nameof(groupCount));
        if (inodesPerGroup < 1) throw new ArgumentOutOfRangeException(nameof(inodesPerGroup));
        if (blocksPerGroup < 1) throw new ArgumentOutOfRangeException(nameof(blocksPerGroup));
        GroupCount = groupCount;
        InodesPerGroup = inodesPerGroup;
        BlocksPerGroup = blocksPerGroup;
        LargeFileThreshold = largeFileThreshold;

        _groups = new List<BlockGroup>(groupCount);
        for (int g = 0; g < groupCount; g++)
            _groups.Add(new BlockGroup(g, InodesPerGroup, BlocksPerGroup));
    }

    public IReadOnlyList<BlockGroup> Groups => _groups;
    public IReadOnlyDictionary<string, FfsFile> Files => _files;

    /// <summary>Allocate a directory with the given name + parent. Returns true on success.</summary>
    public bool CreateDir(string name, string? parent = null)
    {
        int groupId = PickDirGroup();
        if (groupId < 0) return false;
        var grp = _groups[groupId];
        int inodeIdx = grp.AllocateInode();
        if (inodeIdx < 0) return false;
        int inodeId = _nextInodeId++;
        var f = new FfsFile
        {
            Name = name,
            InodeId = inodeId,
            GroupId = groupId,
            InodeIdx = inodeIdx,
            IsDir = true,
            Size = 0,
        };
        _files[name] = f;
        grp.IncrementDirCount();
        return true;
    }

    /// <summary>
    /// Allocate a file of `size` blocks with the given name in the given parent dir.
    /// Per FFS §41.4: data blocks go to the same group as the parent directory's
    /// group, except for large files which rotate per §41.6.
    /// </summary>
    public bool CreateFile(string name, string parent, int size)
    {
        if (!_files.TryGetValue(parent, out var parentDir) || !parentDir.IsDir) return false;
        int parentGroup = parentDir.GroupId;

        // Allocate inode in parent's group.
        var grp = _groups[parentGroup];
        int inodeIdx = grp.AllocateInode();
        if (inodeIdx < 0) return false;
        int inodeId = _nextInodeId++;

        var f = new FfsFile
        {
            Name = name,
            InodeId = inodeId,
            GroupId = parentGroup,
            InodeIdx = inodeIdx,
            IsDir = false,
            Size = size,
        };

        // Allocate data blocks.
        // Small files: all in parent's group.
        // Large files: first L blocks in parent's group, then rotate.
        int allocated = 0;
        int currentGroup = parentGroup;
        int groupRotationIdx = 0;  // which group we're rotating to

        while (allocated < size)
        {
            var curGrp = _groups[currentGroup];
            int toAllocateInThisGroup = Math.Min(LargeFileThreshold, size - allocated);

            // For small files, allocate everything in one go.
            if (size <= LargeFileThreshold)
            {
                toAllocateInThisGroup = size - allocated;
            }

            for (int i = 0; i < toAllocateInThisGroup && allocated < size; i++)
            {
                int blockIdx = curGrp.AllocateBlock();
                if (blockIdx < 0)
                {
                    // Group full; try next group.
                    currentGroup = NextGroup(currentGroup, ref groupRotationIdx);
                    if (currentGroup < 0) return false;
                    break;
                }
                f.DataBlocks.Add(new FfsBlockRef(currentGroup, blockIdx, _nextBlockId++));
                allocated++;
            }

            // If file is small and we're done, break.
            if (size <= LargeFileThreshold) break;

            // Large file: rotate to next group.
            currentGroup = NextGroup(currentGroup, ref groupRotationIdx);
            if (currentGroup < 0) return false;
        }

        _files[name] = f;
        return true;
    }

    /// <summary>
    /// OSEP §41.4 "find the cylinder group with a low number of allocated directories
    /// (to balance directories across groups) and a high number of free inodes".
    /// </summary>
    private int PickDirGroup()
    {
        int bestGroup = -1;
        int bestScore = int.MinValue;
        for (int g = 0; g < _groups.Count; g++)
        {
            var grp = _groups[g];
            if (!grp.HasFreeInode()) continue;
            // Higher score = better. Free inodes: +100 each. Existing dirs: -50 each.
            int score = grp.FreeInodes() * 100 - grp.DirCount * 50;
            if (score > bestScore)
            {
                bestScore = score;
                bestGroup = g;
            }
        }
        return bestGroup;
    }

    private int NextGroup(int current, ref int rotationIdx)
    {
        // Pick the next group with free data blocks (round-robin).
        for (int i = 1; i <= _groups.Count; i++)
        {
            int g = (current + i) % _groups.Count;
            if (_groups[g].HasFreeBlock()) return g;
        }
        return -1;
    }

    /// <summary>
    /// OSEP §41.7 question 3 "filespan: the max distance between any two data
    /// blocks of the file or between the inode and any data block."
    /// Distance = global block index diff (proxy for seek distance).
    /// </summary>
    public int Filespan(string fileName)
    {
        if (!_files.TryGetValue(fileName, out var f) || f.IsDir) return 0;
        var inodeBlock = (f.GroupId * BlocksPerGroup) + f.InodeIdx;  // rough proxy
        var positions = new List<int> { inodeBlock };
        foreach (var b in f.DataBlocks) positions.Add((b.GroupId * BlocksPerGroup) + b.Idx);
        if (positions.Count < 2) return 0;
        int min = int.MaxValue, max = int.MinValue;
        foreach (var p in positions) { if (p < min) min = p; if (p > max) max = p; }
        return max - min;
    }

    /// <summary>
    /// OSEP §41.7 question 5 "dirspan: max distance between the inodes and data
    /// blocks of all files in the directory and the inode and data block of
    /// the directory itself."
    /// </summary>
    public int Dirspan(string dirName)
    {
        if (!_files.TryGetValue(dirName, out var dir) || !dir.IsDir) return 0;
        var dirInodeBlock = (dir.GroupId * BlocksPerGroup) + dir.InodeIdx;
        var positions = new HashSet<int> { dirInodeBlock };

        // Add every inode + data block of every file/dir whose parent == dirName.
        foreach (var f in _files.Values)
        {
            if (f == dir) continue;
            // In our simple model, files are referenced by parent name; we don't track that.
            // Heuristic: a file belongs to the dir that has the same name prefix.
            // For simplicity, find all entries whose name starts with "dirName/".
            string prefix = dirName + "/";
            if (!f.Name.StartsWith(prefix)) continue;
            positions.Add((f.GroupId * BlocksPerGroup) + f.InodeIdx);
            foreach (var b in f.DataBlocks) positions.Add((b.GroupId * BlocksPerGroup) + b.Idx);
        }
        if (positions.Count < 2) return 0;
        int min = int.MaxValue, max = int.MinValue;
        foreach (var p in positions) { if (p < min) min = p; if (p > max) max = p; }
        return max - min;
    }

    /// <summary>Pretty-print the layout as a grid: one row per group, columns show inodes+data.</summary>
    public string FormatLayout()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== FFS Layout (M21 / OSEP Ch. 41) ===");
        sb.AppendLine($"groups: {GroupCount}  inodes/group: {InodesPerGroup}  blocks/group: {BlocksPerGroup}  large-file threshold: {LargeFileThreshold}");
        sb.AppendLine();
        sb.AppendLine($"group | inode-usage | data-usage");
        sb.AppendLine($"------|-------------|----------");
        for (int g = 0; g < _groups.Count; g++)
        {
            var grp = _groups[g];
            int inodesUsed = grp.InodesUsed;
            int blocksUsed = grp.BlocksUsed;
            int dirCount = grp.DirCount;
            sb.AppendLine($"  {g,-3} | {inodesUsed}/{InodesPerGroup} (dirs={dirCount})  | {blocksUsed}/{BlocksPerGroup}");
        }

        sb.AppendLine();
        sb.AppendLine("=== file details ===");
        foreach (var f in _files.Values)
        {
            sb.Append($"  {f.Name} [inode {f.InodeId} in group {f.GroupId} idx {f.InodeIdx}]");
            if (f.IsDir) { sb.AppendLine(" (dir)"); continue; }
            sb.AppendLine($" size={f.Size} blocks -> [{string.Join(",", f.DataBlocks.Select(b => $"g{b.GroupId}:b{b.Idx}"))}]");
            sb.AppendLine($"    filespan = {Filespan(f.Name)}");
        }
        // Compute dirspan for every directory.
        bool anyDirs = false;
        foreach (var f in _files.Values)
        {
            if (!f.IsDir) continue;
            if (!anyDirs) { sb.AppendLine(); sb.AppendLine("=== dirspan ==="); anyDirs = true; }
            sb.AppendLine($"  {f.Name}: dirspan = {Dirspan(f.Name)}");
        }
        return sb.ToString();
    }
}

public sealed class BlockGroup
{
    public readonly int Id;
    private readonly bool[] _inodeBitmap;
    private readonly bool[] _dataBitmap;
    public int DirCount { get; private set; }

    public BlockGroup(int id, int inodesPerGroup, int blocksPerGroup)
    {
        Id = id;
        _inodeBitmap = new bool[inodesPerGroup];
        _dataBitmap = new bool[blocksPerGroup];
    }

    public int InodesUsed
    {
        get { int n = 0; foreach (var b in _inodeBitmap) if (b) n++; return n; }
    }
    public int BlocksUsed
    {
        get { int n = 0; foreach (var b in _dataBitmap) if (b) n++; return n; }
    }
    public int FreeInodes() => _inodeBitmap.Length - InodesUsed;
    public bool HasFreeInode() => FreeInodes() > 0;
    public bool HasFreeBlock() => (_dataBitmap.Length - BlocksUsed) > 0;

    public int AllocateInode()
    {
        for (int i = 0; i < _inodeBitmap.Length; i++)
        {
            if (!_inodeBitmap[i]) { _inodeBitmap[i] = true; return i; }
        }
        return -1;
    }

    public int AllocateBlock()
    {
        for (int i = 0; i < _dataBitmap.Length; i++)
        {
            if (!_dataBitmap[i]) { _dataBitmap[i] = true; return i; }
        }
        return -1;
    }

    public void IncrementDirCount() => DirCount++;
}

public sealed class FfsFile
{
    public string Name { get; set; } = "";
    public int InodeId { get; set; }
    public int GroupId { get; set; }
    public int InodeIdx { get; set; }
    public bool IsDir { get; set; }
    public int Size { get; set; }
    public List<FfsBlockRef> DataBlocks { get; } = new();
}

public sealed record FfsBlockRef(int GroupId, int Idx, int GlobalId);
