using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// Drives the M32 FTL scenarios for the <c>/ssd/run</c> route.
/// </summary>
/// <remarks>
/// Kept out of Program.cs so the route stays a dispatch line, matching how
/// M25/M26/M27 keep their demo formatting next to the simulator it describes.
/// </remarks>
public static class FtlDemos
{
    /// <summary>
    /// Pull the query string apart. The routes in this project parse their
    /// parameters by hand, and this keeps the M32 parameters from adding a
    /// third spelling of the same loop.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> QueryParts(string path)
    {
        int q = path.IndexOf('?');
        if (q < 0) yield break;
        foreach (var part in path.Substring(q + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            yield return new KeyValuePair<string, string>(part.Substring(0, eq), part.Substring(eq + 1));
        }
    }
    /// <summary>OSEP §44.9's own capacity example, so the numbers are checkable against the book.</summary>
    private const long OneTb = 1L * 1024 * 1024 * 1024 * 1024;
    private const int PageBytes = 4096;          // OSEP §44.9: "one entry for each 4-KB page"
    private const int BlockBytes = 256 * 1024;   // §44.9: "real physical blocks can be 256KB or larger"
    private const int EntryBytes = 4;            // §44.9: "a single 4-byte entry"

    public static string RunComparison(string strategy, int writes, string workload, int lbaSpace, int logBlocks)
    {
        var rows = new List<FtlComparison>();
        if (strategy == "all")
        {
            foreach (var s in new[] { "pagelevel", "blocklevel", "hybrid" })
                rows.Add(FtlBase.Compare(s, writes, workload, lbaSpace, logBlocks));
        }
        else
        {
            rows.Add(FtlBase.Compare(strategy, writes, workload, lbaSpace, logBlocks));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"=== FTL strategy comparison (M32 / OSEP §44.9) ===");
        sb.AppendLine($"workload: {writes} writes, {workload}, logical space {lbaSpace}, {logBlocks} log blocks");
        sb.AppendLine();
        sb.AppendLine("strategy   | map entries | host bytes | data bytes | write amp");
        sb.AppendLine("-----------|-------------|------------|------------|----------");
        foreach (var r in rows)
        {
            sb.Append($"{r.Strategy,-10} | {r.MappingEntries,11} | {r.HostBytesWritten,10} | {r.DataBytesWritten,10} | {r.WriteAmplification,9:F2}x");
            if (r.Strategy == "hybrid")
                sb.Append($"   (log {r.LogTableEntries} + data {r.DataTableEntries})");
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("Hybrid's split is measured before the merge: those per-page log entries become");
        sb.AppendLine("block pointers once the log blocks are folded in, which is §44.9's whole point.");

        var pageRow = rows.FirstOrDefault(r => r.Strategy == "page-level");
        var blockRow = rows.FirstOrDefault(r => r.Strategy == "block-level");
        var hybridRow = rows.FirstOrDefault(r => r.Strategy == "hybrid");
        if (blockRow is not null)
        {
            sb.AppendLine("Block-level pays for its small mapping table here because a block pointer");
            sb.AppendLine("cannot address one page. §44.9: a small write makes the FTL \"read a large");
            sb.AppendLine("amount of live data from the old block and copy it into a new one\".");
        }
        if (hybridRow is not null && blockRow is not null && pageRow is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Hybrid lands between them: per-page log pointers keep the small writes cheap,");
            sb.AppendLine("and a merge folds each log block into a single block pointer. Its table is the");
            sb.AppendLine("block table plus a bounded log table, not the page-level table.");
        }

        sb.AppendLine();
        sb.AppendLine("Every address was read back after the merge; a strategy that lost data during");
        sb.AppendLine("cleaning would have thrown instead of printing this table.");
        return sb.ToString();
    }

    public static string RunMappingCost(int logBlocks)
    {
        var t = FtlMappingCost.TableBytes(OneTb, PageBytes, BlockBytes, EntryBytes, logBlocks);
        int pagesPerBlock = BlockBytes / PageBytes;

        var sb = new StringBuilder();
        sb.AppendLine("=== Mapping table cost of a 1-TB SSD (M32 / OSEP §44.9) ===");
        sb.AppendLine($"page {PageBytes / 1024} KB, block {BlockBytes / 1024} KB ({pagesPerBlock} pages/block), {EntryBytes}-byte entry");
        sb.AppendLine();
        sb.AppendLine("strategy   | table size");
        sb.AppendLine("-----------|----------------------------");
        sb.AppendLine($"page-level | {FormatBytes(t.PageLevelBytes)}");
        sb.AppendLine($"block-level| {FormatBytes(t.BlockLevelBytes)}");
        sb.AppendLine($"hybrid     | {FormatBytes(t.HybridBytes)}  (block table + {logBlocks} log blocks)");
        sb.AppendLine();
        sb.AppendLine("§44.9, verbatim: \"With a large 1-TB SSD, for example, a single 4-byte entry per 4-KB");
        sb.AppendLine("page results in 1 GB of memory needed by the device, just for these mappings!");
        sb.AppendLine("Thus, this page-level FTL scheme is impractical.\"");
        sb.AppendLine();
        sb.AppendLine($"Block-level mapping cuts the table by Size_block/Size_page = {(double)BlockBytes / PageBytes:F0}x,");
        sb.AppendLine($"taking {FormatBytes(t.PageLevelBytes)} down to {FormatBytes(t.BlockLevelBytes)} - the amount");
        sb.AppendLine("§44.9's worked example was reaching for.");
        return sb.ToString();
    }

    public static string RunMerges(int pagesPerBlock)
    {
        var sw = HybridMergeDemo.SwitchMerge();
        var pt = HybridMergeDemo.PartialMerge();
        var fl = HybridMergeDemo.FullMerge();

        var sb = new StringBuilder();
        sb.AppendLine("=== Hybrid FTL merges (M32 / OSEP §44.9) ===");
        sb.AppendLine($"pages per block: {HybridMergeDemo.PagesPerBlock} (the chapter's four-pages-per-block example)");
        sb.AppendLine();
        sb.AppendLine("merge   | pages copied | pages written");
        sb.AppendLine("--------|--------------|--------------");
        sb.AppendLine($"switch  | {sw.PagesCopied,12} | {sw.PagesWritten,13}");
        sb.AppendLine($"partial | {pt.PagesCopied,12} | {pt.PagesWritten,13}");
        sb.AppendLine($"full    | {fl.PagesCopied,12} | {fl.PagesWritten,13}");
        sb.AppendLine();
        sb.AppendLine("§44.9 on the switch merge: \"In this best case, all the per-page pointers required");
        sb.AppendLine("replaced by a single block pointer.\" Nothing is copied - the log block already holds");
        sb.AppendLine("the finished chunk, so it just becomes the data block.");
        sb.AppendLine();
        sb.AppendLine("On the full merge: the FTL \"must pull together pages from many other blocks\", which");
        sb.AppendLine("is why the chapter says frequent full merges \"can seriously harm performance\".");
        return sb.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }
}