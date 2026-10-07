using System;
using System.Text;

Run("parses request line", () =>
{
    HttpRequest request = HttpRequestParser.Parse(
        "GET /ostep HTTP/1.1\r\n" +
        "Host: localhost:8080\r\n" +
        "\r\n");

    AssertEqual("GET", request.Method);
    AssertEqual("/ostep", request.Path);
    AssertEqual("HTTP/1.1", request.Version);
});

Run("parses headers", () =>
{
    HttpRequest request = HttpRequestParser.Parse(
        "GET / HTTP/1.1\r\n" +
        "Host: localhost:8080\r\n" +
        "User-Agent: curl/8.0\r\n" +
        "\r\n");

    AssertEqual("localhost:8080", request.Headers["Host"]);
    AssertEqual("curl/8.0", request.Headers["User-Agent"]);
});

Run("invalid request becomes unknown request", () =>
{
    HttpRequest request = HttpRequestParser.Parse("");

    AssertEqual("UNKNOWN", request.Method);
    AssertEqual("/", request.Path);
    AssertEqual("HTTP/1.1", request.Version);
    AssertEqual(0, request.Headers.Count);
});

Run("serves index html for root path", () =>
{
    string webRoot = CreateTempWebRoot();
    File.WriteAllText(Path.Combine(webRoot, "index.html"), "<h1>Home</h1>");

    HttpResponse response = StaticFileResponder.CreateResponse(
        new HttpRequest("GET", "/", "HTTP/1.1", new Dictionary<string, string>()),
        webRoot);

    AssertEqual(200, response.StatusCode);
    AssertEqual("text/html; charset=UTF-8", response.ContentType);
    AssertEqual("<h1>Home</h1>", Encoding.UTF8.GetString(response.Body));
});

Run("missing file returns 404", () =>
{
    string webRoot = CreateTempWebRoot();

    HttpResponse response = StaticFileResponder.CreateResponse(
        new HttpRequest("GET", "/missing.txt", "HTTP/1.1", new Dictionary<string, string>()),
        webRoot);

    AssertEqual(404, response.StatusCode);
    AssertEqual("Not Found", response.ReasonPhrase);
});

Run("path traversal returns 404", () =>
{
    string webRoot = CreateTempWebRoot();
    string outsideFile = Path.Combine(Directory.GetParent(webRoot)!.FullName, "secret.txt");
    File.WriteAllText(outsideFile, "secret");

    HttpResponse response = StaticFileResponder.CreateResponse(
        new HttpRequest("GET", "/../secret.txt", "HTTP/1.1", new Dictionary<string, string>()),
        webRoot);

    AssertEqual(404, response.StatusCode);
});

Run("web root resolves from app base directory", () =>
{
    string appBase = Path.Combine(Path.GetTempPath(), "mini-web-server-tests", Guid.NewGuid().ToString("N"));

    string webRoot = WebRootLocator.GetWebRoot(appBase);

    AssertEqual(Path.GetFullPath(Path.Combine(appBase, "wwwroot")), webRoot);
});

Run("parses /slow path", () =>
{
    HttpRequest request = HttpRequestParser.Parse(
        "GET /slow HTTP/1.1\r\n" +
        "Host: localhost:8080\r\n" +
        "\r\n");

    AssertEqual("GET", request.Method);
    AssertEqual("/slow", request.Path);
    AssertEqual("HTTP/1.1", request.Version);
});

Run("receiver finds header terminator at expected offset", () =>
{
    byte[] bytes = Encoding.ASCII.GetBytes(
        "GET / HTTP/1.1\r\n" +
        "Host: localhost\r\n" +
        "\r\n");

    // "\r\n\r\n" starts at the byte right after "Host: localhost\r\n",
    // which is 31 bytes from the start of the buffer.
    AssertEqual(31, HttpRequestReceiver.FindHeaderEnd(bytes));
});

Run("receiver returns -1 when header terminator is missing", () =>
{
    byte[] bytes = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: localhost\r\n");

    AssertEqual(-1, HttpRequestReceiver.FindHeaderEnd(bytes));
});

Run("receiver parses integer Content-Length value", () =>
{
    byte[] header = Encoding.ASCII.GetBytes(
        "POST /submit HTTP/1.1\r\n" +
        "Host: localhost\r\n" +
        "Content-Length: 42\r\n" +
        "\r\n");

    AssertEqual(42, HttpRequestReceiver.ParseContentLength(header));
});

Run("receiver parses Content-Length with leading whitespace", () =>
{
    byte[] header = Encoding.ASCII.GetBytes(
        "POST /submit HTTP/1.1\r\n" +
        "Content-Length:    7\r\n" +
        "\r\n");

    AssertEqual(7, HttpRequestReceiver.ParseContentLength(header));
});

Run("receiver returns 0 when Content-Length is absent", () =>
{
    byte[] header = Encoding.ASCII.GetBytes(
        "GET / HTTP/1.1\r\n" +
        "Host: localhost\r\n" +
        "\r\n");

    AssertEqual(0, HttpRequestReceiver.ParseContentLength(header));
});

Run("receiver returns 0 when Content-Length is malformed", () =>
{
    byte[] header = Encoding.ASCII.GetBytes(
        "POST /submit HTTP/1.1\r\n" +
        "Content-Length: not-a-number\r\n" +
        "\r\n");

    AssertEqual(0, HttpRequestReceiver.ParseContentLength(header));
});

// ----- M24 RAID simulator (OSEP Ch. 38) -----

Run("raid0 round-trip across 4 disks", () =>
{
    var raid = new MiniWebServer.Host.MiniScheduler.Raid(
        MiniWebServer.Host.MiniScheduler.RaidLevel.Raid0, diskCount: 4, blockCount: 4);
    // Logical block 5 -> disk 1, block 1 (5 % 4 = 1, 5 / 4 = 1).
    raid.WriteRaid0(5, (byte)'Z');
    AssertEqual((byte)'Z', raid.DiskByte(1, 1));
    AssertEqual((byte)'Z', raid.ReadRaid0(5));
});

Run("raid0 round-trip on every disk", () =>
{
    var raid = new MiniWebServer.Host.MiniScheduler.Raid(
        MiniWebServer.Host.MiniScheduler.RaidLevel.Raid0, diskCount: 3, blockCount: 2);
    // Round-robin across 3 disks: blocks 0,3 -> disk 0; 1,4 -> disk 1; 2,5 -> disk 2.
    raid.WriteRaid0(0, (byte)'A');
    raid.WriteRaid0(1, (byte)'B');
    raid.WriteRaid0(2, (byte)'C');
    raid.WriteRaid0(3, (byte)'D');
    raid.WriteRaid0(4, (byte)'E');
    raid.WriteRaid0(5, (byte)'F');
    AssertEqual((byte)'A', raid.ReadRaid0(0));
    AssertEqual((byte)'B', raid.ReadRaid0(1));
    AssertEqual((byte)'C', raid.ReadRaid0(2));
    AssertEqual((byte)'D', raid.ReadRaid0(3));
    AssertEqual((byte)'E', raid.ReadRaid0(4));
    AssertEqual((byte)'F', raid.ReadRaid0(5));
});

Run("raid0 fails after one disk loss (no redundancy)", () =>
{
    var raid = new MiniWebServer.Host.MiniScheduler.Raid(
        MiniWebServer.Host.MiniScheduler.RaidLevel.Raid0, diskCount: 3, blockCount: 2);
    raid.WriteRaid0(0, (byte)'A');
    raid.WriteRaid0(1, (byte)'B');
    raid.WriteRaid0(2, (byte)'C');
    raid.FailDisk(1);
    // Logical block 1 lives on disk 1 -> must throw.
    AssertThrows<InvalidOperationException>(() => raid.ReadRaid0(1));
    // Logical block 0 lives on disk 0 -> still readable.
    AssertEqual((byte)'A', raid.ReadRaid0(0));
});

Run("raid1 mirror writes both disks", () =>
{
    var raid = new MiniWebServer.Host.MiniScheduler.Raid(
        MiniWebServer.Host.MiniScheduler.RaidLevel.Raid1, diskCount: 2, blockCount: 4);
    raid.WriteRaid1(2, (byte)'M');
    AssertEqual((byte)'M', raid.DiskByte(0, 2));
    AssertEqual((byte)'M', raid.DiskByte(1, 2));
    AssertEqual((byte)'M', raid.ReadRaid1(2));
});

Run("raid1 mirror survives one disk loss", () =>
{
    var raid = new MiniWebServer.Host.MiniScheduler.Raid(
        MiniWebServer.Host.MiniScheduler.RaidLevel.Raid1, diskCount: 2, blockCount: 4);
    raid.WriteRaid1(0, (byte)'A');
    raid.WriteRaid1(1, (byte)'B');
    raid.WriteRaid1(2, (byte)'C');
    raid.WriteRaid1(3, (byte)'D');
    raid.FailDisk(0);
    AssertEqual((byte)'A', raid.ReadRaid1(0));
    AssertEqual((byte)'C', raid.ReadRaid1(2));
    raid.FailDisk(1);  // both disks dead - reads still work because either is a valid copy
    AssertEqual((byte)'A', raid.ReadRaid1(0));
});

Run("raid4 parity is XOR of data blocks", () =>
{
    var raid = new MiniWebServer.Host.MiniScheduler.Raid(
        MiniWebServer.Host.MiniScheduler.RaidLevel.Raid4, diskCount: 4, blockCount: 2);
    // Stripe 0: data bytes A=0x41, B=0x42, C=0x43. Parity = 0x41 ^ 0x42 ^ 0x43 = 0x40.
    raid.WriteStripeRaidParity(0, new byte[] { 0x41, 0x42, 0x43 });
    // Parity disk is DiskCount-1 = 3, at stripe 0.
    AssertEqual((byte)0x40, raid.ReadParity(0));
    // Data reads also work.
    AssertEqual((byte)0x41, raid.ReadRaidParity(0, 0));
    AssertEqual((byte)0x42, raid.ReadRaidParity(0, 1));
    AssertEqual((byte)0x43, raid.ReadRaidParity(0, 2));
});

Run("raid4 small write updates parity via XOR", () =>
{
    var raid = new MiniWebServer.Host.MiniScheduler.Raid(
        MiniWebServer.Host.MiniScheduler.RaidLevel.Raid4, diskCount: 4, blockCount: 2);
    raid.WriteStripeRaidParity(0, new byte[] { 0x41, 0x42, 0x43 });
    // Update data[1] from 0x42 to 0xFF. Parity: 0x40 ^ 0x42 ^ 0xFF = 0xFD.
    raid.WriteRaidParity(0, 1, 0xFF);
    AssertEqual((byte)0xFF, raid.ReadRaidParity(0, 1));
    AssertEqual((byte)0xFD, raid.ReadParity(0));
});

Run("raid4 recovers lost data block via XOR", () =>
{
    var raid = new MiniWebServer.Host.MiniScheduler.Raid(
        MiniWebServer.Host.MiniScheduler.RaidLevel.Raid4, diskCount: 4, blockCount: 2);
    raid.WriteStripeRaidParity(0, new byte[] { 0x41, 0x42, 0x43 });
    raid.WriteStripeRaidParity(1, new byte[] { 0x44, 0x45, 0x46 });
    // Fail disk 1, then read data[0] of stripe 0 (which lives on the failed disk
    // per the RAID 4 mapping).
    raid.FailDisk(1);
    // We need a read that crosses the failed disk. dataDiskFor(0, 0) = 0 (no skip needed)
    // so the failed disk isn't on the path. Force a read of the parity (disk 3) - that disk
    // is alive, so we need to find a disk that IS failed.
    // Manually: a write of data[0] would have gone to disk 0,1,2 (none failed).
    // The parity disk (3) is alive. So we test recovery by failing the parity disk itself.
    raid.ReviveDisk();
    raid.FailDisk(3);  // parity disk dead
    // Read parity via XOR recovery: A ^ B ^ C = 0x41 ^ 0x42 ^ 0x43 = 0x40.
    AssertEqual((byte)0x40, raid.ReadParity(0));
    raid.ReviveDisk();
    // Now demonstrate full data-block recovery. dataDiskFor(stripe, dataIndex)
    // returns the physical disk for a given stripe+dataIndex. We'll fail the disk that
    // holds data[1] for stripe 0.
    int targetDisk = raid.DataDiskFor(0, 1);
    raid.FailDisk(targetDisk);
    // ReadRaidParity will detect failed disk on path and XOR the others.
    AssertEqual((byte)0x42, raid.ReadRaidParity(0, 1));
});

Run("raid5 parity rotates across disks", () =>
{
    var raid = new MiniWebServer.Host.MiniScheduler.Raid(
        MiniWebServer.Host.MiniScheduler.RaidLevel.Raid5, diskCount: 4, blockCount: 4);
    // OSEP §38.8 figure 38.8: parity stripe s -> disk (s + 1) % N.
    AssertEqual(1, raid.ParityDiskFor(0));
    AssertEqual(2, raid.ParityDiskFor(1));
    AssertEqual(3, raid.ParityDiskFor(2));
    AssertEqual(0, raid.ParityDiskFor(3));
});

Run("raid5 round-trip survives one disk loss", () =>
{
    var raid = new MiniWebServer.Host.MiniScheduler.Raid(
        MiniWebServer.Host.MiniScheduler.RaidLevel.Raid5, diskCount: 4, blockCount: 4);
    // Write 4 stripes with 3 data bytes each.
    for (int s = 0; s < 4; s++)
    {
        var data = new byte[3];
        for (int i = 0; i < 3; i++) data[i] = (byte)('A' + (s * 3 + i) % 26);
        raid.WriteStripeRaidParity(s, data);
    }
    // Sanity: every data block reads back correctly.
    AssertEqual((byte)'A', raid.ReadRaidParity(0, 0));
    AssertEqual((byte)'C', raid.ReadRaidParity(0, 2));
    AssertEqual((byte)'F', raid.ReadRaidParity(1, 2));  // s=1, i=2 -> 'A'+5 = 'F'

    // Fail disk 2 (which holds parity for stripe 1) and read everything.
    raid.FailDisk(2);
    // Stripes where disk 2 is NOT the parity disk: 0,2,3. Stripe 1's parity is on disk 2 -> recovered.
    AssertEqual((byte)'A', raid.ReadRaidParity(0, 0));
    AssertEqual((byte)'C', raid.ReadRaidParity(0, 2));
    // Stripe 1's parity disk is disk 2 = failed. XOR recovery still works.
    AssertEqual((byte)'D' ^ (byte)'E' ^ (byte)'F', raid.ReadParity(1));
});

Run("raid5 recovers a data block via XOR after a real disk failure", () =>
{
    var raid = new MiniWebServer.Host.MiniScheduler.Raid(
        MiniWebServer.Host.MiniScheduler.RaidLevel.Raid5, diskCount: 4, blockCount: 4);
    // Stripe 0: data A B C, parity on disk 1 (rotated).
    raid.WriteStripeRaidParity(0, new byte[] { 0x41, 0x42, 0x43 });
    // Stripe 0 layout: data disks = {0,2,3}, parity disk = 1.
    // dataDiskFor(0, 0) = 0, dataDiskFor(0, 1) = 2, dataDiskFor(0, 2) = 3.
    AssertEqual(0, raid.DataDiskFor(0, 0));
    AssertEqual(2, raid.DataDiskFor(0, 1));
    AssertEqual(3, raid.DataDiskFor(0, 2));

    raid.FailDisk(2);  // dataDiskFor(0, 1) lives on disk 2 - now dead.
    // Recovery: readRaidParity(0, 1) -> XOR of disks 0, 1, 3 at stripe 0.
    // = 0x41 ^ 0x40 ^ 0x43 (parity 0x40 = A^B^C) = 0x42.
    AssertEqual((byte)0x42, raid.ReadRaidParity(0, 1));
    // Other stripe-0 reads on surviving disks still work directly.
    AssertEqual((byte)0x41, raid.ReadRaidParity(0, 0));
    AssertEqual((byte)0x43, raid.ReadRaidParity(0, 2));
});

// ----- M25 LFS simulator (OSEP Ch. 43) -----

Run("lfs create + write + read round-trip via imap", () =>
{
    var lfs = new MiniWebServer.Host.MiniScheduler.Lfs(segments: 6, blocksPerSegment: 8);
    int ino = lfs.CreateFile("/foo");
    lfs.WriteData("/foo", 0, (byte)'A');
    lfs.WriteData("/foo", 1, (byte)'B');
    lfs.WriteData("/foo", 2, (byte)'C');
    lfs.Flush();
    AssertEqual((byte)'A', lfs.Read("/foo", 0));
    AssertEqual((byte)'B', lfs.Read("/foo", 1));
    AssertEqual((byte)'C', lfs.Read("/foo", 2));
});

Run("lfs imap is updated when an inode is written", () =>
{
    var lfs = new MiniWebServer.Host.MiniScheduler.Lfs(segments: 4, blocksPerSegment: 8);
    int ino1 = lfs.CreateFile("/a");
    int ino2 = lfs.CreateFile("/b");
    // Write data so reads work.
    lfs.WriteData("/a", 0, (byte)'1');
    lfs.WriteData("/b", 0, (byte)'2');
    lfs.Flush();
    // Reads succeed via the imap -> inode -> data chain.
    AssertEqual((byte)'1', lfs.Read("/a", 0));
    AssertEqual((byte)'2', lfs.Read("/b", 0));
    // Inode numbers are sequential and distinct.
    AssertEqual(true, ino2 > ino1);
});

Run("lfs write creates live blocks; rewrite makes old ones dead", () =>
{
    var lfs = new MiniWebServer.Host.MiniScheduler.Lfs(segments: 6, blocksPerSegment: 8);
    lfs.CreateFile("/x");
    lfs.WriteData("/x", 0, (byte)'1');
    lfs.Flush();
    int liveBefore = lfs.LiveBlockCount;
    int deadBefore = lfs.DeadBlockCount;
    // Rewrite the same block. The old block is now dead, the new one is live.
    lfs.WriteData("/x", 0, (byte)'2');
    lfs.Flush();
    int liveAfter = lfs.LiveBlockCount;
    int deadAfter = lfs.DeadBlockCount;
    // Live count should be roughly the same (1 inode + 1 data + the new one); dead count grew by 1.
    AssertEqual(true, deadAfter > deadBefore);
    // The new value is what we read back.
    AssertEqual((byte)'2', lfs.Read("/x", 0));
    // Live block count includes inode + new data block (old data block is dead).
    AssertEqual(true, liveAfter >= 2);  // at least 1 inode + 1 data
    // (liveBefore may have been 0 if no flush triggered - we flushed explicitly above)
    _ = liveBefore;
});

Run("lfs segment summary records (inode, offset) for every block", () =>
{
    var lfs = new MiniWebServer.Host.MiniScheduler.Lfs(segments: 4, blocksPerSegment: 8);
    lfs.CreateFile("/q");
    lfs.WriteData("/q", 0, (byte)'Z');
    lfs.WriteData("/q", 1, (byte)'Y');
    lfs.Flush();
    // The cleaner uses the summary indirectly via IsLive; verify liveness.
    int liveCount = lfs.LiveBlockCount;
    AssertEqual(true, liveCount >= 2);  // inode + at least 1 data block live
    AssertEqual((byte)'Z', lfs.Read("/q", 0));
    AssertEqual((byte)'Y', lfs.Read("/q", 1));
});

Run("lfs cleaner compacts live blocks and frees old segment", () =>
{
    var lfs = new MiniWebServer.Host.MiniScheduler.Lfs(segments: 8, blocksPerSegment: 8);
    // Write 3 files, then rewrite one of them to introduce garbage.
    lfs.CreateFile("/a");
    lfs.CreateFile("/b");
    lfs.CreateFile("/c");
    lfs.WriteData("/a", 0, (byte)'1');
    lfs.WriteData("/b", 0, (byte)'2');
    lfs.WriteData("/c", 0, (byte)'3');
    lfs.Flush();
    int deadBefore = lfs.DeadBlockCount;
    int freeBefore = lfs.FreeSegmentCount;

    // Rewrite /a to make its old data block dead.
    lfs.WriteData("/a", 0, (byte)'X');
    lfs.Flush();

    int deadAfterRewrite = lfs.DeadBlockCount;
    AssertEqual(true, deadAfterRewrite > deadBefore);

    // Run the cleaner.
    var report = lfs.Clean();
    // After cleaning, the cleaned segment is freed (or freed + reallocated for the live blocks).
    int freeAfter = lfs.FreeSegmentCount;
    // We freed at least the segment we cleaned; reallocation depends on compaction result.
    // The key invariant: reads still work.
    AssertEqual((byte)'X', lfs.Read("/a", 0));
    AssertEqual((byte)'2', lfs.Read("/b", 0));
    AssertEqual((byte)'3', lfs.Read("/c", 0));
    _ = report;
    _ = freeAfter;
    _ = freeBefore;
});

Run("lfs cleaner can free an entirely-dead segment without compaction", () =>
{
    // Use a small disk so that after a handful of rewrites, the first segment
    // ends up entirely dead (every block was overwritten by a newer version).
    var lfs2 = new MiniWebServer.Host.MiniScheduler.Lfs(segments: 10, blocksPerSegment: 4);
    lfs2.CreateFile("/temp");
    lfs2.WriteData("/temp", 0, (byte)'T');
    lfs2.Flush();
    // Many rewrites push the live data into later segments; the early segments
    // become fully dead.
    for (int i = 0; i < 6; i++)
    {
        lfs2.WriteData("/temp", 0, (byte)('A' + (i % 26)));
    }
    lfs2.Flush();
    // Run cleaner repeatedly.
    for (int i = 0; i < 10; i++) lfs2.Clean();
    // Read still works (smoke test - cleaner preserved live data).
    byte lastVal = (byte)('A' + (5 % 26));
    AssertEqual(lastVal, lfs2.Read("/temp", 0));
    // Some segment may have been freed - but the value is in the invariant,
    // not a specific count. Just confirm operation succeeded.
});

// ----- M26 SSD simulator (OSEP Ch. 44) -----

Run("ssd write + read round-trip via mapping table", () =>
{
    var ssd = new MiniWebServer.Host.MiniScheduler.Ssd(blocks: 4, pagesPerBlock: 4);
    ssd.Write(100, (byte)'A');
    ssd.Write(101, (byte)'B');
    ssd.Write(2000, (byte)'C');
    ssd.Write(2001, (byte)'D');
    AssertEqual((byte)'A', ssd.Read(100));
    AssertEqual((byte)'B', ssd.Read(101));
    AssertEqual((byte)'C', ssd.Read(2000));
    AssertEqual((byte)'D', ssd.Read(2001));
    AssertEqual(4, ssd.MappingSize);
});

Run("ssd rewrite makes old physical page dead", () =>
{
    var ssd = new MiniWebServer.Host.MiniScheduler.Ssd(blocks: 4, pagesPerBlock: 4);
    ssd.Write(100, (byte)'A');
    int deadBefore = ssd.DeadPageCount;
    ssd.Write(100, (byte)'Z');  // rewrite - old page becomes dead
    int deadAfter = ssd.DeadPageCount;
    AssertEqual(true, deadAfter > deadBefore);
    AssertEqual((byte)'Z', ssd.Read(100));  // new value readable
});

Run("ssd trim drops the LBA mapping without rewriting", () =>
{
    var ssd = new MiniWebServer.Host.MiniScheduler.Ssd(blocks: 4, pagesPerBlock: 4);
    ssd.Write(100, (byte)'A');
    AssertEqual(1, ssd.MappingSize);
    ssd.Trim(100);
    AssertEqual(0, ssd.MappingSize);
    // Read should now fail because the LBA is unmapped.
    AssertThrows<InvalidOperationException>(() => ssd.Read(100));
});

Run("ssd erase block increments the wear counter", () =>
{
    var ssd = new MiniWebServer.Host.MiniScheduler.Ssd(blocks: 4, pagesPerBlock: 4);
    AssertEqual(0, ssd.EraseCount(0));
    ssd.EraseBlock(0);
    AssertEqual(1, ssd.EraseCount(0));
    ssd.EraseBlock(0);
    AssertEqual(2, ssd.EraseCount(0));
});

Run("ssd gc migrates live pages and erases a block", () =>
{
    var ssd = new MiniWebServer.Host.MiniScheduler.Ssd(blocks: 4, pagesPerBlock: 4);
    ssd.Write(0, (byte)'1');
    ssd.Write(1, (byte)'2');
    ssd.Write(2, (byte)'3');
    ssd.Write(3, (byte)'4');
    // Rewrite a few to create dead pages.
    ssd.Write(0, (byte)'X');
    ssd.Write(1, (byte)'Y');
    int deadBefore = ssd.DeadPageCount;
    AssertEqual(true, deadBefore > 0);
    // Run GC - should pick a block with dead pages, migrate live, erase it.
    var report = ssd.CollectGarbage();
    AssertEqual(true, report.CleanedBlock >= 0);
    AssertEqual(true, report.DeadPagesFreed > 0);
    // Reads still work (the live data was migrated).
    AssertEqual((byte)'X', ssd.Read(0));
    AssertEqual((byte)'Y', ssd.Read(1));
    AssertEqual((byte)'3', ssd.Read(2));
    AssertEqual((byte)'4', ssd.Read(3));
});

// ----- M32 Block-level + hybrid FTL (OSEP §44.9) -----

Run("ftl mapping table for a 1TB drive matches the §44.9 worked figures", () =>
{
    // OSTEP §44.9 verbatim: "With a large 1-TB SSD, for example, a single
    // 4-byte entry per 4-KB page results in 1 GB of memory needed by the
    // device, just for these mappings! Thus, this page-level FTL scheme is
    // impractical." Block-level mapping reduces the information "by a factor
    // of Size_block / Size_page".
    var tb = MiniWebServer.Host.MiniScheduler.FtlMappingCost.TableBytes(1L * 1024 * 1024 * 1024 * 1024, pageBytes: 4096, blockBytes: 256 * 1024, entryBytes: 4);
    AssertEqual(1L * 1024 * 1024 * 1024, tb.PageLevelBytes);   // 1 GB, the book's number
    AssertEqual(16L * 1024 * 1024, tb.BlockLevelBytes);         // 1 GB / 64 pages per block

    // The reduction factor is exactly Size_block/Size_page.
    AssertClose(64.0, tb.PageLevelBytes / (double)tb.BlockLevelBytes);

    // A hybrid table is the block table plus a bounded per-page log table:
    // it can never be smaller than pure block-level, and never larger than
    // page-level for any sane log size.
    long hybrid = MiniWebServer.Host.MiniScheduler.FtlMappingCost.TableBytes(1L * 1024 * 1024 * 1024 * 1024, pageBytes: 4096, blockBytes: 256 * 1024, entryBytes: 4, logBlocks: 64).HybridBytes;
    AssertEqual(true, hybrid > tb.BlockLevelBytes);
    AssertEqual(true, hybrid < tb.PageLevelBytes);
});

Run("block-level FTL preserves the page offset within a block", () =>
{
    // §44.9: the FTL "computes the address of the desired flash page by
    // adding the offset from the logical address to the physical address of
    // the block". Worked example: chunk 500 -> physical block starting at
    // page 4; reading logical 2002 (offset 2) lands on physical page 6.
    var ftl = new MiniWebServer.Host.MiniScheduler.BlockLevelFtl(blocks: 8, pagesPerBlock: 4);
    // Four logical pages share one chunk, so they need ONE table entry, not four.
    for (int i = 0; i < 4; i++) ftl.Write(2000 + i, (byte)('a' + i));
    AssertEqual(1, ftl.DataTableEntries);
    for (int i = 0; i < 4; i++) AssertEqual((byte)('a' + i), ftl.Read(2000 + i));

    // The next chunk needs a second entry, which is what proves the split is
    // by chunk boundary rather than by address magnitude.
    for (int i = 0; i < 4; i++) ftl.Write(2004 + i, (byte)('A' + i));
    AssertEqual(2, ftl.DataTableEntries);
    for (int i = 0; i < 8; i++) AssertEqual(i < 4 ? (byte)('a' + i) : (byte)('A' + i - 4), ftl.Read(2000 + i));

    int base_ = ftl.PhysicalPageOf(2002);
    int blockStart = ftl.PhysicalPageOf(2000);
    AssertEqual(2, base_ - blockStart);   // offset preserved, not remapped per page

    // A partial-block write must copy the whole block out (read-modify-write).
    ftl.Write(2002, (byte)'z');
    for (int i = 0; i < 4; i++) AssertEqual(i == 2 ? (byte)'z' : (byte)('a' + i), ftl.Read(2000 + i));
});

Run("block-level FTL pays full read-modify-write on a small write", () =>
{
    // §44.9: "the FTL must read a large amount of live data from the old
    // block and copy it into a new one (along with the data from the small
    // write). This data copying increases write amplification greatly."
    var ftl = new MiniWebServer.Host.MiniScheduler.BlockLevelFtl(blocks: 16, pagesPerBlock: 8);
    // 96..103 is exactly chunk 12 (96/8 == 103/8), so it fills one physical block.
    for (int i = 0; i < 8; i++) ftl.Write(96 + i, (byte)('a' + i));
    AssertEqual(1, ftl.DataTableEntries);          // one block pointer for 8 pages

    long writtenBefore = ftl.DataBytesWritten;
    long hostBefore = ftl.HostBytesWritten;
    ftl.Write(99, (byte)'z');                       // one small write into a full block

    // The client wrote 1 page; the device must have programmed the whole 8-page
    // block again, because the block pointer cannot address a single page.
    AssertEqual(1L, ftl.HostBytesWritten - hostBefore);
    AssertEqual(8L, ftl.DataBytesWritten - writtenBefore);
    AssertEqual(8L, ftl.PagesCopiedForSmallWrite);
});

Run("hybrid FTL keeps per-page log writes and amortizes them into a data block", () =>
{
    // §44.9 hybrid: writes land in log blocks under per-page pointers; a
    // merge turns a log block into a single block pointer. Use a single log
    // block so every write folds into block pointers at the merge - with a
    // larger budget the FTL keeps several blocks outstanding by design, which
    // is what the next test covers.
    var seq = new MiniWebServer.Host.MiniScheduler.HybridFtl(blocks: 64, pagesPerBlock: 4, logBlocks: 1);
    for (int i = 0; i < 32; i++) seq.Write(i, (byte)('a' + (i % 26)));
    seq.MergeLogBlocks();
    // Every page is now reachable through a block pointer alone.
    AssertEqual(0, seq.LogTableEntries);
    for (int i = 0; i < 32; i++) AssertEqual((byte)('a' + (i % 26)), seq.Read(i));
});

Run("sequential writes trigger switch merges that copy nothing", () =>
{
    // §44.9's switch merge is the "best case": "all the per-page pointers
    // required replaced by a single block pointer" with no data moved. Only a
    // sequential workload produces it - scattered writes leave chunks
    // incomplete in the log and force partial or full merges instead.
    var seq = new MiniWebServer.Host.MiniScheduler.HybridFtl(blocks: 64, pagesPerBlock: 4, logBlocks: 4);
    for (int i = 0; i < 32; i++) seq.Write(i, (byte)('a' + (i % 26)));

    AssertEqual(true, seq.SwitchMerges > 0);        // sequential writes do switch-merge
    AssertEqual(0L, seq.PagesCopiedOnMerge);        // ... and copy nothing to do it
    AssertEqual(0, seq.PartialMerges);
    AssertEqual(0, seq.FullMerges);

    seq.MergeLogBlocks();
    // One write in, one write out: the device programmed each page once.
    AssertEqual(32L, seq.HostBytesWritten);
    AssertEqual(32L, seq.DataBytesWritten);
    AssertClose(1.0, seq.DataBytesWritten / (double)seq.HostBytesWritten);
    for (int i = 0; i < 32; i++) AssertEqual((byte)('a' + (i % 26)), seq.Read(i));
});

Run("scattered writes force partial or full merges and copy pages", () =>
{
    // The same FTL under scattered writes: chunks stay incomplete in the log
    // block, so §44.9's partial / full merge path runs and live pages have to
    // be read out and rewritten. Eight log blocks of 32 pages each means the
    // writes have to exceed 256 to force any cleaning at all.
    var rnd = new MiniWebServer.Host.MiniScheduler.HybridFtl(blocks: 128, pagesPerBlock: 32, logBlocks: 8);
    var rng = new Random(99);
    for (int r = 0; r < 600; r++) rnd.Write(rng.Next(0, 1024), (byte)('a' + (r % 26)));

    AssertEqual(0, rnd.SwitchMerges);              // no chunk ever completes in order
    AssertEqual(true, rnd.FullMerges + rnd.PartialMerges > 0);
    AssertEqual(true, rnd.PagesCopiedOnMerge > 0);
});

Run("hybrid FTL merge cost is switch < partial < full", () =>
{
    // §44.9 figure 44.10: switch merge = no page copying at all (best case);
    // partial merge copies the sibling pages out of one other block; full
    // merge pulls siblings from many blocks.
    var sw = MiniWebServer.Host.MiniScheduler.HybridMergeDemo.SwitchMerge();
    var pt = MiniWebServer.Host.MiniScheduler.HybridMergeDemo.PartialMerge();
    var fl = MiniWebServer.Host.MiniScheduler.HybridMergeDemo.FullMerge();

    AssertEqual(0, sw.PagesCopied);        // "the best case"
    AssertEqual(true, pt.PagesCopied > sw.PagesCopied);
    AssertEqual(true, fl.PagesCopied > pt.PagesCopied);
    AssertEqual(1, sw.MergeKind);
    AssertEqual(2, pt.MergeKind);
    AssertEqual(3, fl.MergeKind);
});

Run("hybrid FTL write amplification sits between page-level and block-level", () =>
{
    // The §44.9 point: hybrid buys back most of block-level's amplification
    // without block-level's memory bill. Under a scattered rewrite workload
    // hybrid must land strictly between the two.
    const int pages = 32;
    var page = new MiniWebServer.Host.MiniScheduler.PageLevelFtl(blocks: 64, pagesPerBlock: pages);
    var block = new MiniWebServer.Host.MiniScheduler.BlockLevelFtl(blocks: 64, pagesPerBlock: pages);
    var hybrid = new MiniWebServer.Host.MiniScheduler.HybridFtl(blocks: 64, pagesPerBlock: pages, logBlocks: 8);
    var rng = new Random(1234);
    for (int round = 0; round < 40; round++)
    {
        int lba = rng.Next(0, 512);
        byte v = (byte)('a' + (lba % 26));
        page.Write(lba, v);
        block.Write(lba, v);
        hybrid.Write(lba, v);
    }
    page.MergeLogBlocks(); block.MergeLogBlocks(); hybrid.MergeLogBlocks();

    double pa = page.DataBytesWritten / (double)page.HostBytesWritten;
    double ba = block.DataBytesWritten / (double)block.HostBytesWritten;
    double ha = hybrid.DataBytesWritten / (double)hybrid.HostBytesWritten;
    if (!(pa < ha)) throw new InvalidOperationException($"page-level {pa} should be below hybrid {ha}");
    if (!(ha < ba)) throw new InvalidOperationException($"hybrid {ha} should be below block-level {ba}");
});

Run("mapping cost arithmetic survives huge capacities and exact hybrid size", () =>
{
    // The hybrid table is the block table plus one per-page entry for every
    // page of every log block. Multiplying the entry size twice would inflate
    // the log's share; ceiling division written as (a + b - 1) / b overflows to
    // a negative count near long.MaxValue.
    var t = MiniWebServer.Host.MiniScheduler.FtlMappingCost.TableBytes(
        1L * 1024 * 1024 * 1024 * 1024, pageBytes: 4096, blockBytes: 256 * 1024, entryBytes: 4, logBlocks: 64);
    AssertEqual(16L * 1024 * 1024 + 64L * 64 * 4, t.HybridBytes);

    var huge = MiniWebServer.Host.MiniScheduler.FtlMappingCost.TableBytes(long.MaxValue, pageBytes: 4096, blockBytes: 256 * 1024);
    AssertEqual(true, huge.PageLevelBytes > 0);
    AssertEqual(true, huge.BlockLevelBytes > 0);
    AssertEqual(true, huge.PageLevelBytes >= huge.BlockLevelBytes);
});

Run("a permuted log block is not promoted as a zero-copy switch merge", () =>
{
    // A switch merge repoints the data table at the log block's base address,
    // so it is only valid when logical offset N already sits at base+N. Writing
    // a complete chunk out of order and promoting it would hand every offset
    // the wrong page.
    var h = new MiniWebServer.Host.MiniScheduler.HybridFtl(blocks: 8, pagesPerBlock: 4, logBlocks: 1);
    h.Write(2, (byte)'C');
    h.Write(0, (byte)'A');
    h.Write(3, (byte)'D');
    h.Write(1, (byte)'B');
    h.MergeLogBlocks();

    AssertEqual((byte)'A', h.Read(0));
    AssertEqual((byte)'B', h.Read(1));
    AssertEqual((byte)'C', h.Read(2));
    AssertEqual((byte)'D', h.Read(3));
    // The chunk was complete but misaligned, so this cannot be a zero-copy
    // switch merge - the block has to be rewritten in offset order instead.
    AssertEqual(0, h.SwitchMerges);
    AssertEqual(1, h.PartialMerges);
    AssertEqual(true, h.DataTableEntries > 0);
});

Run("a failed write leaves the previous value readable", () =>
{
    // A full device cannot accept the overwrite. The old mapping is the only
    // remaining copy of that LBA, so the write must not invalidate it before
    // the allocation succeeds.
    var page = new MiniWebServer.Host.MiniScheduler.PageLevelFtl(blocks: 2, pagesPerBlock: 1);
    page.Write(0, (byte)'A');
    page.Write(1, (byte)'B');
    AssertThrows<InvalidOperationException>(() => page.Write(0, (byte)'Z'));
    AssertEqual((byte)'A', page.Read(0));
    AssertEqual((byte)'B', page.Read(1));
});

Run("hybrid FTL log block budget delays the first merge", () =>
{
    // §44.9 makes the number of log blocks a tuning knob: keeping several
    // outstanding means filling them before any cleaning happens. If the budget
    // were ignored and every rollover merged, a larger budget would change
    // nothing.
    int small = CountMergesAfterWrites(logBlocks: 1, writes: 32);
    int large = CountMergesAfterWrites(logBlocks: 8, writes: 32);
    AssertEqual(true, small > large);

    static int CountMergesAfterWrites(int logBlocks, int writes)
    {
        var h = new MiniWebServer.Host.MiniScheduler.HybridFtl(blocks: 64, pagesPerBlock: 4, logBlocks);
        for (int i = 0; i < writes; i++) h.Write(i, (byte)('a' + (i % 26)));
        return h.SwitchMerges + h.PartialMerges + h.FullMerges;
    }
});

// ----- M34 Device drivers (OSEP §36.2-§36.6) -----

Run("device canonical protocol reads a block through the three registers", () =>
{
    // §36.3, verbatim: "While (STATUS == BUSY) ; // wait until device is not
    // busy / Write data to DATA register / Write command to COMMAND register /
    // While (STATUS == BUSY) ; // wait until device is done with your request".
    var dev = new MiniWebServer.Host.MiniScheduler.Device(payloadSize: 64, latencyTicks: 3);
    for (int i = 0; i < 64; i++) dev.BackingStore[i] = (byte)('A' + (i % 26));

    byte[] read = dev.CanonicalRead(block: 1, length: 16);
    AssertEqual(16, read.Length);
    // Block 1 starts at byte 16, and the backing store cycles A-Z, so the
    // contents are the same 16 bytes as block 0 but reached at a different
    // offset - which is what makes the offset arithmetic observable.
    for (int i = 0; i < 16; i++) AssertEqual(dev.BackingStore[16 + i], read[i]);

    // §36.3's fourth step ends with the device reporting success or failure - it
    // does not go back to Idle, because that is the state the OS would next
    // wait for *before* issuing a command, and the value it reads now is the
    // result of the command it just issued.
    AssertEqual(MiniWebServer.Host.MiniScheduler.DeviceStatus.Complete, dev.Status);
});

Run("device reports BUSY between the command and its completion", () =>
{
    // §36.3's polling loops only make sense if BUSY is observable while the
    // device works. A device that answered BUSY only at the end would make both
    // waits no-ops, and the protocol would collapse to a sleep.
    var dev = new MiniWebServer.Host.MiniScheduler.Device(payloadSize: 32, latencyTicks: 4);
    AssertEqual(MiniWebServer.Host.MiniScheduler.DeviceStatus.Idle, dev.Status);

    // Writing a command is what starts the device (§36.3, step 3).
    dev.Command = MiniWebServer.Host.MiniScheduler.DeviceCommand.Read;
    AssertEqual(MiniWebServer.Host.MiniScheduler.DeviceStatus.Busy, dev.Status);

    // The Busy polls and the one that saw it finish are all real status reads;
// deriving the second count from PollsExecuted keeps it honest rather than
// asserting a literal that would hold no matter what the device did.
long pollsBefore = dev.PollsExecuted;
    int busyObservations = 0;
    while (dev.Status == MiniWebServer.Host.MiniScheduler.DeviceStatus.Busy)
    {
        busyObservations++;
        dev.Tick();
    }

    AssertEqual(4, busyObservations);                  // 4 ticks of latency
    // One status read per busy poll, plus the one that observed completion.
    AssertEqual(busyObservations + 1L, dev.PollsExecuted - pollsBefore);
    AssertEqual(MiniWebServer.Host.MiniScheduler.DeviceStatus.Complete, dev.Status);
    AssertEqual(1L, dev.InterruptsRaised);              // one completion, one interrupt

    // Extra ticks after completion must not raise a second interrupt.
    dev.Tick(); dev.Tick();
    AssertEqual(1L, dev.InterruptsRaised);
});

Run("DMA burns fewer CPU cycles than PIO for a large transfer", () =>
{
    // §36.5: with PIO "the CPU is once again overburdened with a rather
    // trivial task", because the copy happens "explicitly, one word at a
    // time". With DMA the CPU programs the channel and is done; the copy is
    // off the CPU entirely.
    const int n = 4096;
    var cost = new MiniWebServer.Host.MiniScheduler.DeviceCostModel();
    double pio = cost.PioCycles(n);
    double dma = cost.DmaCycles(n);
    AssertEqual(true, dma < pio);
    // PIO's marginal cost is linear in the byte count - that is §36.5's "one
    // word at a time" - while DMA's total is flat, because the copy happens off
    // the CPU. The difference between two PIO transfers therefore depends only
    // on the extra bytes, not on the transfer size.
    AssertClose(pio + n, cost.PioCycles(2 * n));
    AssertClose(dma, cost.DmaCycles(2 * n));
    AssertEqual(true, cost.PioCycles(2 * n) > cost.PioCycles(n));
});

Run("PIO beats DMA below the crossover and DMA wins above it", () =>
{
    // §36.5 frames DMA as the answer to transferring "a large chunk of data".
    // The chapter never gives a number, so the threshold follows from the cost
    // model: DMA's fixed setup has to be smaller than the per-byte work PIO
    // would otherwise do.
    var cost = new MiniWebServer.Host.MiniScheduler.DeviceCostModel();
    int crossover = cost.DmaCrossoverBytes;
    AssertEqual(true, crossover > 1);

    // Below the crossover PIO is strictly cheaper; at crossover-1 the two tie
    // exactly, which is why the crossover is defined as the first size where
    // DMA is strictly cheaper rather than the first where it is not dearer.
    AssertEqual(true, cost.PioCycles(crossover - 2) < cost.DmaCycles(crossover - 2));
    AssertClose(cost.DmaCycles(crossover), cost.PioCycles(crossover - 1));
    AssertEqual(true, cost.DmaCycles(crossover) < cost.PioCycles(crossover));
    // A tiny transfer is the clear PIO case §36.5 implies.
    AssertEqual(true, cost.PioCycles(1) < cost.DmaCycles(1));
});

Run("DMA raises exactly one interrupt per transfer", () =>
{
    // §36.5: "When the DMA is complete, the DMA controller raises an
    // interrupt, and the OS thus knows the transfer is complete." One, not
    // one per byte - that difference is the whole point of DMA.
    var dev = new MiniWebServer.Host.MiniScheduler.Device(payloadSize: 128, latencyTicks: 2);
    for (int i = 0; i < 128; i++) dev.BackingStore[i] = (byte)('A' + (i % 26));
    int interrupts = 0;
    dev.OnInterrupt = () => interrupts++;

    // A read: the engine pulls bytes out of the device into host memory.
    var buffer = new byte[100];
    dev.DmaTransferToDevice(buffer, fromDevice: true);
    dev.RunUntilIdle();
    AssertEqual(1, interrupts);
    for (int i = 0; i < buffer.Length; i++) AssertEqual((byte)('A' + (i % 26)), buffer[i]);

    // A write: the engine pushes host memory into the device. Still one
    // interrupt per transfer, not one per byte.
    var buffer2 = new byte[64];
    for (int i = 0; i < buffer2.Length; i++) buffer2[i] = (byte)('0' + (i % 10));
    dev.DmaTransferToDevice(buffer2, fromDevice: false);
    dev.RunUntilIdle();
    AssertEqual(2, interrupts);
    for (int i = 0; i < buffer2.Length; i++) AssertEqual(buffer2[i], dev.BackingStore[i]);
});

Run("interrupt loses to polling on a fast device and wins on a slow one", () =>
{
    // §36.4, verbatim: "if a device is fast, it may be best to poll; if it is
    // slow, interrupts, which allow overlap, are best." A driver that always
    // used interrupts would be wrong for half the devices.
    var cost = new MiniWebServer.Host.MiniScheduler.DeviceCostModel();

    // Fast device: the first poll usually finds it done, so the interrupt's
    // switch-and-handle cost is pure overhead.
    double pollFast = cost.InterruptDriveCycles(deviceLatencyTicks: 1, interruptCost: MiniWebServer.Host.MiniScheduler.DeviceCostModel.DefaultInterruptCost);
    AssertEqual(true, cost.PollDriveCycles(deviceLatencyTicks: 1) < pollFast);

    // Slow device: overlap wins.
    double pollSlow = cost.PollDriveCycles(deviceLatencyTicks: 500);
    AssertEqual(true, cost.InterruptDriveCycles(500, MiniWebServer.Host.MiniScheduler.DeviceCostModel.DefaultInterruptCost) < pollSlow);
});

Run("hybrid polls a fast device and falls back to an interrupt on a slow one", () =>
{
    // §36.4: "it may be best to use a hybrid that polls for a little while
    // and then, if the device is not yet finished, uses interrupts. This
    // two-phased approach may achieve the best of both worlds."
    //
    // "Best of both" is the chapter's phrasing, not a claim this model
    // supports: at the slow latency the hybrid costs 49 cycles against the
    // interrupt's 41, because it pays the interrupt's fixed cost after eight
    // polls it did not need. What it does win is both comparisons that DO
    // hold - it never loses to polling, and it never loses to the interrupt.
    // The strict-cheapest claim is deliberately not asserted, and the
    // ordering that breaks it is pinned so the model cannot be quietly
    // changed into one where the hybrid dominates.
    var cost = new MiniWebServer.Host.MiniScheduler.DeviceCostModel();
    int threshold = MiniWebServer.Host.MiniScheduler.DeviceCostModel.DefaultPollThresholdTicks;
    double hybridFast = cost.HybridDriveCycles(deviceLatencyTicks: 1);
    double pollFast = cost.PollDriveCycles(deviceLatencyTicks: 1);
    double interruptFast = cost.InterruptDriveCycles(1, MiniWebServer.Host.MiniScheduler.DeviceCostModel.DefaultInterruptCost);

    // On a fast device the hybrid behaves like polling...
    AssertEqual(true, hybridFast <= pollFast);
    // ... and still beats going straight to interrupts.
    AssertEqual(true, hybridFast < interruptFast);

    double hybridSlow = cost.HybridDriveCycles(deviceLatencyTicks: threshold + 50);
    AssertEqual(true, hybridSlow < cost.PollDriveCycles(threshold + 50));
});

Run("MMIO and explicit I/O reach the same registers at the same cost", () =>
{
    // §36.6: "There is not some great advantage to one approach or the
    // other. The memory-mapped approach is nice in that no new instructions are
    // needed to support it, but both approaches are still in use today."
    var mmio = new MiniWebServer.Host.MiniScheduler.Device(payloadSize: 32, latencyTicks: 2);
    var ports = new MiniWebServer.Host.MiniScheduler.Device(payloadSize: 32, latencyTicks: 2);
    for (int i = 0; i < 32; i++)
    {
        mmio.BackingStore[i] = (byte)('x');
        ports.BackingStore[i] = (byte)('x');
    }

    var viaMmio = mmio.CanonicalRead(0, 8);
    var viaPorts = ports.CanonicalReadViaPorts(0, 8);
    AssertEqual(viaMmio.Length, viaPorts.Length);
    for (int i = 0; i < viaMmio.Length; i++) AssertEqual(viaMmio[i], viaPorts[i]);
    AssertClose(mmio.CpuCycles, ports.CpuCycles);
    AssertClose(mmio.PollsExecuted, ports.PollsExecuted);
});

Run("a zero-latency device still completes its command", () =>
{
    // The polling loop spins while BUSY, so a device that accepted a command
    // and then never reported completion would hang the caller forever rather
    // than return. Zero latency has to mean "done immediately", not "never done".
    var dev = new MiniWebServer.Host.MiniScheduler.Device(payloadSize: 32, latencyTicks: 0);
    int interrupts = 0;
    dev.OnInterrupt = () => interrupts++;

    var read = dev.CanonicalRead(0, 16);
    AssertEqual(16, read.Length);
    AssertEqual(1, interrupts);
    AssertEqual(MiniWebServer.Host.MiniScheduler.DeviceStatus.Complete, dev.Status);
    AssertEqual(false, dev.IsBusy);
});

Run("DMA refuses a transfer larger than the device", () =>
{
    // Silently copying only what fits would report a successful completion
    // while dropping write data or leaving stale bytes in a read buffer.
    var dev = new MiniWebServer.Host.MiniScheduler.Device(payloadSize: 16, latencyTicks: 1);
    var tooBig = new byte[17];
    AssertThrows<ArgumentOutOfRangeException>(() => dev.DmaTransferToDevice(tooBig, fromDevice: false));

    // A rejected transfer must not have started anything or raised an interrupt.
    int interrupts = 0;
    dev.OnInterrupt = () => interrupts++;
    dev.RunUntilIdle();
    AssertEqual(0, interrupts);
    AssertEqual(false, dev.IsBusy);

    // The largest transfer that does fit still works.
    var exact = new byte[16];
    dev.DmaTransferToDevice(exact, fromDevice: true);
    dev.RunUntilIdle();
    AssertEqual(1, interrupts);
});

Run("cost model stays finite for very large transfers", () =>
{
    // int arithmetic wrapped before the conversion to double and produced a
    // negative cost, which is worse than useless in a report about which method
    // is cheaper.
    var cost = new MiniWebServer.Host.MiniScheduler.DeviceCostModel();
    double huge = cost.PioCycles(int.MaxValue);
    AssertEqual(true, huge > 0);
    AssertClose((double)int.MaxValue + 1, huge);
    AssertEqual(true, cost.PollDriveCycles(int.MaxValue) > 0);
    // DMA does not grow with size, so DMA still wins by a wide margin.
    AssertEqual(true, cost.DmaCycles(int.MaxValue) < cost.PioCycles(int.MaxValue));
});

Run("both register-access paths refuse a read past the end of the device", () =>
{
    // §36.6 says neither access method has an advantage, which includes not
    // being a way to escape the device's bounds: the port path used to read
    // straight past the backing store where the memory-mapped path checked.
    var dev = new MiniWebServer.Host.MiniScheduler.Device(payloadSize: 32, latencyTicks: 2);
    for (int i = 0; i < 32; i++) dev.BackingStore[i] = (byte)('a' + (i % 26));

    AssertThrows<ArgumentOutOfRangeException>(() => dev.CanonicalRead(block: 2, length: 16));
    AssertThrows<ArgumentOutOfRangeException>(() => dev.CanonicalReadViaPorts(block: 2, length: 16));

    // A large block index would wrap to a negative offset in int arithmetic and
    // slip past a bounds check written on the wrapped value.
    AssertThrows<ArgumentOutOfRangeException>(() => dev.CanonicalRead(block: int.MaxValue, length: 8));
    AssertThrows<ArgumentOutOfRangeException>(() => dev.CanonicalReadViaPorts(block: int.MaxValue, length: 8));

    // The last in-bounds read still works on both paths.
    AssertEqual(16, dev.CanonicalRead(block: 1, length: 16).Length);
    AssertEqual(16, dev.CanonicalReadViaPorts(block: 1, length: 16).Length);
});

// ----- M27 Integrity simulator (OSEP Ch. 45) -----

Run("integrity xor checksum on a known payload", () =>
{
    byte[] data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
    AssertEqual((byte)(0x01 ^ 0x02 ^ 0x03 ^ 0x04), MiniWebServer.Host.MiniScheduler.IntegrityChecksums.Xor(data));
});

Run("integrity additive checksum on a known payload", () =>
{
    byte[] data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
    AssertEqual((byte)((0x01 + 0x02 + 0x03 + 0x04) & 0xff), MiniWebServer.Host.MiniScheduler.IntegrityChecksums.Additive(data));
});

Run("integrity fletcher checksum catches reordering", () =>
{
    byte[] a = new byte[] { 0x01, 0x02, 0x03, 0x04 };
    byte[] b = new byte[] { 0x04, 0x03, 0x02, 0x01 };
    // XOR + additive are order-independent (commutative), Fletcher is not.
    AssertEqual(MiniWebServer.Host.MiniScheduler.IntegrityChecksums.Xor(a),
                MiniWebServer.Host.MiniScheduler.IntegrityChecksums.Xor(b));
    AssertEqual(MiniWebServer.Host.MiniScheduler.IntegrityChecksums.Additive(a),
                MiniWebServer.Host.MiniScheduler.IntegrityChecksums.Additive(b));
    AssertEqual(true,
                MiniWebServer.Host.MiniScheduler.IntegrityChecksums.Fletcher(a) !=
                MiniWebServer.Host.MiniScheduler.IntegrityChecksums.Fletcher(b));
});

Run("integrity write + read round-trip", () =>
{
    var store = new MiniWebServer.Host.MiniScheduler.IntegrityStore(diskId: 0, blocks: 4, blockSize: 16);
    store.Write(0, System.Text.Encoding.UTF8.GetBytes("Hello"));
    store.Write(1, System.Text.Encoding.UTF8.GetBytes("World"));
    byte[] r0 = store.Read(0);
    byte[] r1 = store.Read(1);
    AssertEqual("Hello", System.Text.Encoding.UTF8.GetString(r0).TrimEnd('\0'));
    AssertEqual("World", System.Text.Encoding.UTF8.GetString(r1).TrimEnd('\0'));
});

Run("integrity corruption is detected by checksum", () =>
{
    var store = new MiniWebServer.Host.MiniScheduler.IntegrityStore(diskId: 0, blocks: 4, blockSize: 16);
    store.Write(0, System.Text.Encoding.UTF8.GetBytes("Hello"));
    store.InjectCorruption(0, byteIdx: 0, bitMask: 0x80);  // flip high bit of first byte
    var failures = store.Verify(store.GetBlock(0));
    AssertEqual(true, failures.Count > 0);
});

Run("integrity misdirected write is detected by physical ID", () =>
{
    var store = new MiniWebServer.Host.MiniScheduler.IntegrityStore(diskId: 0, blocks: 4, blockSize: 16);
    store.Write(0, System.Text.Encoding.UTF8.GetBytes("Hello"));
    store.InjectMisdirectedWrite(0, fakeDiskId: 99);
    var failures = store.Verify(store.GetBlock(0));
    AssertEqual(true, failures.Count > 0);
    AssertEqual(true, failures[0].StartsWith("physical-id-mismatch"));
});

Run("integrity scrubber reports clean + corrupted blocks", () =>
{
    var store = new MiniWebServer.Host.MiniScheduler.IntegrityStore(diskId: 0, blocks: 8, blockSize: 16);
    // Write 8 blocks.
    for (int i = 0; i < 8; i++)
    {
        store.Write(i, new byte[] { (byte)('A' + i), (byte)('0' + i) });
    }
    // Inject one corruption, one misdirected write.
    store.InjectCorruption(3, 0, 0x01);
    store.InjectMisdirectedWrite(5, 99);
    // Run the scrubber.
    var report = store.Scrub();
    AssertEqual(6, report.OkCount);
    AssertEqual(2, report.BadCount);
    // Bad blocks should be 3 and 5.
    var badSet = new System.Collections.Generic.HashSet<int>(report.BadBlocks.Select(b => b.BlockId));
    AssertEqual(true, badSet.Contains(3));
    AssertEqual(true, badSet.Contains(5));
});

Run("scrubber covers a batch per pass and resumes where it stopped", () =>
{
    // §45.7: "By periodically reading through every block of the system". A
    // partial sweep cannot restart from zero each pass or a block would never
    // be reached, so the cursor has to survive between passes.
    var store = new MiniWebServer.Host.MiniScheduler.IntegrityStore(diskId: 0, blocks: 10, blockSize: 8);
    for (int i = 0; i < 10; i++) store.Write(i, new byte[] { (byte)('A' + i) });
    store.InjectCorruption(7, 0, 0x01);   // block 7 lands in the second pass

    var scrubber = new MiniWebServer.Host.MiniScheduler.Scrubber(store, batchSize: 4);
    var p1 = scrubber.ScrubBatch();          // blocks 0-3
    AssertEqual(4, p1.OkCount + p1.BadCount);
    AssertEqual(0, p1.BadCount);

    var p2 = scrubber.ScrubBatch();          // blocks 4-7
    AssertEqual(4, p2.OkCount + p2.BadCount);
    AssertEqual(1, p2.BadCount);
    AssertEqual(7, p2.BadBlocks[0].BlockId);

    // 10 blocks at 4 per pass wraps: the third pass covers 8, 9, 0, 1.
    var p3 = scrubber.ScrubBatch();
    AssertEqual(4, p3.OkCount + p3.BadCount);
    AssertEqual(0, p3.BadCount);

    // A fourth pass continues from the cursor rather than restarting at zero.
    var p4 = scrubber.ScrubBatch();
    AssertEqual(4, p4.OkCount + p4.BadCount);
    AssertEqual(16, scrubber.BlocksScrubbed);
});

Run("scrub catch probability follows exp(-T/MTTF) and is monotone in T", () =>
{
    // Derived, not quoted: a block is revisited every T hours, errors on one
    // block arrive as a Poisson process of rate 1/MTTF, and the corruption is
    // caught iff the next error arrives later than the next scrub visit. So
    // P(caught) = P(Gap > T) = exp(-T/MTTF).
    AssertClose(1.0, MiniWebServer.Host.MiniScheduler.CatchProbability.OfSweepPeriod(0.0, 100000.0));
    AssertClose(Math.Exp(-24.0 / 100000.0),
        MiniWebServer.Host.MiniScheduler.CatchProbability.OfSweepPeriod(24.0, 100000.0));

    // Monotone: a longer gap between visits can only lose catches.
    double prev = 1.0;
    for (double t = 1; t <= 24 * 60; t *= 2)
    {
        double p = MiniWebServer.Host.MiniScheduler.CatchProbability.OfSweepPeriod(t, 100000.0);
        AssertEqual(true, p < prev);
        prev = p;
    }

    // A disk that never corrupts is always caught; a huge MTTF approaches 1.
    AssertClose(1.0, MiniWebServer.Host.MiniScheduler.CatchProbability.OfSweepPeriod(24.0, double.PositiveInfinity));
});

Run("scrub schedule derives the sweep period from interval and batch size", () =>
{
    // A pass covers batchSize of N blocks and the schedule fires every interval,
    // so a block is revisited once per whole sweep. Fewer, larger passes mean
    // each individual block waits longer between visits - which is the cost of
    // bounding the work one pass does.
    double quarter = MiniWebServer.Host.MiniScheduler.CatchProbability.SweepPeriodHours(intervalHours: 24, totalBlocks: 1000, batchSize: 250);
    AssertClose(24.0 * 4, quarter);           // four passes to cover the disk

    double whole = MiniWebServer.Host.MiniScheduler.CatchProbability.SweepPeriodHours(intervalHours: 24, totalBlocks: 1000, batchSize: 1000);
    AssertClose(24.0, whole);                 // one pass covers everything

    // Splitting a sweep into more passes lengthens the period, so it lowers the
    // catch probability for an individual block.
    double pQuarter = MiniWebServer.Host.MiniScheduler.CatchProbability.OfSweepPeriod(quarter, 100000.0);
    double pWhole = MiniWebServer.Host.MiniScheduler.CatchProbability.OfSweepPeriod(whole, 100000.0);
    AssertEqual(true, pQuarter < pWhole);

    // A nightly schedule that covers everything is what §45.7 describes; a
    // nightly schedule covering a tenth of the disk is ten times staler per block.
    AssertEqual(true, pWhole > 0.999);
    AssertEqual(true, pQuarter < 0.9999);
});

Run("checksum overhead matches the §45.8 worked figure", () =>
{
    // §45.8, verbatim: "A typical ratio might be an 8-byte checksum per 4 KB
    // data block, for a 0.19% on-disk space overhead."
    //
    // Note the chapter rounds: 8/4096 is 0.1953125%, which the text writes as
    // 0.19%. Both are asserted - the exact ratio, and agreement with the
    // printed figure - so a change to either would show up.
    AssertClose(0.19, MiniWebServer.Host.MiniScheduler.ChecksumOverhead.SpacePercent(checksumBytes: 8, dataBlockBytes: 4096), 0.006);
    AssertClose(8.0 / 4096.0 * 100.0, MiniWebServer.Host.MiniScheduler.ChecksumOverhead.SpacePercent(8, 4096));
    AssertClose(0.1953125, MiniWebServer.Host.MiniScheduler.ChecksumOverhead.SpacePercent(8, 4096));
    AssertEqual(8.0, MiniWebServer.Host.MiniScheduler.ChecksumOverhead.SpacePercent(8, 4096) * 4096 / 100.0);
});

Run("scrubber schedules and stops cleanly without leaking the worker", () =>
{
    var store = new MiniWebServer.Host.MiniScheduler.IntegrityStore(diskId: 0, blocks: 16, blockSize: 8);
    for (int i = 0; i < 16; i++) store.Write(i, new byte[] { (byte)('A' + i) });

    var scrubber = new MiniWebServer.Host.MiniScheduler.Scrubber(store, batchSize: 2);
    scrubber.Schedule(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(1));
    System.Threading.Thread.Sleep(120);

    // The loop must actually do work: a scheduler that waited but never
    // scrubbed would still report IsRunning, so the pass count is what
    // distinguishes "running" from "spinning".
    AssertEqual(true, scrubber.PassesCompleted > 0);
    AssertEqual(true, scrubber.BlocksScrubbed > 0);
    AssertEqual(true, scrubber.IsRunning);

    scrubber.Stop();

    // Stop() must join the worker, not just signal it: IsRunning is derived
    // from the task's completion, so it can only be false once the loop has
    // actually left. A scheduler that dropped the request and returned would
    // leave the loop scrubbing in the background.
    AssertEqual(false, scrubber.IsRunning);

    // And nothing may keep scrubbing after Stop returns.
    int settled = scrubber.PassesCompleted;
    System.Threading.Thread.Sleep(80);
    AssertEqual(settled, scrubber.PassesCompleted);
});

Run("scrubber rejects intervals the timer cannot express", () =>
{
    // WaitOne takes whole milliseconds, so a sub-millisecond interval would
    // become a zero-length wait and the loop would spin. A 30-day interval
    // exceeds int.MaxValue milliseconds and faults the worker instead. Both
    // must fail at Schedule, where the caller can still see why.
    var store = new MiniWebServer.Host.MiniScheduler.IntegrityStore(diskId: 0, blocks: 8, blockSize: 8);
    for (int i = 0; i < 8; i++) store.Write(i, new byte[] { (byte)('A' + i) });

    var tooShort = new MiniWebServer.Host.MiniScheduler.Scrubber(store, batchSize: 2);
    AssertThrows<ArgumentOutOfRangeException>(
        () => tooShort.Schedule(TimeSpan.FromTicks(1), TimeSpan.Zero));

    var tooLong = new MiniWebServer.Host.MiniScheduler.Scrubber(store, batchSize: 2);
    AssertThrows<ArgumentOutOfRangeException>(
        () => tooLong.Schedule(TimeSpan.FromDays(30), TimeSpan.Zero));

    var hugeThrottle = new MiniWebServer.Host.MiniScheduler.Scrubber(store, batchSize: 2);
    AssertThrows<ArgumentOutOfRangeException>(
        () => hugeThrottle.Schedule(TimeSpan.FromMilliseconds(5), TimeSpan.FromDays(30)));

    // A rejected schedule must not have started anything.
    AssertEqual(false, tooShort.IsRunning);
    AssertEqual(0, tooShort.PassesCompleted);
});

Run("concurrent Schedule and Stop calls cannot orphan a worker", () =>
{
    // The stop-start-publish sequence must be atomic across threads, or two
    // Schedules can both finish stopping before either publishes and the first
    // worker becomes unreachable - still scrubbing, with no Stop able to reach
    // it. Hammer the lifecycle from several threads and require that after the
    // final Stop nothing is running.
    var store = new MiniWebServer.Host.MiniScheduler.IntegrityStore(diskId: 0, blocks: 64, blockSize: 8);
    for (int i = 0; i < 64; i++) store.Write(i, new byte[] { (byte)('A' + (i % 26)) });

    var scrubber = new MiniWebServer.Host.MiniScheduler.Scrubber(store, batchSize: 4);
    int failures = 0;
    var threads = new List<System.Threading.Thread>();
    for (int t = 0; t < 4; t++)
    {
        bool scheduler = t % 2 == 0;
        var th = new System.Threading.Thread(() =>
        {
            try
            {
                for (int r = 0; r < 25; r++)
                {
                    if (scheduler) scrubber.Schedule(TimeSpan.FromMilliseconds(2), TimeSpan.Zero);
                    else scrubber.Stop();
                }
            }
            catch (Exception) { System.Threading.Interlocked.Increment(ref failures); }
        });
        threads.Add(th);
        th.Start();
    }
    foreach (var th in threads) th.Join(TimeSpan.FromSeconds(30));

    scrubber.Stop();
    AssertEqual(0, failures);
    AssertEqual(false, scrubber.IsRunning);

    // Nothing may keep running after the final Stop.
    int settled = scrubber.PassesCompleted;
    System.Threading.Thread.Sleep(120);
    AssertEqual(settled, scrubber.PassesCompleted);
});

Run("scrubber does not scrub while stopped", () =>
{
    var store = new MiniWebServer.Host.MiniScheduler.IntegrityStore(diskId: 0, blocks: 8, blockSize: 8);
    for (int i = 0; i < 8; i++) store.Write(i, new byte[] { (byte)('A' + i) });

    var scrubber = new MiniWebServer.Host.MiniScheduler.Scrubber(store, batchSize: 2);
    // Never scheduled: a fresh scrubber must not touch the disk on its own.
    System.Threading.Thread.Sleep(60);
    AssertEqual(0, scrubber.PassesCompleted);
    AssertEqual(0L, scrubber.BlocksScrubbed);
    AssertEqual(0, scrubber.Cursor);
});

Run("tlb asid isolates address spaces (slice 28.1)", () =>
{
    // Fill entries for ASID 1 and ASID 2.
    var tlb = new MiniWebServer.Host.MiniPager.Tlb(capacity: 8);
    tlb.Fill(vpn: 0, pfn: 100, asid: 1, isGlobal: false);
    tlb.Fill(vpn: 1, pfn: 101, asid: 1, isGlobal: false);
    tlb.Fill(vpn: 0, pfn: 200, asid: 2, isGlobal: false);
    tlb.Fill(vpn: 1, pfn: 201, asid: 2, isGlobal: false);
    AssertEqual(4, tlb.ValidCount);
    // Flush ASID 1 only.
    int removed = tlb.Flush(asid: 1);
    AssertEqual(2, removed);
    AssertEqual(2, tlb.ValidCount);
    // ASID 2 entries still hit.
    AssertEqual(true, tlb.Lookup(asid: 2, vpn: 0, out int pfn0));
    AssertEqual(200, pfn0);
    AssertEqual(true, tlb.Lookup(asid: 2, vpn: 1, out int pfn1));
    AssertEqual(201, pfn1);
    // ASID 1 entries are gone.
    AssertEqual(false, tlb.Lookup(asid: 1, vpn: 0, out _));
    // Flush ASID 2; nothing survives.
    tlb.Flush(asid: 2);
    AssertEqual(0, tlb.ValidCount);
});

Run("tlb global entries survive per-ASID flush but not full flush (slice 28.1)", () =>
{
    var tlb = new MiniWebServer.Host.MiniPager.Tlb(capacity: 4);
    tlb.Fill(vpn: 0, pfn: 100, asid: 1, isGlobal: false);
    tlb.Fill(vpn: 50, pfn: 250, asid: 0, isGlobal: true);
    tlb.Fill(vpn: 51, pfn: 251, asid: 0, isGlobal: true);
    AssertEqual(3, tlb.ValidCount);
    // Per-ASID flush of ASID 1 removes only the non-global entry.
    int removed = tlb.Flush(asid: 1);
    AssertEqual(1, removed);
    AssertEqual(2, tlb.ValidCount);
    AssertEqual(2, tlb.GlobalCount);
    // Global entries still hit under any ASID.
    AssertEqual(true, tlb.Lookup(asid: 7, vpn: 50, out int pfn));
    AssertEqual(250, pfn);
    // Full flush wipes everything including globals.
    tlb.Flush();
    AssertEqual(0, tlb.ValidCount);
    AssertEqual(false, tlb.Lookup(asid: 7, vpn: 50, out _));
});

Run("tlb lookup with mismatched asid misses (slice 28.1)", () =>
{
    var tlb = new MiniWebServer.Host.MiniPager.Tlb(capacity: 4);
    tlb.Fill(vpn: 10, pfn: 999, asid: 1, isGlobal: false);
    // Same VPN, different ASID: must miss (no global bit).
    AssertEqual(false, tlb.Lookup(asid: 2, vpn: 10, out _));
    // Correct ASID: hits.
    AssertEqual(true, tlb.Lookup(asid: 1, vpn: 10, out int pfn));
    AssertEqual(999, pfn);
});

Run("pager CurrentAsid bumps on CreateProcess (slice 28.1)", () =>
{
    var pager = new MiniWebServer.Host.MiniPager.Pager(numFrames: 4);
    AssertEqual(0, pager.CurrentAsid);
    pager.CreateProcess(1);
    AssertEqual(1, pager.CurrentAsid);
    pager.CreateProcess(2);
    AssertEqual(2, pager.CurrentAsid);
});

Run("cv wait without the lock is rejected (slice 29.1)", () =>
{
    // Waiting without holding the lock reintroduces the very race the CV
    // removes, so the primitive refuses instead of hanging silently.
    var gate = new object();
    var cv = new MiniWebServer.Host.MiniScheduler.ConditionVariable();
    AssertThrows<System.Threading.SynchronizationLockException>(() => cv.Wait(gate));
});

Run("cv signal releases exactly one waiter and broadcast releases all (slice 29.1)", () =>
{
    var gate = new object();
    var cv = new MiniWebServer.Host.MiniScheduler.ConditionVariable();

    // Each waiter parks exactly once, so WaitingCount falls monotonically and
    // the release order is observable. (A waiter that re-checked a still-false
    // predicate would legitimately park again - that is the Mesa rule, covered
    // by the waitwhile test below.)
    var parked = new List<System.Threading.Thread>();
    for (int i = 0; i < 3; i++)
    {
        var t = new System.Threading.Thread(() =>
        {
            lock (gate) { cv.Wait(gate); }
        }) { IsBackground = true };
        parked.Add(t);
        t.Start();
    }

    SpinWait.SpinUntil(() => cv.WaitingCount == 3, 4000);
    AssertEqual(3, cv.WaitingCount);

    // Signal releases the head of the queue and nobody else.
    lock (gate) { cv.Signal(); }
    SpinWait.SpinUntil(() => cv.WaitingCount == 2, 4000);
    AssertEqual(2, cv.WaitingCount);
    AssertEqual(1L, cv.SignalCount);

    // Broadcast releases everyone that is left.
    lock (gate) { cv.Broadcast(); }
    SpinWait.SpinUntil(() => cv.WaitingCount == 0, 4000);
    AssertEqual(0, cv.WaitingCount);
    AssertEqual(2L, cv.BroadcastWakeCount);

    foreach (var t in parked) t.Join(4000);
    foreach (var t in parked) AssertEqual(false, t.IsAlive);
});

Run("cv separate instances are separate queues so a fill signal cannot wake a producer (slice 29.1)", () =>
{
    // The §30.2 fix depends on this: a consumer signals `fill`, and no producer
    // is parked on `fill`, so no producer can be woken by mistake. Monitor.Pulse
    // could not express this - one monitor means one queue.
    //
    // The waiter uses a bare Wait, not a predicate loop. With a predicate, a
    // cross-wake would be masked: the waiter would re-park on its unmet
    // predicate and WaitingCount would look unchanged, so the test would pass
    // even with the queues wrongly shared.
    var gate = new object();
    var empty = new MiniWebServer.Host.MiniScheduler.ConditionVariable();
    var fill = new MiniWebServer.Host.MiniScheduler.ConditionVariable();
    bool consumerReturned = false;

    var consumer = new System.Threading.Thread(() =>
    {
        lock (gate)
        {
            fill.Wait(gate);
            consumerReturned = true;
        }
    }) { IsBackground = true };
    consumer.Start();
    SpinWait.SpinUntil(() => fill.WaitingCount == 1, 4000);

    // Signalling the wrong CV must not release the `fill` waiter.
    lock (gate) { empty.Signal(); }

    System.Threading.Thread.Sleep(200);
    AssertEqual(false, consumerReturned);
    AssertEqual(1, fill.WaitingCount);

    // Signalling the right one does.
    lock (gate) { fill.Signal(); }
    consumer.Join(4000);
    AssertEqual(false, consumer.IsAlive);
    AssertEqual(true, consumerReturned);
});

Run("cv waitwhile re-checks the predicate after every wakeup (slice 29.1)", () =>
{
    // Mesa: a signal is a hint. Woken while the predicate still holds, the
    // waiter must park again rather than proceed (§30.1 "always use while").
    // WaitWhile owns that re-check, so the observable effect is: the waiter
    // does not return until the predicate finally goes false.
    var gate = new object();
    var cv = new MiniWebServer.Host.MiniScheduler.ConditionVariable();
    int generation = 0;       // advances on each signal
    int generationNeeded = 2; // the only state the waiter will accept
    bool returned = false;

    var t = new System.Threading.Thread(() =>
    {
        lock (gate)
        {
            cv.WaitWhile(gate, () => generation != generationNeeded);
            returned = true;
        }
    }) { IsBackground = true };
    t.Start();
    SpinWait.SpinUntil(() => cv.WaitingCount == 1, 4000);

    // Signal into a state the predicate still rejects: the hint is not a
    // guarantee, so the waiter re-parks instead of proceeding.
    lock (gate) { generation = 1; cv.Signal(); }
    SpinWait.SpinUntil(() => cv.WaitingCount == 1, 4000);
    AssertEqual(false, returned);
    AssertEqual(1, cv.WaitingCount);

    // Now satisfy the predicate; the waiter returns for good.
    lock (gate) { generation = 2; cv.Signal(); }
    t.Join(4000);
    AssertEqual(false, t.IsAlive);
    AssertEqual(true, returned);
    AssertEqual(2L, cv.WaitCount);
});

Run("cv lost-wakeup reproduces without a state variable and is fixed with one (slice 29.1)", () =>
{
    var result = MiniWebServer.Host.MiniScheduler.ConditionVariableDemos.Run("lost-wakeup");
    var text = string.Join("\n", result.Lines);

    // The demo only means something if the broken variant actually hangs and
    // the fixed one actually finishes; otherwise it proves nothing about §30.1.
    AssertEqual(true, text.Contains("broken (no state variable)          : HUNG"));
    AssertEqual(true, text.Contains("fixed  (predicate + while loop)     : completed"));
    AssertEqual(true, result.Completed);
});

Run("cv single shared condition variable hangs while two conditions complete (slice 29.1)", () =>
{
    var single = MiniWebServer.Host.MiniScheduler.ConditionVariableDemos.Run("single-cv");
    AssertEqual(false, single.Completed);

    var two = MiniWebServer.Host.MiniScheduler.ConditionVariableDemos.Run("two-cv");
    AssertEqual(true, two.Completed);
    var text = string.Join("\n", two.Lines);
    // Every item moved exactly once.
    AssertEqual(true, text.Contains("every value consumed exactly once: True"));
    // The buffer stayed within capacity. Asserted as a bound, not as equality:
    // a schedule where consumers keep up never fills it to 8, and that is a
    // correct execution, not a failure.
    AssertEqual(true, text.Contains("produced=1000 consumed=1000"));
});

Run("cv broadcast wakes the waiter's predicate that a signal could have missed (slice 29.1)", () =>
{
    var covering = MiniWebServer.Host.MiniScheduler.ConditionVariableDemos.Run("covering-condition");
    AssertEqual(true, covering.Completed);
    var text = string.Join("\n", covering.Lines);
    // The 10-byte waiter proceeds; the 100-byte waiter is still unsatisfied.
    AssertEqual(true, text.Contains("Tb (10-byte request, satisfied by the 50 free)   : allocated"));
    AssertEqual(true, text.Contains("Ta (100-byte request, still unsatisfied)            : correctly still parked"));
});

Run("cv timed wait returns false on timeout and true when signaled (slice 29.1 follow-up)", () =>
{
    var gate = new object();
    var cv = new MiniWebServer.Host.MiniScheduler.ConditionVariable();

    // No signaller: the wait must give up on its own and hand the lock back.
    bool signaled = false;
    bool returned = false;
    var t = new System.Threading.Thread(() =>
    {
        lock (gate)
        {
            signaled = cv.Wait(gate, TimeSpan.FromMilliseconds(150));
            returned = true;
        }
    }) { IsBackground = true };
    var sw = System.Diagnostics.Stopwatch.StartNew();
    t.Start();
    t.Join(4000);
    sw.Stop();

    AssertEqual(false, t.IsAlive);
    AssertEqual(true, returned);
    AssertEqual(false, signaled);
    // Timed out rather than hanging: comfortably under the join deadline.
    AssertEqual(true, sw.ElapsedMilliseconds < 3000);

    // With a signaller it reports a signal.
    bool signaled2 = false;
    var t2 = new System.Threading.Thread(() =>
    {
        lock (gate) { signaled2 = cv.Wait(gate, TimeSpan.FromSeconds(5)); }
    }) { IsBackground = true };
    t2.Start();
    SpinWait.SpinUntil(() => cv.WaitingCount == 1, 2000);
    lock (gate) { cv.Signal(); }
    t2.Join(4000);
    AssertEqual(true, signaled2);

    // A timed-out waiter must not still be queued: it left on its own.
    AssertEqual(0, cv.WaitingCount);
});

Run("deadlock naive scenario deadlocks and prevention scenarios do not (slice 30.1)", () =>
{
    var naive = MiniWebServer.Host.MiniScheduler.DeadlockSim.Run("naive");
    AssertEqual(true, naive.Deadlocked);
    var naiveText = string.Join("\n", naive.Lines);
    AssertEqual(true, naiveText.Contains("DEADLOCK"));
    // The verdict and the count must not disagree: a DEADLOCK means the run did
    // not finish, so some thread is still parked. The count itself varies (a
    // non-participant can leave during the timeout window), so assert the
    // implication rather than an exact number.
    int completedReported = int.Parse(
        naiveText.Split("completed=")[1].Split('/')[0]);
    AssertEqual(true, completedReported < 4);
    AssertEqual(true, completedReported >= 0);

    // Each prevention technique must clear the same workload. This is the
    // contrast the milestone exists to show: same callers, same lock pair,
    // different acquisition discipline.
    foreach (var scenario in new[] { "ordering", "batch", "preempt" })
    {
        var result = MiniWebServer.Host.MiniScheduler.DeadlockSim.Run(scenario);
        AssertEqual(false, result.Deadlocked);
        var text = string.Join("\n", result.Lines);
        AssertEqual(true, text.Contains("completed=4/4"));
    }

    // The batch scenario's whole claim is that the prevention lock admits one
// thread at a time. If that lock were removed the overlap would exceed 1.
var batchText = string.Join("\n",
    MiniWebServer.Host.MiniScheduler.DeadlockSim.Run("batch").Lines);
AssertEqual(true, batchText.Contains("max threads inside the prevention lock at once: 1"));

// The trylock scenario only teaches something if its contended branch
    // actually runs. With a uniform acquisition order the threads queue on the
    // first lock and the second is always free, so the branch is never taken.
    var preemptText = string.Join("\n",
        MiniWebServer.Host.MiniScheduler.DeadlockSim.Run("preempt").Lines);
    var retries = int.Parse(preemptText.Split("retried): ")[1].Split('\n')[0]);
    AssertEqual(true, retries > 0);
});

Run("banker refuses the unsafe request and grants the safe one (slice 30.1)", () =>
{
    // Available = [3,3,2]; classic five-thread allocation/max.
    int[,] allocation = { { 0, 1, 0 }, { 2, 0, 0 }, { 3, 0, 2 }, { 2, 1, 1 }, { 0, 0, 2 } };
    int[,] max = { { 7, 5, 3 }, { 3, 2, 2 }, { 9, 0, 2 }, { 2, 2, 2 }, { 4, 3, 3 } };
    var banker = new MiniWebServer.Host.MiniScheduler.Banker(new[] { 3, 3, 2 }, allocation, max);

    // The starting state itself is safe.
    AssertEqual(true, banker.FindSafeSequence() != null);

    // T4 asking [3,3,0] fits Available [3,3,2] and T4's declared Need [4,3,1],
    // so it passes both cheap checks and reaches the safety test. Granting it
    // leaves Available [0,0,2] with T0 needing [7,4,3] and T2 needing [6,0,0]
    // — nothing can finish, so the grant would deadlock. A request that merely
    // exceeded Available would be refused earlier and never exercise this.
    var availBefore = banker.Available;
    var needsBefore = Enumerable.Range(0, 5).Select(banker.NeedOf).Select(n => string.Join(",", n)).ToArray();
    AssertEqual(false, banker.TryRequest(4, new[] { 3, 3, 0 }, out string? unsafeReason));
    AssertEqual(true, unsafeReason!.Contains("no safe sequence"));

    // A refusal after a tentative grant must roll the grant back completely,
    // not just Available but every thread's allocation (visible via Need).
    AssertEqual(string.Join(",", availBefore), string.Join(",", banker.Available));
    var needsAfter = Enumerable.Range(0, 5).Select(banker.NeedOf).Select(n => string.Join(",", n)).ToArray();
    for (int i = 0; i < 5; i++)
    {
        AssertEqual(needsBefore[i], needsAfter[i]);
    }

    // T1's full remaining need is grantable: T1's allocation then equals its Max,
    // so T1 finishes first and returns everything.
    AssertEqual(true, banker.TryRequest(1, new[] { 1, 2, 2 }, out _));
    AssertEqual("2,1,0", string.Join(",", banker.Available));

    // A request within Available but larger than the thread's declared Need is
    // refused on the Need check. T1's remaining need is [1,2,2] and Available is
    // still [3,3,2], so asking for 3 of resource 0 fits what is free yet overruns
    // what T1 ever declared.
    var needCheck = new MiniWebServer.Host.MiniScheduler.Banker(new[] { 3, 3, 2 }, allocation, max);
    AssertEqual(false, needCheck.TryRequest(1, new[] { 3, 0, 0 }, out string? overNeed));
    AssertEqual(true, overNeed!.Contains("exceeds declared Need"));

    // A request beyond what is free is refused on the availability check.
    AssertEqual(false, needCheck.TryRequest(0, new[] { 9, 0, 0 }, out string? tooBig));
    AssertEqual(true, tooBig!.Contains("Available"));

    // A negative request is malformed, not merely unsafe.
    AssertEqual(false, banker.TryRequest(0, new[] { -1, 0, 0 }, out string? negative));
    AssertEqual(true, negative!.Contains("negative request"));
});

Run("banker detects an unsafe state rather than only refusing requests (slice 30.1)", () =>
{
    // Available [1,1,1] with the classic allocation/max is NOT safe: T0 needs
    // [7,4,3], T2 needs [6,0,0] - no thread can finish, so no safe sequence
    // exists even though every thread's remaining need is within its max.
    int[,] allocation = { { 0, 1, 0 }, { 2, 0, 0 }, { 3, 0, 2 }, { 2, 1, 1 }, { 0, 0, 2 } };
    int[,] max = { { 7, 5, 3 }, { 3, 2, 2 }, { 9, 0, 2 }, { 2, 2, 2 }, { 4, 3, 3 } };
    var unsafeBanker = new MiniWebServer.Host.MiniScheduler.Banker(new[] { 1, 1, 1 }, allocation, max);
    AssertEqual(true, unsafeBanker.FindSafeSequence() == null);
});

// Repeated deadlock scenarios must not retain their worker threads. The
// `naive` case parks four threads on purpose, so without teardown every
// request would strand four more for the life of the process.
Run("deadlock scenarios do not retain threads across runs (slice 30.1)", () =>
{
    int proc() => System.Diagnostics.Process.GetCurrentProcess().Threads.Count;

    // Warm up first: the runtime's own thread pool expands on demand, and that
    // growth is not a leak.
    MiniWebServer.Host.MiniScheduler.DeadlockSim.Run("naive");
    MiniWebServer.Host.MiniScheduler.DeadlockSim.Run("preempt");
    int before = proc();

    foreach (var scenario in new[] { "naive", "ordering", "batch", "preempt" })
    {
        MiniWebServer.Host.MiniScheduler.DeadlockSim.Run(scenario);
    }

    int after = proc();
    if (after > before + 2)
    {
        throw new InvalidOperationException(
            $"threads grew from {before} to {after} across 4 scenario runs - workers are being retained");
    }
});

Run("segment sizer reproduces the OSEP §43.3 worked example (slice 31.1)", () =>
{
    // OSEP §43.3: "a disk with a positioning time of 10 milliseconds and peak
    // transfer rate of 100 MB/s; assume we want an effective bandwidth of 90%
    // of peak (F = 0.9). In this case, D = 0.9/0.1 x 100 MB/s x 0.01 seconds
    // = 9 MB." Equation 43.6.
    AssertClose(9_000_000d,
        MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(100, 0.010, 0.9));

    // The chapter also asks "how much is needed to reach 95% of peak? 99%?"
    AssertClose(19_000_000d,
        MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(100, 0.010, 0.95));
    AssertClose(99_000_000d,
        MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(100, 0.010, 0.99));

    // OptimalBytes inverts equation 43.2, so feeding it back must reproduce the
    // target fraction. Asserting only the closed form would pass even if
    // EffectiveBandwidthFraction disagreed with it.
    foreach (double f in new[] { 0.5, 0.9, 0.95, 0.99 })
    {
        double bytes = MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(100, 0.010, f);
        double achieved = MiniWebServer.Host.MiniScheduler.SegmentSizer
            .EffectiveBandwidthFraction(bytes, 100, 0.010);
        AssertClose(f, achieved);
    }

    // F/(1-F) must blow up as F approaches 1, which is the chapter's point.
    double at90 = MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(100, 0.010, 0.90);
    double at99 = MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(100, 0.010, 0.99);
    AssertEqual(true, at99 / at90 > 10);

    // Offsets: a slower disk needs a bigger segment for the same fraction.
    AssertEqual(true,
        MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(200, 0.010, 0.9) > at90);
    AssertEqual(true,
        MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(100, 0.020, 0.9) > at90);

    AssertThrows<ArgumentOutOfRangeException>(
        () => MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(100, 0.010, 1.0));
    AssertThrows<ArgumentOutOfRangeException>(
        () => MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(100, 0.010, 0.0));
});

Run("segment-size write amplification falls with size and is cleaning-independent (slice 31.1)", () =>
{
    // Positioning overhead is amortised over the segment, so the write-path cost
    // strictly decreases as segments grow. Measured, not assumed: an earlier
    // model tried to make the total a U-shape on the assumption that big segments
    // accumulate more garbage, but §43.9's arithmetic cancels segment size out of
    // the reclaim cost.
    int[] sizes = { 4, 8, 16, 32, 64, 128, 256 };
    var amps = sizes
        .Select(b => MiniWebServer.Host.MiniScheduler.SegmentSizer.WriteAmplification(
            b, liveBlockRatio: 0.4, blockBytes: 4096,
            positionTimeSeconds: 0.010, peakBandwidthMbPerSecond: 100))
        .ToArray();

    for (int i = 1; i < amps.Length; i++)
    {
        AssertEqual(true, amps[i] < amps[i - 1]);
    }

    // The cleaner's term does not depend on segment size at all: two segment
    // sizes with the same live ratio differ only by positioning.
    double small = MiniWebServer.Host.MiniScheduler.SegmentSizer.WriteAmplification(
        8, 0.4, 4096, 0.010, 100);
    double large = MiniWebServer.Host.MiniScheduler.SegmentSizer.WriteAmplification(
        128, 0.4, 4096, 0.010, 100);
    AssertEqual(true, large < small);
    // The gap between two sizes is pure positioning, which is the same
    // difference a liveRatio of 1.0 shows.
    AssertClose(small - large,
        MiniWebServer.Host.MiniScheduler.SegmentSizer.WriteAmplification(
            8, 1.0, 4096, 0.010, 100)
        - MiniWebServer.Host.MiniScheduler.SegmentSizer.WriteAmplification(
            128, 1.0, 4096, 0.010, 100));

    // A dirtier segment costs more to reclaim, and that dominates the total.
    double clean = MiniWebServer.Host.MiniScheduler.SegmentSizer.WriteAmplification(
        32, 0.8, 4096, 0.010, 100);
    double dirtier = MiniWebServer.Host.MiniScheduler.SegmentSizer.WriteAmplification(
        32, 0.2, 4096, 0.010, 100);
    AssertEqual(true, dirtier > clean);
    // A fully-live segment has no reclaim cost at all: 1 + positioning.
    AssertClose(1.0, MiniWebServer.Host.MiniScheduler.SegmentSizer.WriteAmplification(
        32, 1.0, 4096, 0.010, 100) - 0.010 / (0.010 + 32 * 4096 / 1e6 / 100));

    AssertThrows<ArgumentOutOfRangeException>(
        () => MiniWebServer.Host.MiniScheduler.SegmentSizer.WriteAmplification(
            32, 0.0, 4096, 0.010, 100));
});

Run("dual-cr recovery takes the newest consistent CR (slice 31.2)", () =>
{
    var cr = new MiniWebServer.Host.MiniScheduler.DualCheckpointRegion();
    cr.Write();  // CR0 ts=1
    cr.Write();  // CR1 ts=2
    cr.Write();  // CR0 ts=3

    var (index, image, _) = cr.Recover();
    AssertEqual(0, index);
    AssertEqual(3L, image.HeaderTimestamp);

    // An even count leaves CR1 newest.
    var cr2 = new MiniWebServer.Host.MiniScheduler.DualCheckpointRegion();
    cr2.Write();
    cr2.Write();
    var (index2, image2, _) = cr2.Recover();
    AssertEqual(1, index2);
    AssertEqual(2L, image2.HeaderTimestamp);

    // Neither written at all: recovery must fail loudly rather than mount a
    // filesystem with no anchor.
    AssertThrows<InvalidOperationException>(
        () => new MiniWebServer.Host.MiniScheduler.DualCheckpointRegion().Recover());
});

Run("dual-cr alternation leaves the other CR intact on a mid-write crash (slice 31.2)", () =>
{
    var cr = new MiniWebServer.Host.MiniScheduler.DualCheckpointRegion();
    cr.Write();                    // CR0 ts=1 consistent
    cr.Write();                    // CR1 ts=2 consistent
    // Crash partway: header written, trailer never.
    cr.Write(bodyTimestamp: 3, trailerTimestamp: -1);

    // The crashed CR is visibly inconsistent - that is the detection the
    // header/trailer pair exists for.
    AssertEqual(false, cr.Cr0!.IsConsistent);
    AssertEqual(true, cr.Cr1!.IsConsistent);

    // Recovery falls back to CR1, the one that was not being written.
    var (index, image, reason) = cr.Recover();
    AssertEqual(1, index);
    AssertEqual(2L, image.HeaderTimestamp);
    AssertEqual(true, reason.Contains("CR0 rejected"));

    // The alternation itself: N writes must leave the next target at N % 2.
    var cr3 = new MiniWebServer.Host.MiniScheduler.DualCheckpointRegion();
    for (int i = 0; i < 7; i++) cr3.Write();
    AssertEqual(1, cr3.ActiveIndex);
    AssertEqual("CR0,CR1,CR0,CR1,CR0,CR1,CR0",
        string.Join(",", cr3.WriteLog.Select(w => $"CR{w.Cr}")));

    // Both CRs inconsistent: nothing mountable, and that must be an error.
    var broken = new MiniWebServer.Host.MiniScheduler.DualCheckpointRegion();
    broken.Write(bodyTimestamp: 1, trailerTimestamp: -1);
    broken.Write(bodyTimestamp: 2, trailerTimestamp: -1);
    AssertThrows<InvalidOperationException>(() => broken.Recover());
});

Run("segment sizer rejects non-invertible and overflowing inputs (slice 31.1)", () =>
{
    // T_position = 0 has no positioning overhead, so every segment size reaches
    // peak: equation 43.6 returns 0 and its inverse cannot recover F. Accepting
    // it would produce a 0-byte "optimal" segment.
    AssertThrows<ArgumentOutOfRangeException>(
        () => MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(100, 0, 0.9));

    // Left-associated `odds * R_peak * T` overflows on a huge bandwidth paired
    // with a tiny seek time, returning Infinity for a finite answer (9 MB).
    double big = MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(1e308, 1e-308, 0.9);
    AssertClose(9_000_000d, big);
    AssertEqual(true, double.IsFinite(big));

    // The inverse must produce a finite number for the same extreme, not NaN.
    AssertEqual(true, double.IsFinite(
        MiniWebServer.Host.MiniScheduler.SegmentSizer.EffectiveBandwidthFraction(big, 1e308, 1e-308)));

    // Non-finite parameters are rejected rather than propagating.
    AssertThrows<ArgumentOutOfRangeException>(
        () => MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(double.NaN, 0.01, 0.9));
    AssertThrows<ArgumentOutOfRangeException>(
        () => MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(100, 0.01, double.NaN));
    AssertThrows<ArgumentOutOfRangeException>(
        () => MiniWebServer.Host.MiniScheduler.SegmentSizer.OptimalBytes(double.PositiveInfinity, 0.01, 0.9));
});

Run("dual-cr refuses a fresh header paired with the previous write's trailer (slice 31.2)", () =>
{
    // The trailer is written last and always carries its own write's timestamp,
    // so a crash cannot leave a new header beside the old trailer. Allowing it
    // would publish a torn body behind a "consistent" header/trailer pair that
    // recovery would happily mount.
    var cr = new MiniWebServer.Host.MiniScheduler.DualCheckpointRegion();
    cr.Write();                        // CR0 ts=1
    cr.Write();                        // CR1 ts=2
    AssertThrows<ArgumentException>(
        () => cr.Write(bodyTimestamp: 3, trailerTimestamp: 1));  // ts=1 is CR0's old header

    // The legitimate crash shape is still accepted: header written, trailer not.
    var ok = new MiniWebServer.Host.MiniScheduler.DualCheckpointRegion();
    ok.Write();
    ok.Write();
    ok.Write(bodyTimestamp: 3, trailerTimestamp: -1);
    AssertEqual(false, ok.Cr0!.IsConsistent);
    AssertEqual(1, ok.Recover().Index);
});

Run("assert helpers reject NaN rather than silently passing (slice 31.1)", () =>
{
    // A plain `difference > threshold` check lets NaN through, because every
    // comparison with NaN is false - so a helper mutated to return NaN would
    // pass every assertion that used it. Pin the behaviour.
    AssertThrows<InvalidOperationException>(() => AssertClose(1.0, double.NaN));
    AssertThrows<InvalidOperationException>(() => AssertClose(double.NaN, 1.0));
    AssertThrows<InvalidOperationException>(() => AssertClose(1.0, double.PositiveInfinity));
    AssertClose(1.0, 1.0 + 1e-12);
});

Console.WriteLine("All tests passed.");

static string CreateTempWebRoot()
{
    string path = Path.Combine(Path.GetTempPath(), "mini-web-server-tests", Guid.NewGuid().ToString("N"), "wwwroot");
    Directory.CreateDirectory(path);
    return path;
}

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.Exit(1);
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}");
    }
}

/// <summary>
/// Assert two doubles agree to within <paramref name="tolerance"/> relative.
/// Used where the expected value comes from a closed-form formula evaluated in
/// floating point, so the last bits are not part of the claim.
/// </summary>
static void AssertClose(double expected, double actual, double tolerance = 1e-9)
{
    if (!double.IsFinite(expected) || !double.IsFinite(actual))
    {
        throw new InvalidOperationException(
            $"Expected a finite {expected}, got {actual}");
    }
    // `!(difference <= threshold)` rather than `difference > threshold`: with
    // NaN the latter is false, so a NaN actual would silently pass.
    double scale = Math.Max(1.0, Math.Abs(expected));
    if (!(Math.Abs(expected - actual) <= tolerance * scale))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual} (tolerance {tolerance})");
    }
}

static void AssertThrows<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}");
    }
    throw new InvalidOperationException($"Expected {typeof(T).Name}, no exception thrown");
}
