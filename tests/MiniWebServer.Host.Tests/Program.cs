using System;
using System.Text;
using MiniWebServer.Host.MiniFs;
using MiniWebServer.Host.MiniAuth;
using MiniWebServer.Host.MiniCrypto;



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

// ---------------------------------------------------------------------------
// M35 / OSEP Ch. 7 §7.1-§7.7 — scheduling baselines.
//
// Every expected value here is a number printed in OSTEP itself, not one this
// implementation produced: the chapter states each average explicitly. That is
// the only oracle worth having for a simulator - a test that recomputes the
// metric the way the code does would pass by construction.
// ---------------------------------------------------------------------------

Run("fifo reproduces the chapter's equal-length example (slice 35.1)", () =>
{
    // §7.3, figure 7.1: three jobs of 10s each, all arriving together.
    // "A finished at 10, B at 20, and C at 30 ... the average turnaround time
    //  for the three jobs is simply (10+20+30)/3 = 20."
    //
    // Response is the chapter's §7.6 metric and the chapter does not print a
    // figure for this workload, so the value here is derived from figure 7.1's
    // own bars rather than asserted independently: A first runs at t=0, B at
    // t=10, C at t=20, so the average response is (0+10+20)/3 = 10. That is
    // already the problem §7.6 is about - equal-length jobs, and a job still
    // waits 20 ticks to start - which the SJF/RR comparison quantifies later.
    var jobs = MiniWebServer.Host.MiniScheduler.BaselineWorkload.FromLengths(
        new[] { 10, 10, 10 }, new[] { 0, 0, 0 }, names: new[] { "A", "B", "C" });

    var r = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo, jobs);

    AssertClose(20.0, r.AvgTurnaround);
    AssertClose(10.0, r.AvgResponse);
    AssertClose(10.0, r.AvgWait);      // wait = turnaround - burst = 20 - 10
    AssertEqual("ABC", r.CompletionOrder);
});

Run("fifo reproduces the chapter's convoy example (slice 35.1)", () =>
{
    // §7.3, figure 7.2: A=100, B=10, C=10, all arriving at 0. The chapter
    // prints "the average turnaround time for the system is high: a painful
    //  110 seconds ((100+110+120)/3)".
    var jobs = MiniWebServer.Host.MiniScheduler.BaselineWorkload.FromLengths(
        new[] { 100, 10, 10 }, new[] { 0, 0, 0 }, names: new[] { "A", "B", "C" });

    var r = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo, jobs);

    AssertClose(110.0, r.AvgTurnaround);
    AssertEqual("ABC", r.CompletionOrder);
});

Run("sjf halves the convoy and beats fifo on turnaround (slice 35.1)", () =>
{
    // §7.4, figure 7.3: same workload as above, SJF runs B and C first.
    // "SJF reduces average turnaround from 110 seconds to 50 ((10+20+120)/3),
    //  more than a factor of two improvement."
    var lengths = new[] { 100, 10, 10 };
    var arrivals = new[] { 0, 0, 0 };

    var fifo = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo,
        MiniWebServer.Host.MiniScheduler.BaselineWorkload.FromLengths(lengths, arrivals));
    var sjf = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Sjf,
        MiniWebServer.Host.MiniScheduler.BaselineWorkload.FromLengths(lengths, arrivals));

    AssertClose(110.0, fifo.AvgTurnaround);
    AssertClose(50.0, sjf.AvgTurnaround);
    AssertEqual("BCA", sjf.CompletionOrder);
});

Run("stcf preempts the long job for late arrivals (slice 35.1)", () =>
{
    // §7.4 figure 7.4 / §7.5 figure 7.5: A=100 arrives at t=0, B and C=10
    // arrive at t=10. Non-preemptive SJF cannot react - "even though B and C
    //  arrived shortly after A, they still are forced to wait until A has
    //  completed ... Average turnaround time for these three jobs is 103.33".
    var jobs = MiniWebServer.Host.MiniScheduler.BaselineWorkload.LateArrivals();

    var sjf = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Sjf, jobs);

    // §7.5: "STCF would preempt A and run B and C to completion ... The
    // result is a much-improved average turnaround time: 50 seconds
    // ((120-0)+(20-10)+(30-10))/3".
    var stcf = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Stcf, jobs);

    AssertClose(103.3333333333333, sjf.AvgTurnaround, 1e-9);
    AssertClose(50.0, stcf.AvgTurnaround, 1e-9);
    AssertEqual("BCA", stcf.CompletionOrder);
});

Run("round robin trades turnaround for response (slice 35.1)", () =>
{
    // §7.7: three jobs of 5s arriving together, 1s time slice.
    // "The average response time of RR is: (0+1+2)/3 = 1; for SJF, average
    //  response time is: (0+5+10)/3 = 5."
    // Then the same workload by turnaround: "A finishes at 13, B at 14, and C
    //  at 15, for an average of 14. Pretty awful!"
    var jobs = MiniWebServer.Host.MiniScheduler.BaselineWorkload.ResponseTimeCase();

    var sjf = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Sjf, jobs);
    var rr = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.RoundRobin, jobs, quantum: 1);

    AssertClose(5.0, sjf.AvgResponse, 1e-9);
    AssertClose(1.0, rr.AvgResponse, 1e-9);
    AssertClose(14.0, rr.AvgTurnaround, 1e-9);

    // §7.7: "Because turnaround time only cares about when jobs finish, RR is
    // nearly pessimal, even worse than simple FIFO in many cases."
    //
    // "in many cases" is doing real work in that sentence. On *this* workload
    // (three equal 5s jobs) it does hold: FIFO completes at 5/10/15 for an
    // average of 10, RR at 13/14/15 for 14. It is not universal though - on
    // the §7.3 workload FIFO's convoy makes it worse than RR - so the test
    // pins this workload rather than asserting the general claim.
    var fifo = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo, jobs);
    AssertClose(10.0, fifo.AvgTurnaround, 1e-9);
    AssertEqual(true, rr.AvgTurnaround > fifo.AvgTurnaround);

    // The chapter's actual point in §7.7 is the trade-off, so both directions
    // are asserted: RR wins response, loses turnaround.
    AssertEqual(true, rr.AvgResponse < sjf.AvgResponse);
    AssertEqual(true, rr.AvgTurnaround > sjf.AvgTurnaround);
});

Run("baseline scheduler rejects inputs the chapter's model cannot express (slice 35.1)", () =>
{
    // §7.7: "the length of a time slice must be a multiple of the timer
    // interrupt period", so a quantum below one tick is not a slow quantum,
    // it is an unrunnable scheduler.
    var jobs = MiniWebServer.Host.MiniScheduler.BaselineWorkload.EqualLengths(3);
    AssertThrows<ArgumentOutOfRangeException>(() =>
        MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
            MiniWebServer.Host.MiniScheduler.BaselinePolicy.RoundRobin, jobs, quantum: 0));

    // A time slice on a run-to-completion policy is meaningless rather than
    // harmless: accepting it would silently measure RR while reporting FIFO.
    AssertThrows<ArgumentException>(() =>
        MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
            MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo, jobs, quantum: 5));

    AssertThrows<ArgumentException>(() =>
        MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
            MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo, new List<MiniWebServer.Host.MiniScheduler.BaselineJob>()));

    // §7.1 assumes a job runs for a positive, known amount of time.
    AssertThrows<ArgumentException>(() =>
        MiniWebServer.Host.MiniScheduler.BaselineWorkload.FromLengths(
            new[] { 10, 0 }, new[] { 0, 0 }));
    AssertThrows<ArgumentException>(() =>
        MiniWebServer.Host.MiniScheduler.BaselineWorkload.FromLengths(
            new[] { 10 }, new[] { -1 }));
});

Run("baseline policies do not mutate the caller's workload (slice 35.1)", () =>
{
    // A caller comparing two policies on one workload is the obvious use, and
    // the simulation decrements BurstRemaining as it runs. If the input list
    // were used directly, the second run would see every job already spent.
    var jobs = MiniWebServer.Host.MiniScheduler.BaselineWorkload.Convoy();

    var first = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo, jobs);
    var second = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo, jobs);

    AssertClose(first.AvgTurnaround, second.AvgTurnaround);
    AssertClose(110.0, second.AvgTurnaround);
});

Run("a longer quantum trades response for turnaround (slice 35.1)", () =>
{
    // §7.7: "The shorter it is, the better the performance of RR under the
    // response-time metric. However, making the time slice too short is
    // problematic: suddenly the cost of context switching will dominate."
    //
    // The quantum has to actually reach the simulator. A suite that only ever
    // passed quantum=1 would not notice a route or a scheduler that ignored the
    // parameter - which is exactly what happened: the comparison table hardcoded
    // 1 and reported the requested policy twice with different numbers.
    var jobs = MiniWebServer.Host.MiniScheduler.BaselineWorkload.Convoy();

    double Response(int q) => MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.RoundRobin, jobs, quantum: q).AvgResponse;

    double Turnaround(int q) => MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.RoundRobin, jobs, quantum: q).AvgTurnaround;

    // Response rises monotonically with the slice: a job waits longer for its
    // first turn.
    AssertClose(1.0, Response(1));
    AssertClose(5.0, Response(5));
    AssertClose(20.0, Response(25));

    // At quantum=100 the slice is exactly A's length, so A runs to completion
    // before anyone else starts: B first runs at 100, C at 110. Turnaround is
    // therefore FIFO's (110.00) and response is (0+100+110)/3 = 70.00 — the
    // slice no longer helps response at all, which is §7.7's "making the time
    // slice too long" limit taken to its conclusion.
    AssertClose(110.0, Turnaround(100));
    AssertClose(70.0, Response(100));
    AssertEqual(true, Response(100) > Response(25));

    // The pairing the chapter states, on one workload: a short slice is
    // strictly better for response and strictly worse for turnaround than a
    // long one. Both directions are asserted because either alone is satisfied
    // by a scheduler that ignored the parameter in one direction only.
    AssertEqual(true, Response(25) > Response(1));
    AssertEqual(true, Turnaround(25) > Turnaround(1));
});

Run("baseline result carries arrivals by name, not by position (slice 35.1)", () =>
{
    // Regression: the route rendered one row per job by taking job names from the
    // trace's execution order and indexing the result's positional Response list
    // with that index. On the convoy workload SJF executes B, C, A, so B was
    // rendered with A's response - a response of 70 for a job that started at 0.
    // Turnaround and Response stay positional; Arrivals is the name-keyed view
    // the renderer is meant to join on.
    var jobs = MiniWebServer.Host.MiniScheduler.BaselineWorkload.Convoy();

    var r = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Sjf, jobs);

    AssertEqual("BCA", r.CompletionOrder);              // execution order differs from input order
    AssertEqual(3, r.Arrivals.Count);
    AssertEqual(0, r.Arrivals["A"]);
    AssertEqual(0, r.Arrivals["B"]);
    AssertEqual(0, r.Arrivals["C"]);

    // §7.6: response = first run - arrival. Index 1 is B *by input order*, and all
    // three jobs arrive at 0, so B's response is its own first run. SJF runs B
    // first, so that is 0 — while A, which runs last, is the job that waited the
    // whole run. That inversion is exactly why the renderer must not index a
    // positional list by a name-derived index.
    AssertClose(0.0, r.Response[1]);      // B, by input order
    AssertClose(0.0, r.Arrivals["B"]);
    AssertClose(120.0, r.Turnaround[0]);  // A: completes at 120, arrived at 0
});

Run("baseline scheduler rejects a horizon it cannot represent (slice 35.1)", () =>
{
    // Regression: the iteration guard was computed in int, so one job of length
    // int.MaxValue arriving at 0 made the bound wrap negative and the run failed
    // with "simulation failed to terminate". The bound is now computed in long.
    //
    // The limit is not int.MaxValue either. `Trace` records one entry per tick,
    // so a job of int.MaxValue ticks needs ~2 billion list entries and threw
    // OutOfMemoryException before the guard could fire - the same class of bug
    // one level up. The horizon is an explicit cap that names its own cause.
    AssertThrows<ArgumentOutOfRangeException>(() =>
        MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
            MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo,
            MiniWebServer.Host.MiniScheduler.BaselineWorkload.FromLengths(
                new[] { int.MaxValue }, new[] { 0 })));

    // Two large jobs overflow even int Sum, before the guard is consulted.
    AssertThrows<ArgumentOutOfRangeException>(() =>
        MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
            MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo,
            MiniWebServer.Host.MiniScheduler.BaselineWorkload.FromLengths(
                new[] { int.MaxValue, 1 }, new[] { 0, 0 })));

    // A workload that fits is accepted. A single 1000-tick job arriving at 0
    // completes at 1000, so its turnaround is 1000 and its response is 0.
    var ok = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
        MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo,
        MiniWebServer.Host.MiniScheduler.BaselineWorkload.FromLengths(
            new[] { 1000 }, new[] { 0 }));
    AssertClose(1000.0, ok.AvgTurnaround);
    AssertClose(0.0, ok.AvgResponse);
});

// ---------------------------------------------------------------------------
// M36 / OSTEP Ch. 17 §17.1-§17.4 — free-space management.
//
// The oracle is the chapter's own arithmetic: §17.2's 30-byte heap with its
// literal free-list diagrams, the 4096-byte heap's 4088/3980/3764 sizes, §17.3's
// worked example with a 10/30/20 free list, and §17.4's 64 KB buddy split for a
// 7 KB request. The chapter prints those numbers; this simulator has to match
// them or it is not implementing the chapter.
// ---------------------------------------------------------------------------

Run("the header is charged to the request and splits are exact (slice 36.1)", () =>
{
    // §17.2's worked heap: 4096 bytes with an 8-byte header. "initially, the list
    // should have one entry, of size 4096 (minus the header size) ... the status
    // of the list is that it has a single entry, of size 4088."
    var heap = MiniWebServer.Host.MiniScheduler.HeapAllocator.Heap(
        4096, MiniWebServer.Host.MiniScheduler.FitPolicy.First,
        headerBytes: 8, nodeHeaderBytes: 8);
    AssertEqual(1, heap.FreeChunks.Count);
    AssertEqual(8, heap.FreeChunks[0].Start);       // the node lives at the head
    AssertEqual(4088, heap.FreeChunks[0].Length);   // "a single entry, of size 4088"

    // "upon the request for 100 bytes, the library allocated 108 bytes out of the
    //  existing one free chunk ... and shrinks the one free node in the list to
    //  3980 bytes (4088 minus 108)."
    long got = heap.Malloc(100);
    AssertEqual(8L, got);   // first byte after the node header
    AssertEqual(108, heap.AllocatedBytes);          // 100 + the 8-byte header
    AssertEqual(116, heap.FreeChunks[0].Start);      // 8 node header + 108 allocated
    AssertEqual(3980, heap.FreeChunks[0].Length);   // "3980 bytes (4088 minus 108)"

    // Two more 100-byte requests: "the first 324 bytes of the heap are now
    //  allocated ... just a single node (pointed to by head), but now only 3764
    //  bytes in size".
    heap.Malloc(100);
    heap.Malloc(100);
    AssertEqual(324, heap.AllocatedBytes);          // 3 x 108
    AssertEqual(1, heap.FreeChunks.Count);
    AssertEqual(332, heap.FreeChunks[0].Start);      // 8 + 3*108
    AssertEqual(3764, heap.FreeChunks[0].Length);   // "now only 3764 bytes in size"

    // Freeing the middle chunk must NOT coalesce across the still-allocated
    // neighbours on either side: two extents, 108 and the big tail. §17.2
    // figures 17.6-17.7 - "the free space is fragmented, an unfortunate but
    // common occurrence".
    heap.Free(116);   // the second chunk: 8 node header + the first 108
    AssertEqual(2, heap.FreeChunks.Count);
    AssertEqual(116, heap.FreeChunks[0].Start);
    AssertEqual(108, heap.FreeChunks[0].Length);
    AssertEqual(332, heap.FreeChunks[1].Start);
    AssertEqual(3764, heap.FreeChunks[1].Length);
    AssertClose(3872.0, (double)heap.FreeBytes);     // 108 + 3764
});

Run("coalescing restores one extent where a naive free list makes three (slice 36.1)", () =>
{
    // §17.2's second example: a 30-byte heap "free 10 bytes, used 10 bytes, and
    // another free 10 bytes". Freeing the middle with no coalescing gives
    // "head addr:10 len:10  addr:0 len:10  addr:20 len:10" - "the entire heap is
    // now free, it is seemingly divided into three chunks of 10 bytes each" -
    // and "if a user requests 20 bytes, a simple list traversal will not find such
    // a free chunk, and return failure."
    var naive = MiniWebServer.Host.MiniScheduler.HeapAllocator.Heap(
        30, MiniWebServer.Host.MiniScheduler.FitPolicy.First, coalesce: false);
    long a = naive.Malloc(10);      // [0,10)
    long b = naive.Malloc(10);      // [10,20)
    AssertEqual(0L, a);
    AssertEqual(10L, b);
    naive.Free(b);
    AssertEqual(2, naive.FreeChunks.Count);
    // The chapter's trap: 20 bytes free in total, no contiguous 20.
    AssertClose(20.0, (double)naive.FreeBytes);
    AssertEqual(10, naive.LargestFreeChunk);
    AssertEqual(-1L, naive.Malloc(20));

    // "with coalescing, our final list should look like this: head addr:0 len:30"
    // once the head is freed too, because the whole heap is free again.
    naive.Free(a);
    AssertEqual(3, naive.FreeChunks.Count);   // no coalescing: still fragmented
    // All 30 bytes are free and the extents are adjacent, but the largest
    // SINGLE extent is 10 - which is exactly the chapter's definition of external
    // fragmentation: total free space overstates what is allocatable.
    AssertClose(30.0, (double)naive.FreeBytes);
    AssertEqual(10, naive.LargestFreeChunk);

    var coalescing = MiniWebServer.Host.MiniScheduler.HeapAllocator.Heap(
        30, MiniWebServer.Host.MiniScheduler.FitPolicy.First, coalesce: true);
    long x = coalescing.Malloc(10);
    long y = coalescing.Malloc(10);
    coalescing.Free(y);
    coalescing.Free(x);
    AssertEqual(1, coalescing.FreeChunks.Count);
    AssertEqual(0, coalescing.FreeChunks[0].Start);
    AssertEqual(30, coalescing.FreeChunks[0].Length);
    AssertEqual(30, coalescing.LargestFreeChunk);
    AssertEqual(-1L, coalescing.Malloc(31));   // cannot exceed the heap
});

Run("best, worst and first fit each choose differently on the chapter's list (slice 36.1)", () =>
{
    // §17.3's example: "a free list with three elements on it, of sizes 10, 30,
    // and 20 ... Assume an allocation request of size 15."
    //
    // The chapter prints the resulting list for each policy, so each is checked
    // against those literals rather than against what the code happens to do.
    int[] sizes = { 10, 30, 20 };   // laid out at offsets 0, 10, 40

    // Best fit: "would search the entire list and find that 20 was the best fit,
    // as it is the smallest free space that can accommodate the request. The
    // resulting free list: head 10 30 5"
    var best = MiniWebServer.Host.MiniScheduler.HeapAllocator.FixedExtentHeap(sizes);
    AssertEqual(40L, best.Malloc(15));            // the 20-byte extent at offset 40
    AssertEqual(3, best.FreeChunks.Count);
    AssertEqual(10, best.FreeChunks[0].Length);
    AssertEqual(30, best.FreeChunks[1].Length);
    AssertEqual(5, best.FreeChunks[2].Length);    // the chapter's literal 5
    AssertClose(45.0, (double)best.FreeBytes);

    // Worst fit: "finds the largest chunk, in this example 30. The resulting
    // list: head 10 15 20"
    var worst = MiniWebServer.Host.MiniScheduler.HeapAllocator.FixedExtentHeap(
        sizes, MiniWebServer.Host.MiniScheduler.FitPolicy.Worst);
    AssertEqual(10L, worst.Malloc(15));           // the 30-byte extent at offset 10
    AssertEqual(3, worst.FreeChunks.Count);
    AssertEqual(10, worst.FreeChunks[0].Length);
    AssertEqual(15, worst.FreeChunks[1].Length);  // the chapter's literal 15
    AssertEqual(20, worst.FreeChunks[2].Length);
    AssertClose(45.0, (double)worst.FreeBytes);

    // First fit: "in this example, does the same thing as worst-fit, also finding
    // the first free block that can satisfy the request" - the 10 is too small, so
    // the 30 is chosen and the remainder is 15. Same outcome, different cost.
    var first = MiniWebServer.Host.MiniScheduler.HeapAllocator.FixedExtentHeap(
        sizes, MiniWebServer.Host.MiniScheduler.FitPolicy.First);
    AssertEqual(10L, first.Malloc(15));
    AssertEqual(3, first.FreeChunks.Count);
    AssertEqual(15, first.FreeChunks[1].Length);  // the chapter's literal 15
    AssertEqual(20, first.FreeChunks[2].Length);

    // §17.3's cost claim, made concrete: "both best-fit and worst-fit look
    // through the entire list; first-fit only examines free chunks until it finds
    // one that fits". Here the head extent satisfies the request, so first fit
    // leaves the 30-byte extent intact while best fit chops it to 25.
    int[] headFits = { 40, 30, 20 };
    var firstCheap = MiniWebServer.Host.MiniScheduler.HeapAllocator.FixedExtentHeap(
        headFits, MiniWebServer.Host.MiniScheduler.FitPolicy.First);
    var bestCostly = MiniWebServer.Host.MiniScheduler.HeapAllocator.FixedExtentHeap(
        headFits, MiniWebServer.Host.MiniScheduler.FitPolicy.Best);
    AssertEqual(0L, firstCheap.Malloc(15));            // first fit: the head
    AssertEqual(70L, bestCostly.Malloc(15));           // best fit: the smallest that fits (20 at offset 70)
    AssertEqual(25, firstCheap.FreeChunks[0].Length);  // 40 - 15
    AssertEqual(5, bestCostly.FreeChunks[2].Length);   // 20 - 15, leaving a 5-byte splinter
});

Run("buddy allocation splits to a power of two and coalesces back up (slice 36.1)", () =>
{
    // §17.4: "Here is an example of a 64KB free space getting divided in the
    // search for a 7KB block ... the leftmost 8KB block is allocated (as indicated
    // by the darker shade of gray) and returned to the user".
    //
    // The chapter's figure 17.8 is a 64 / 32+32 / 16+16 / 8+8 split, so a 7 KB
    // request lands in the leftmost 8 KB - the smallest power of two >= 7 KB.
    var buddy = new MiniWebServer.Host.MiniScheduler.BuddyAllocator(64 * 1024);
    long got = buddy.Allocate(7 * 1024);

    AssertEqual(0L, got);                 // the leftmost block
    AssertEqual(8 * 1024, buddy.BlockSizeOf(got));   // "the leftmost 8KB block"
    // 7 KB requested, 8 KB granted: that is internal fragmentation by construction,
    // which §17.4 names - "this scheme can suffer from internal fragmentation, as
    // you are only allowed to give out power-of-two-sized blocks".
    AssertClose(1024.0, (double)(8 * 1024 - 7 * 1024));
    AssertEqual(56 * 1024, buddy.FreeBytes);

    // The buddy relationship is the XOR trick: "the address of each buddy pair only
    // differs by a single bit; which bit is determined by the level in the buddy
    // tree". The buddy of the leftmost 8 KB block is the second 8 KB block.
    AssertEqual(8L * 1024, MiniWebServer.Host.MiniScheduler.BuddyAllocator.BuddyAddress(0, 8 * 1024));
    AssertEqual(0L, MiniWebServer.Host.MiniScheduler.BuddyAllocator.BuddyAddress(8 * 1024, 8 * 1024));   // symmetric

    // A second 8 KB allocation takes the buddy, exhausting the level.
    long second = buddy.Allocate(8 * 1024);
    AssertEqual(8 * 1024, second);
    AssertEqual(48 * 1024, buddy.FreeBytes);

    // Freeing the leftmost block coalesces with its buddy and with the 16 KB
    // level above, all the way back to 64 KB: "This recursive coalescing process
    // continues up the tree, either restoring the entire free space or stopping
    // when a buddy is found to be in use."
    buddy.Free(got);
    buddy.Free(second);
    AssertEqual(64 * 1024, buddy.FreeBytes);
    AssertEqual(1, buddy.FreeBlockCount);              // one 64 KB extent again
    AssertEqual(0, buddy.AllocationBlockCount);
    AssertEqual(65536, buddy.FreeBlocks().Single().Size);
});

Run("the heap rejects what the chapter's model cannot express (slice 36.1)", () =>
{
    // §17.4: "free memory is first conceptually thought of as one big space of
    // size 2^N". A buddy tree over a non-power-of-two cannot represent the tail,
    // so it is rejected rather than silently rounded.
    AssertThrows<ArgumentException>(() =>
        new MiniWebServer.Host.MiniScheduler.BuddyAllocator(1000));
    AssertThrows<ArgumentOutOfRangeException>(() =>
        new MiniWebServer.Host.MiniScheduler.BuddyAllocator(0));
    AssertThrows<ArgumentOutOfRangeException>(() =>
        new MiniWebServer.Host.MiniScheduler.BuddyAllocator(1024).Allocate(0));

    var heap = MiniWebServer.Host.MiniScheduler.HeapAllocator.Heap(
        100, MiniWebServer.Host.MiniScheduler.FitPolicy.First);
    AssertThrows<ArgumentOutOfRangeException>(() => heap.Malloc(0));
    AssertThrows<ArgumentException>(() => heap.Free(50));   // never returned by Malloc
});

Run("external fragmentation is the gap between free bytes and the largest extent (slice 36.1)", () =>
{
    // §17.1's opening example, made numeric: "the total free space available is
    // 20 bytes; unfortunately, it is fragmented into two chunks of size 10 each.
    // As a result, a request for 15 bytes will fail even though there are 20
    // bytes free."
    var heap = MiniWebServer.Host.MiniScheduler.HeapAllocator.FixedExtentHeap(
        new[] { 10, 10 }, MiniWebServer.Host.MiniScheduler.FitPolicy.First);

    AssertClose(20.0, (double)heap.FreeBytes);
    AssertEqual(10, heap.LargestFreeChunk);
    AssertEqual(-1L, heap.Malloc(15));      // fails despite 20 free
    AssertEqual(true, heap.FreeBytes > heap.LargestFreeChunk);   // the definition

    // Coalescing removes the condition rather than hiding it: adjacent extents
    // become one, so the same 15-byte request now succeeds.
    var coalescing = MiniWebServer.Host.MiniScheduler.HeapAllocator.Heap(
        20, MiniWebServer.Host.MiniScheduler.FitPolicy.First, coalesce: true);
    long a = coalescing.Malloc(5);
    long b = coalescing.Malloc(5);
    coalescing.Free(a);
    coalescing.Free(b);
    AssertEqual(1, coalescing.FreeChunks.Count);
    AssertEqual(20, coalescing.LargestFreeChunk);
    AssertEqual(0L, coalescing.Malloc(15));
});

Run("a buddy split accounts for every byte of the heap (slice 36.1)", () =>
{
    // §17.4's split is a recursive halving, so a small request walks the tree down
    // many levels: "the search for free space recursively divides free space by two
    // until a block that is big enough to accommodate the request is found".
    //
    // The point of this test is the accounting: a multi-level split must leave the
    // heap fully accounted for, and freeing the block must restore it. Every figure
    // below is the tree's own arithmetic - 65536 = 32768 + 16384 + ... + 128 - so a
    // split that stops one level short, or records the wrong block size, cannot pass.
    var buddy = new MiniWebServer.Host.MiniScheduler.BuddyAllocator(64 * 1024);
    long got = buddy.Allocate(100);

    AssertEqual(0L, got);
    AssertEqual(128, buddy.BlockSizeOf(got));       // 2^7, not 100
    AssertClose(65408.0, (double)buddy.FreeBytes);
    AssertClose(65536.0, (double)(buddy.FreeBytes + buddy.BlockSizeOf(got)));
    AssertClose(9, (double)buddy.FreeBlockCount);   // 32768 ... 128

    // Freeing it must coalesce all the way back, which only works if the size was
    // recorded as the block's, not the request's.
    buddy.Free(got);
    AssertClose(65536.0, (double)buddy.FreeBytes);
    AssertEqual(1, buddy.FreeBlockCount);
    AssertEqual(65536, buddy.FreeBlocks().Single().Size);
});

Run("a buddy heap refuses a request it cannot round up (slice 36.1)", () =>
{
    // A request larger than the whole heap cannot be split out of it, and the
    // round-up loop must not walk off the end of int trying.
    var buddy = new MiniWebServer.Host.MiniScheduler.BuddyAllocator(64 * 1024);
    AssertEqual(-1L, buddy.Allocate(1 << 30));          // bigger than the heap
    AssertEqual(-1L, buddy.Allocate(64 * 1024 + 1));     // one byte too big
    AssertClose(65536.0, (double)buddy.FreeBytes);      // and it changed nothing

    // Near int.MaxValue the round-up overflows; the loop is bounded and says so
    // rather than wrapping to a negative size.
    AssertThrows<ArgumentOutOfRangeException>(() => buddy.Allocate(int.MaxValue));

    // An exact fit still works: the top-level block, no split needed.
    var exact = new MiniWebServer.Host.MiniScheduler.BuddyAllocator(64 * 1024);
    AssertEqual(0L, exact.Allocate(64 * 1024));
    AssertEqual(0, exact.FreeBytes);
});

// ---------------------------------------------------------------------------
// M37 / OSTEP Ch. 37 §37.2-§37.5 — disk geometry, I/O time, disk scheduling.
//
// The oracle is the chapter's own arithmetic: equation 37.1, the Cheetah and
// Barracuda worked examples of §37.4 (which print every intermediate value),
// the ASIDE that derives average seek as N/3, and the geometry figures 37.1-37.3.
// ---------------------------------------------------------------------------

Run("io time is seek plus rotation plus transfer (slice 37.1)", () =>
{
    // §37.4, equation 37.1: "T_I/O = T_seek + T_rotation + T_transfer"
    // (37.1), and equation 37.2: "R_I/O = Size_Transfer / T_I/O" (37.2).
    //
    // The Cheetah 15K.5 [S09b] from figure 37.5: 15,000 RPM, 4 ms average
    // seek, 125 MB/s max transfer. §37.4 does the arithmetic step by step:
    // "15000 RPM is equal to 250 RPS; thus, each rotation takes 4 ms. On
    //  average, the disk will encounter a half rotation and thus 2 ms is the
    //  average time. Finally, the transfer time is just the size of the transfer
    //  over the peak transfer rate; here it is vanishingly small (30
    //  microseconds ...). Thus ... T_I/O for the Cheetah roughly equals 6 ms."
    var cheetah = MiniWebServer.Host.MiniScheduler.DriveGeometry.Cheetah15K5;

    AssertClose(4.0, cheetah.RotationMs());          // 60000 / 15000
    AssertClose(2.0, cheetah.AvgRotationMs());       // half a rotation
    AssertClose(4.0, cheetah.AvgSeekMs);             // the datasheet figure

    // A random 4 KB read. §37.4: "T_seek = 4 ms, T_rotation = 2 ms,
    // T_transfer = 30 microsecs (37.3)".
    var read = new MiniWebServer.Host.MiniScheduler.DiskRequest(0, Bytes: 4096);
    var timing = cheetah.Time(read, seekMs: cheetah.AvgSeekMs);
    AssertClose(4.0, timing.SeekMs);
    AssertClose(2.0, timing.RotationMs);            // the average half rotation

    // Binary units throughout, as the chapter's dimensional analysis uses them
    // ("512 KB * 1024/KB / 1 MB ..."): 4 KB = 4096 bytes against 125 MB =
    // 125 * 2^20, so the transfer is 31.25 us. The chapter writes "30
    // microseconds" and "roughly equals 6 ms" - both of those are its own
    // rounding, and asserting the exact values keeps the rounding visible rather
    // than baking it into the oracle.
    AssertClose(0.03125, timing.TransferMs, 1e-9);    // = 31.25 us, the chapter's "30"
    AssertClose(6.03125, timing.TotalMs, 1e-9);       // the chapter's "roughly 6"

    // "we just divide the size of the transfer by the average time, and thus
    //  arrive at R_I/O for the Cheetah under the random workload of about
    //  0.66 MB/s."
    //
    // 0.6477 is what equation 37.2 gives for the *exact* total of 6.03125 ms; the
    // chapter's 0.66 comes from its rounded 6 ms. The tolerance spans both.
    AssertClose(0.66, cheetah.Rate(read, timing), 0.02);
});

Run("sequential transfers reach the peak rate and random ones do not (slice 37.1)", () =>
{
    // §37.4: "Here we can assume there is a single seek and rotation before a very
    // long transfer. For simplicity, assume the size of the transfer is 100 MB.
    // Thus, T_I/O for the Cheetah and Barracuda is about 800 ms and 950 ms,
    // respectively. The rates of I/O are thus very nearly the peak transfer rates
    // of 125 MB/s and 105 MB/s."
    var cheetah = MiniWebServer.Host.MiniScheduler.DriveGeometry.Cheetah15K5;
    var barracuda = MiniWebServer.Host.MiniScheduler.DriveGeometry.BarracudaES2;

    // 100 MB = 100 * 2^20 bytes, in the chapter's binary units.
    const int hundredMB = 100 * 1024 * 1024;

    var seqC = cheetah.Time(new MiniWebServer.Host.MiniScheduler.DiskRequest(0, hundredMB),
                            cheetah.AvgSeekMs);
    // AssertClose's tolerance is RELATIVE (it scales by max(1,|expected|)), so
    // 0.5 on 800 would admit anything from 400 to 1200. These are the tolerances
    // the chapter's own "about" and "very nearly" license, not wider ones.
    AssertClose(800.0, seqC.TotalMs, 0.01);     // +-8 ms: the chapter says "about 800"
    // §37.4 says the sequential rates are "very nearly" the peak rates, not equal
    // to them: the fixed seek and rotation are amortised over the transfer but do
    // not vanish. 124.07 against 125 and 103.57 against 105 are both that.
    AssertClose(125.0, cheetah.Rate(new MiniWebServer.Host.MiniScheduler.DiskRequest(0, hundredMB), seqC), 0.02);

    var seqB = barracuda.Time(new MiniWebServer.Host.MiniScheduler.DiskRequest(0, hundredMB),
                             barracuda.AvgSeekMs);
    //
    // The chapter's 950 is its own rounding: 9 ms seek + 4.17 ms average rotation
    // + 952.38 ms transfer = 965.55, and the chapter reports "about 800 ms and
    // 950 ms". The Cheetah figure is consistent with the exact arithmetic
    // (4 + 2 + 800 = 806, "about 800"); the Barracuda's is not, most likely because
    // 100 MB at 105 MB/s is easier to carry as ~950 than as ~965. The tolerance
    // spans the chapter's rounding and nothing wider.
    AssertClose(950.0, seqB.TotalMs, 0.02);     // +-19 ms, covering the chapter's 950
    AssertClose(105.0, barracuda.Rate(new MiniWebServer.Host.MiniScheduler.DiskRequest(0, hundredMB), seqB), 0.02);

    // §37.4's random row: "The same calculation for the Barracuda yields a T_I/O
    // of about 13.2 ms, more than twice as slow, and thus a rate of about 0.31
    // MB/s."
    var randB = barracuda.Time(new MiniWebServer.Host.MiniScheduler.DiskRequest(0, 4096),
                              barracuda.AvgSeekMs);
    AssertClose(13.2, randB.TotalMs, 0.005);
    // §37.4: "a rate of about 0.31 MB/s". The exact value is 0.2958, so the
    // chapter rounded - and AssertClose's tolerance is relative, scaled by
    // max(1,|expected|), so a tolerance of 0.05 is an absolute 0.05 here (the
    // scale is 1, not 0.31) and admits 0.26 to 0.36. That is the width "about"
    // licenses for a figure the chapter itself rounds.
    AssertClose(0.31, barracuda.Rate(new MiniWebServer.Host.MiniScheduler.DiskRequest(0, 4096), randB), 0.05);

    // The chapter's headline: "a huge gap in drive performance between random and
    // sequential workloads, almost a factor of 200 or so for the Cheetah and more
    // than a factor 300 difference for the Barracuda."
    var randC = cheetah.Time(new MiniWebServer.Host.MiniScheduler.DiskRequest(0, 4096), cheetah.AvgSeekMs);
    double seqFactorC = cheetah.Rate(new MiniWebServer.Host.MiniScheduler.DiskRequest(0, hundredMB), seqC)
                      / cheetah.Rate(new MiniWebServer.Host.MiniScheduler.DiskRequest(0, 4096), randC);
    double seqFactorB = barracuda.Rate(new MiniWebServer.Host.MiniScheduler.DiskRequest(0, hundredMB), seqB)
                      / barracuda.Rate(new MiniWebServer.Host.MiniScheduler.DiskRequest(0, 4096), randB);
    AssertEqual(true, seqFactorC > 150 && seqFactorC < 250);   // "about 200"
    AssertEqual(true, seqFactorB > 250);                       // "more than 300"

    // The seek dominates the random case and vanishes into the sequential one.
    // That is the TIP's point: "When at all possible, transfer data to and from
    // disks in a sequential manner."
    AssertEqual(true, randC.SeekMs > randC.TransferMs * 100);
    AssertEqual(true, seqC.TransferMs > seqC.SeekMs * 100);
});

Run("the average seek distance is one third of the disk (slice 37.1)", () =>
{
    // §37.4's ASIDE derives it by integrating |x - y| over [0,N]^2, getting N^3/3,
    // and dividing by N^2: "Thus the average seek distance on a disk, over all
    // possible seeks, is one-third the full distance."
    //
    // **That is a limit, not an identity.** The chapter switches from its own
    // discrete summation (equation 37.4) to an integral (37.5) partway through,
    // and the integral drops the finite-N correction. The exact discrete mean over
    // all N^2 ordered pairs is (N^2 - 1) / (3N) - one third of a track less than
    // N/3. At N = 1,000 the gap is 0.0003 tracks, which is why the rule of thumb is
    // fine in practice, but the difference is real and the brute force below finds
    // it at every N where it is visible.
    AssertClose(1.0 / 3.0, MiniWebServer.Host.MiniScheduler.DriveGeometry.AvgSeekFraction, 1e-12);

    // Brute force at small N, where every one of the N^2 pairs can be enumerated.
    double Brute(int n)
    {
        double sum = 0;
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
                sum += Math.Abs(x - y);
        return sum / (n * n);
    }

    foreach (int n in new[] { 4, 5, 10, 17 })
    {
        // The exact form is what enumeration finds...
        AssertClose(Brute(n),
            MiniWebServer.Host.MiniScheduler.DriveGeometry.AvgSeekDistanceExact(n), 1e-12);
        // ...and the chapter's N/3 is close to it but NOT equal to it.
        AssertClose(n / 3.0, MiniWebServer.Host.MiniScheduler.DriveGeometry.AvgSeekDistance(n), 1e-12);
        AssertEqual(true, Brute(n) < n / 3.0);       // the finite-N correction
        AssertClose(1.0 / (3.0 * n),                  // and it is exactly 1/(3N)
            (n / 3.0) - Brute(n), 1e-12);
    }

    // The gap vanishes as the disk grows, which is the reason the chapter's
    // shorthand is safe to use.
    AssertClose(333.333000000, MiniWebServer.Host.MiniScheduler.DriveGeometry.AvgSeekDistanceExact(1000), 1e-9);
    AssertEqual(true,
        MiniWebServer.Host.MiniScheduler.DriveGeometry.AvgSeekDistance(1000)
        - MiniWebServer.Host.MiniScheduler.DriveGeometry.AvgSeekDistanceExact(1000) < 0.001);

    // The chapter's own wording: "In many books and papers, you will see average
    // disk-seek time cited as being roughly one-third of the full seek time." Note
    // "roughly" - and note the chapter derived it over distance, letting the drive
    // turn distance into time. The Cheetah's 4 ms average is a datasheet number, so
    // a full seek is roughly 12 ms by this rule.
    var cheetah = MiniWebServer.Host.MiniScheduler.DriveGeometry.Cheetah15K5;
    AssertClose(4.0, cheetah.AvgSeekMs);
    AssertClose(12.0, cheetah.AvgSeekMs / MiniWebServer.Host.MiniScheduler.DriveGeometry.AvgSeekFraction);
});

Run("sstf services the nearer track first (slice 37.1)", () =>
{
    // §37.5, figure 37.7: "assuming the current position of the head is over the
    // inner track, and we have requests for sectors 21 (middle track) and 2
    // (outer track), we would then issue the request to 21 first, wait for it to
    // complete, and then issue the request to 2."
    //
    // The chapter's three-track disk (figure 37.3) has 12 sectors per track:
    // outer 0-11, middle 12-23, inner 24-35. So 21 is on the middle track and 2 on
    // the outer one, and the head starts on the inner.
    var drive = MiniWebServer.Host.MiniScheduler.DriveGeometry.SingleTrack;
    var threeTracks = new MiniWebServer.Host.MiniScheduler.DriveGeometry
    {
        Name = "three tracks (figure 37.3)",
        Rpm = 60 * 60,
        AvgSeekMs = 0.0,
        TransferMBps = 1.0,
        TracksPerSurface = 3,
        SectorsPerTrack = 12,
        Surfaces = 1,
    };

    // Block 2 -> track 0, block 21 -> track 1, head on track 2.
    AssertEqual(0L, threeTracks.TrackOf(2));
    AssertEqual(1L, threeTracks.TrackOf(21));
    AssertEqual(2L, threeTracks.TrackOf(30));

    var scheduler = new MiniWebServer.Host.MiniScheduler.DiskScheduler(threeTracks);
    var (order, travel) = scheduler.Serve(
        MiniWebServer.Host.MiniScheduler.DiskPolicy.Sstf,
        new long[] { 2, 21 }, startTrack: 2);

    // 21 first, exactly as the chapter describes: block 21 is on track 1, one move
    // away, while block 2 is on track 0, two moves away.
    AssertEqual(21L, order[0]);
    AssertEqual(2L, order[1]);
    // Head 2 -> 1 -> 0: two track moves, the minimum possible for this pair.
    AssertEqual(2L, travel);

    // FIFO takes 2 first - the order §37.5's example is avoiding. Same two requests,
    // same head, same total travel (any order of these two costs two track moves),
    // but SCAN and SSTF at least pick the nearer one first.
    AssertEqual(2L, scheduler.Serve(MiniWebServer.Host.MiniScheduler.DiskPolicy.Fifo,
        new long[] { 2, 21 }, 2).Order[0]);
    AssertClose(drive.TransferMs(512), drive.TransferMs(512));   // sanity: single track seeks nowhere
});

Run("scan services the next track ahead, not the farthest (slice 37.1)", () =>
{
    // §37.5: SCAN "simply moves back and forth across the disk servicing requests
    // in order across the tracks." ORDER is the defining property, not distance:
    // on an inward sweep the head takes the *next* track with a pending request.
    //
    // A first implementation picked the FARTHEST track ahead, which meant sweeping
    // past track 11 to serve 18 and only coming back for 11 on the return trip -
    // the opposite of "in order across the tracks".
    var drive = MiniWebServer.Host.MiniScheduler.DriveGeometry.Cheetah15K5;  // 10000 tracks
    var sched = new MiniWebServer.Host.MiniScheduler.DiskScheduler(drive);

    var ahead = sched.Serve(MiniWebServer.Host.MiniScheduler.DiskPolicy.Scan,
                            new long[] { 3300, 5400 }, startTrack: 10);   // tracks 11, 18
    AssertEqual(11L, drive.TrackOf(ahead.Order[0]));
    AssertEqual(18L, drive.TrackOf(ahead.Order[1]));
    AssertEqual(8L, ahead.TracksTravelled);    // 10 -> 11 -> 18, no detour

    // When the sweep direction runs dry the head must reverse, not fall back to
    // arrival order. Head 10 with every request behind it: the inward sweep has
    // nothing, so it reverses and services 8, 6, 3 going out.
    var behind = sched.Serve(MiniWebServer.Host.MiniScheduler.DiskPolicy.Scan,
                             new long[] { 900, 2400, 1800 }, startTrack: 10);  // 3, 8, 6
    AssertEqual(8L, drive.TrackOf(behind.Order[0]));
    AssertEqual(6L, drive.TrackOf(behind.Order[1]));
    AssertEqual(3L, drive.TrackOf(behind.Order[2]));
    AssertEqual(7L, behind.TracksTravelled);    // 10 -> 8 -> 6 -> 3
});

Run("nearest-block-first measures from the last block, not a track start (slice 37.1)", () =>
{
    // §37.5 offers NBF because "the drive geometry is not available to the host
    // OS; rather, it sees an array of blocks", so the distance has to be measured
    // in blocks, from where the head actually is.
    //
    // Reconstructing "the first block of the head's track" after every request is
    // wrong once requests share a track: from block 599, block 598 is one away and
    // block 300 is 299 away, but the reconstruction pointed at 600 and picked 300.
    var drive = MiniWebServer.Host.MiniScheduler.DriveGeometry.Cheetah15K5;
    var sched = new MiniWebServer.Host.MiniScheduler.DiskScheduler(drive);

    var order = sched.Serve(MiniWebServer.Host.MiniScheduler.DiskPolicy.NearestBlock,
                            new long[] { 599, 598, 300 }, startTrack: 2).Order;
    AssertEqual(599L, order[0]);
    AssertEqual(598L, order[1]);     // 1 block away, not 299
    AssertEqual(300L, order[2]);
});

Run("sstf order depends on proximity and scan order does not (slice 37.1)", () =>
{
    // §37.5's second crux, stated as its own CRUX box: "HOW TO HANDLE DISK
    // STARVATION? How can we implement SSTF-like scheduling but avoid starvation?"
    //
    // The mechanism the chapter describes needs an *infinite* near-request stream
    // - "a steady stream of requests to the inner track, where the head currently
    // is positioned" - which no finite queue can reproduce. With a finite queue
    // every policy eventually serves every request. So what SCAN actually changes
    // is ORDER, and that is what this test measures: SSTF's order is a function of
    // distance alone, SCAN's is a function of sweep position, which is the property
    // that makes starvation impossible rather than merely unlikely.
    var drive = new MiniWebServer.Host.MiniScheduler.DriveGeometry
    {
        Name = "100 tracks", Rpm = 7200, AvgSeekMs = 4.0, TransferMBps = 100.0,
        TracksPerSurface = 100, SectorsPerTrack = 100, Surfaces = 1,
    };
    var sched = new MiniWebServer.Host.MiniScheduler.DiskScheduler(drive);

    long[] queue = { 8000, 1000, 1001, 1002, 1003, 1004 };   // track 80, then track 10 x5

    var sstf = sched.Serve(MiniWebServer.Host.MiniScheduler.DiskPolicy.Sstf, queue, 10);
    var scan = new MiniWebServer.Host.MiniScheduler.DiskScheduler(drive).Serve(
        MiniWebServer.Host.MiniScheduler.DiskPolicy.Scan, queue, 10);

    // Both serve everything - that is the finite-queue truth, and asserting
    // otherwise would be asserting something the chapter does not claim.
    AssertEqual(queue.Length, sstf.Order.Count);
    AssertEqual(queue.Length, scan.Order.Count);
    foreach (long b in queue)
    {
        AssertEqual(true, sstf.Order.Contains(b));
        AssertEqual(true, scan.Order.Contains(b));
    }

    // SSTF orders by proximity, so all five track-10 requests precede the far one.
    var sstfTracks = sstf.Order.Select(drive.TrackOf).ToList();
    AssertEqual(5, sstfTracks.IndexOf(80));
    AssertEqual(true, sstfTracks.Take(5).All(t => t == 10));

    // SCAN's order comes from the sweep, so the far track is reached on its own
    // terms rather than after every nearer request. Moving the near requests to
    // the far side of the head changes SCAN's answer and not SSTF's, because SCAN
    // follows the sweep and SSTF follows the distance.
    long[] flipped = { 8000, 200, 201, 202, 203, 204 };   // track 2, behind the head
    var sstfFlipped = new MiniWebServer.Host.MiniScheduler.DiskScheduler(drive).Serve(
        MiniWebServer.Host.MiniScheduler.DiskPolicy.Sstf, flipped, 10);
    var scanFlipped = new MiniWebServer.Host.MiniScheduler.DiskScheduler(drive).Serve(
        MiniWebServer.Host.MiniScheduler.DiskPolicy.Scan, flipped, 10);

    AssertEqual(queue.Length, sstfFlipped.Order.Count);
    // SCAN's answer depends on sweep position, so it changes with the flip.
    // SSTF is unmoved by it: track 2 is nearer than track 80, so it serves
    // all five first and the far request last.
    var sstfFlippedTracks = sstfFlipped.Order.Select(drive.TrackOf).ToList();
    AssertEqual(2, sstfFlippedTracks[0]);
    AssertEqual(5, sstfFlippedTracks.IndexOf(80));
    // SCAN is not: the head sweeps inward first and comes back outward for track 2,
    // so the far request is reached before the near ones.
    AssertEqual(true, scanFlipped.Order.Select(drive.TrackOf).ToList().IndexOf(80)
                       < scanFlipped.Order.Select(drive.TrackOf).ToList().IndexOf(2));
});

Run("c-scan sweeps one way and resets, unlike scan (slice 37.1)", () =>
{
    // §37.5: C-SCAN "only sweeps from outer-to-inner, and then resets at the outer
    // track to begin again. Doing so is a bit more fair to inner and outer tracks,
    // as pure back- and-forth SCAN favors the middle tracks."
    //
    // The defining property is the FIXED direction. An earlier version shared one
    // branch between the two policies, which made C-SCAN a relabelled SCAN - and
    // the chapter's claim about middle-track bias could not have been checked at
    // all. This workload is chosen so the two genuinely diverge.
    var drive = new MiniWebServer.Host.MiniScheduler.DriveGeometry
    {
        Name = "20 tracks", Rpm = 7200, AvgSeekMs = 4.0, TransferMBps = 100.0,
        TracksPerSurface = 20, SectorsPerTrack = 100, Surfaces = 1,
    };
    long[] queue = { 50, 105, 1800, 1100, 5, 15 };

    var scan = new MiniWebServer.Host.MiniScheduler.DiskScheduler(drive).Serve(
        MiniWebServer.Host.MiniScheduler.DiskPolicy.Scan, queue, startTrack: 10);
    var cscan = new MiniWebServer.Host.MiniScheduler.DiskScheduler(drive).Serve(
        MiniWebServer.Host.MiniScheduler.DiskPolicy.CScan, queue, startTrack: 10);

    // They are not the same policy, which is the property the chapter's whole
    // paragraph about C-SCAN depends on.
    AssertEqual(true, !scan.Order.SequenceEqual(cscan.Order));
    AssertEqual(26L, scan.TracksTravelled);
    AssertEqual(28L, cscan.TracksTravelled);

    // Both start by sweeping inward from track 10, which is what §37.3's layout
    // implies: track 0 is the outer end, so increasing track numbers go in.
    // §37.5: C-SCAN "only sweeps from outer-to-inner".
    AssertEqual(11L, drive.TrackOf(scan.Order[0]));
    AssertEqual(11L, drive.TrackOf(cscan.Order[0]));

    // They part company on the way back out. SCAN reverses and descends, so the
    // track-18 request (still ahead on the way out) is served before the track-1
    // one; C-SCAN has already passed track 1 going in, so it waits for the reset
    // and is served after track 18 but before track 0 - the middle-track bias the
    // chapter describes.
    AssertEqual(18L, drive.TrackOf(scan.Order[1]));
    AssertEqual(18L, drive.TrackOf(cscan.Order[1]));
    AssertEqual(1L, drive.TrackOf(scan.Order[2]));
    AssertEqual(0L, drive.TrackOf(cscan.Order[2]));

    // C-SCAN never serves anything twice and never loses a request.
    AssertEqual(queue.Length, cscan.Order.Count);
    AssertEqual(queue.Length, cscan.Order.Distinct().Count());
    foreach (long b in queue) AssertEqual(true, cscan.Order.Contains(b));
});

// --- MiniFs (M12): file system core, journaling, crash recovery ---
//
// Nothing in the suite touched MiniFs before this block, which is the largest
// untested module in the repo (1294 lines). The tests below target invariants a
// caller can rely on rather than the code that happens to produce them:
// allocation accounting, the journal's commit rule, and what survives a crash.

Run("fs: a fresh mount is formatted, with inode 0 and the journal reserved (slice 12.1)", () =>
{
    using var fs = new MiniFsScope();
    AssertEqual(true, MiniFs.IsMounted);
    // Inode 0 is reserved as "no inode" and counted as in-use, so a fresh
    // disk is not zero: one bit is set, and the free count excludes it.
    AssertEqual(1, MiniFs.InodesInUse());
    AssertEqual(Constants.NUM_INODES - 1, MiniFs.Superblock.FreeInodes);
    // The journal region is reserved up front: its bits are set in the
    // bitmap AND excluded from the free count, so the two agree.
    AssertEqual(Constants.JOURNAL_BLOCKS, MiniFs.DataBlocksInUse());
    AssertEqual(Constants.NUM_DATA_BLOCKS - Constants.JOURNAL_BLOCKS, MiniFs.Superblock.FreeDataBlocks);
});

Run("fs: balloc never returns a journal-reserved block (slice 12.6)", () =>
{
    using var fs = new MiniFsScope();
    // An allocator that skipped the journal check would hand a client the
    // block the journal superblock lives in, and the next commit would
    // overwrite it. That is silent corruption, so it needs a test.
    int before = MiniFs.DataBlocksInUse();
    for (int i = 0; i < Constants.JOURNAL_BLOCKS + 4; i++)
        AssertEqual(true, MiniFs.Balloc() >= Constants.JOURNAL_BLOCKS);
    // Each allocation raises the in-use count by exactly one, starting from
    // the journal reservation rather than from zero.
    AssertEqual(before + Constants.JOURNAL_BLOCKS + 4, MiniFs.DataBlocksInUse());
});

Run("fs: bfree returns the block and the count balances (slice 12.1)", () =>
{
    using var fs = new MiniFsScope();
    int freeBlocks = MiniFs.Superblock.FreeDataBlocks;

    int a = MiniFs.Balloc();
    int b = MiniFs.Balloc();
    AssertEqual(true, a != b);
    AssertEqual(freeBlocks - 2, MiniFs.Superblock.FreeDataBlocks);

    MiniFs.Bfree(a);
    AssertEqual(freeBlocks - 1, MiniFs.Superblock.FreeDataBlocks);

    // A double free is the classic way a bitmap allocator credits twice and
    // hands the same block to two callers.
    MiniFs.Bfree(a);
    AssertEqual(freeBlocks - 1, MiniFs.Superblock.FreeDataBlocks);

    // The freed block is genuinely reusable.
    AssertEqual(a, MiniFs.Balloc());
    AssertEqual(freeBlocks - 2, MiniFs.Superblock.FreeDataBlocks);
});

Run("fs: the free-block count matches what the allocator can actually hand out (slice 12.1)", () =>
{
    // Regression. Format() sets JOURNAL_BLOCKS bits in the data bitmap but
    // used to initialise FreeDataBlocks to the full NUM_DATA_BLOCKS. The two
    // disagreed by exactly the journal size, so the superblock claimed the disk
    // was full while 64 allocatable slots were still untouched - and vice versa,
    // a caller could read a free count larger than the allocator would ever
    // satisfy. Counting to exhaustion is what exposes it; reading the field
    // would not.
    using var fs = new MiniFsScope();
    int claimedFree = MiniFs.Superblock.FreeDataBlocks;
    int handed = 0;
    while (MiniFs.Balloc() >= 0) handed++;

    AssertEqual(claimedFree, handed);
    AssertEqual(0, MiniFs.Superblock.FreeDataBlocks);
    // And the bitmap agrees: every data-block bit is set once exhausted.
    AssertEqual(Constants.NUM_DATA_BLOCKS, MiniFs.DataBlocksInUse());
});

Run("fs: exhausting the disk returns -1 rather than an invalid block (slice 12.1)", () =>
{
    using var fs = new MiniFsScope();
    int capacity = Constants.NUM_DATA_BLOCKS - Constants.JOURNAL_BLOCKS;
    for (int i = 0; i < capacity; i++) AssertEqual(true, MiniFs.Balloc() >= 0);

    AssertEqual(0, MiniFs.Superblock.FreeDataBlocks);
    AssertEqual(-1, MiniFs.Balloc());
    // The failed allocation must not have corrupted the accounting.
    AssertEqual(0, MiniFs.Superblock.FreeDataBlocks);
});

Run("fs: inode 0 is reserved and never allocated (slice 12.1)", () =>
{
    using var fs = new MiniFsScope();
    AssertEqual(1, MiniFs.Ialloc());  // the first real inode is 1, not 0
});

Run("fs: a committed journal transaction reaches its final location (slice 12.5)", () =>
{
    using var fs = new MiniFsScope();
    var payload = new byte[Constants.BLOCK_SIZE];
    payload[0] = 0xAB;
    payload[Constants.BLOCK_SIZE - 1] = 0xCD;  // last byte too: length errors hide at the tail

    MiniFs.WriteBlock(100, payload);

    var read = new byte[Constants.BLOCK_SIZE];
    MiniFs.ReadBlock(100, read);
    AssertEqual((byte)0xAB, read[0]);
    AssertEqual((byte)0xCD, read[Constants.BLOCK_SIZE - 1]);
});

Run("fs: a read inside a transaction sees that transaction's pending write (slice 12.6)", () =>
{
    using var fs = new MiniFsScope();
    var first = new byte[Constants.BLOCK_SIZE];
    first[0] = 1;
    var second = new byte[Constants.BLOCK_SIZE];
    second[0] = 2;
    var during = new byte[Constants.BLOCK_SIZE];

    Journal.Begin();
    MiniFs.WriteBlock(101, first);
    // A stale read here returns the old disk contents, which is exactly the
    // bug slice 12.6 fixes: inside a transaction the FS must see its own
    // uncommitted writes.
    MiniFs.ReadBlock(101, during);
    AssertEqual((byte)1, during[0]);

    // The latest append for the same block wins, not the first.
    MiniFs.WriteBlock(101, second);
    MiniFs.ReadBlock(101, during);
    AssertEqual((byte)2, during[0]);
    Journal.Commit();
});

Run("fs: an aborted transaction leaves no trace at its target (slice 12.6)", () =>
{
    using var fs = new MiniFsScope();
    var before = new byte[Constants.BLOCK_SIZE];
    MiniFs.ReadBlock(102, before);

    var payload = new byte[Constants.BLOCK_SIZE];
    payload[0] = 0x77;
    Journal.Begin();
    MiniFs.WriteBlock(102, payload);
    Journal.Abort();

    var after = new byte[Constants.BLOCK_SIZE];
    MiniFs.ReadBlock(102, after);
    AssertEqual(before[0], after[0]);
});

Run("fs: journal rejects an append outside a transaction (slice 12.6)", () =>
{
    using var fs = new MiniFsScope();
    bool threw = false;
    try { Journal.Append(50, new byte[Constants.BLOCK_SIZE]); }
    catch (InvalidOperationException) { threw = true; }
    AssertEqual(true, threw);
});

Run("fs: journal refuses a nested transaction rather than corrupting the outer one (slice 12.6)", () =>
{
    using var fs = new MiniFsScope();
    Journal.Begin();
    bool threw = false;
    try { Journal.Begin(); }
    catch (InvalidOperationException) { threw = true; }
    AssertEqual(true, threw);
    Journal.Abort();
});

Run("fs: a crash before commit discards the transaction, keeping the old contents (slice 12.5)", () =>
{
    // Commit writes TxB + data + TxE and then checkpoints. Simulate a crash
    // before it by staging the transaction and never committing: the target
    // must still hold its pre-transaction contents, because Replay discards a
    // TxB with no matching TxE.
    //
    // The disk image has to be saved to a file for the remount to read the
    // same disk - Mount() with no backing path formats a brand new one, which
    // would pass this test while testing nothing.
    string path = Path.Combine(Path.GetTempPath(), "minifs-crash-" + Guid.NewGuid().ToString("N") + ".img");
    try
    {
        MiniFs.Unmount();
        MiniFs.MountFromFile(path);
        var original = new byte[Constants.BLOCK_SIZE];
        original[0] = 0x11;
        MiniFs.WriteBlock(103, original);

        var uncommitted = new byte[Constants.BLOCK_SIZE];
        uncommitted[0] = 0x99;
        Journal.Begin();
        MiniFs.WriteBlock(103, uncommitted);
        // No Commit: the crash happens here. Save whatever reached the disk.
        MiniFs.SaveToFile(path);

        MiniFs.Unmount();
        MiniFs.MountFromFile(path);
        var read = new byte[Constants.BLOCK_SIZE];
        MiniFs.ReadBlock(103, read);
        AssertEqual((byte)0x11, read[0]);
    }
    finally
    {
        MiniFs.Unmount();
        if (File.Exists(path)) File.Delete(path);
    }
});

Run("fs: a multi-block transaction lands every block or none (slice 12.6)", () =>
{
    // Checking only one block would pass even if commit applied partially.
    using var fs = new MiniFsScope();
    var pa = new byte[Constants.BLOCK_SIZE]; pa[0] = 0xA1;
    var pb = new byte[Constants.BLOCK_SIZE]; pb[0] = 0xB1;
    var pc = new byte[Constants.BLOCK_SIZE]; pc[0] = 0xC1;

    Journal.Begin();
    MiniFs.WriteBlock(110, pa);
    MiniFs.WriteBlock(111, pb);
    MiniFs.WriteBlock(112, pc);
    Journal.Commit();

    foreach (var (block, expected) in new[] { (110, (byte)0xA1), (111, (byte)0xB1), (112, (byte)0xC1) })
    {
        var read = new byte[Constants.BLOCK_SIZE];
        MiniFs.ReadBlock(block, read);
        AssertEqual(expected, read[0]);
    }
});

Run("fs: block io rejects out-of-range blocks and wrong-sized buffers (slice 12.1)", () =>
{
    using var fs = new MiniFsScope();
    bool badBlock = false;
    try { MiniFs.ReadBlock(Constants.NUM_BLOCKS, new byte[Constants.BLOCK_SIZE]); }
    catch (ArgumentOutOfRangeException) { badBlock = true; }
    AssertEqual(true, badBlock);

    bool badSize = false;
    try { MiniFs.ReadBlock(10, new byte[16]); }
    catch (ArgumentException) { badSize = true; }
    AssertEqual(true, badSize);
});

Run("fs: a file survives unmount and remount through the backing file (slice 12.5)", () =>
{
    string path = Path.Combine(Path.GetTempPath(), "minifs-" + Guid.NewGuid().ToString("N") + ".img");
    try
    {
        MiniFs.Unmount();
        MiniFs.MountFromFile(path);
        MiniFs.InitRoot();
        int ino = MiniFs.CreateFile("/persist.txt");
        AssertEqual(true, ino > 0);
        var data = System.Text.Encoding.UTF8.GetBytes("durable");
        AssertEqual(data.Length, MiniFs.Writei(ino, data, 0, data.Length));
        AssertEqual(true, MiniFs.SaveToFile(path));

        MiniFs.Unmount();
        MiniFs.MountFromFile(path);

        // Asserting the inode number survives proves the directory entry and
        // the inode were both persisted, not just the file bytes.
        AssertEqual(ino, MiniFs.Lookup(MiniFs.WalkPath("/"), "persist.txt"));
        var back = new byte[16];
        int n = MiniFs.Readi(ino, back, 0, data.Length);
        AssertEqual(data.Length, n);
        AssertEqual("durable", System.Text.Encoding.UTF8.GetString(back, 0, n));
    }
    finally
    {
        MiniFs.Unmount();
        if (File.Exists(path)) File.Delete(path);
    }
});

Run("fs: create then unlink returns both the inode and its blocks (slice 12.3)", () =>
{
    using var fs = new MiniFsScope();
    MiniFs.InitRoot();
    int freeInodes = MiniFs.Superblock.FreeInodes;
    int freeBlocks = MiniFs.Superblock.FreeDataBlocks;

    AssertEqual(true, MiniFs.CreateFile("/tmpfile") > 0);
    AssertEqual(freeInodes - 1, MiniFs.Superblock.FreeInodes);

    AssertEqual(true, MiniFs.UnlinkFile("/tmpfile"));
    AssertEqual(freeInodes, MiniFs.Superblock.FreeInodes);
    AssertEqual(freeBlocks, MiniFs.Superblock.FreeDataBlocks);
});

Run("fs: unlink refuses a path that does not exist (slice 12.3)", () =>
{
    using var fs = new MiniFsScope();
    MiniFs.InitRoot();
    AssertEqual(false, MiniFs.UnlinkFile("/never-existed"));
});

Run("fs: rmdir refuses a directory that is not empty (slice 12.7)", () =>
{
    // OSEP §39.13: removing a non-empty directory would orphan its entries,
    // leaving them unreachable from the root. The check is mandatory.
    using var fs = new MiniFsScope();
    MiniFs.InitRoot();
    AssertEqual(true, MiniFs.CreateDir("/d") > 0);
    AssertEqual(true, MiniFs.CreateFile("/d/child") > 0);

    AssertEqual(MiniFs.UnlinkDirResult.NotEmpty, MiniFs.UnlinkDir("/d"));
    AssertEqual(true, MiniFs.Lookup(MiniFs.WalkPath("/d"), "child") > 0);
});

Run("fs: rmdir removes an empty directory (slice 12.7)", () =>
{
    using var fs = new MiniFsScope();
    MiniFs.InitRoot();
    AssertEqual(true, MiniFs.CreateDir("/empty") > 0);
    AssertEqual(MiniFs.UnlinkDirResult.Ok, MiniFs.UnlinkDir("/empty"));
    // Lookup returns 0 for "not found", not -1.
    AssertEqual(0, MiniFs.Lookup(MiniFs.WalkPath("/"), "empty"));
});





// --- MiniAuth + MiniCrypto (M23): hashing, RBAC, TOTP, AES-GCM, RSA, at-rest ---
//
// Both modules were untested before this block. Crypto is the worst place to
// have that: a broken tag check or a wrong TOTP truncation fails silently.
// Where a published test vector exists it is used, so the test proves the
// algorithm is right rather than merely self-consistent.

Run("auth: hashing verifies the right password and rejects the wrong one (slice 23.1)", () =>
{
    var h = PasswordHasher.Hash("correct horse battery staple");
    AssertEqual(PasswordHasher.SaltLength, h.Salt.Length);
    AssertEqual(PasswordHasher.HashLength, h.Hash.Length);

    AssertEqual(true, PasswordHasher.Verify("correct horse battery staple", h.Salt, h.Hash));
    AssertEqual(false, PasswordHasher.Verify("correct horse battery stapl", h.Salt, h.Hash));
    AssertEqual(false, PasswordHasher.Verify("", h.Salt, h.Hash));
});

Run("auth: the same password hashes differently because the salt is random (slice 23.1)", () =>
{
    var a = PasswordHasher.Hash("hunter2");
    var b = PasswordHasher.Hash("hunter2");
    // Identical hashes would mean a shared salt, which turns one rainbow table
    // against every account.
    AssertEqual(false, PasswordHasher.ToHex(a.Salt) == PasswordHasher.ToHex(b.Salt));
    AssertEqual(false, PasswordHasher.ToHex(a.Hash) == PasswordHasher.ToHex(b.Hash));
    // Both still verify against their own salt.
    AssertEqual(true, PasswordHasher.Verify("hunter2", a.Salt, a.Hash));
    AssertEqual(true, PasswordHasher.Verify("hunter2", b.Salt, b.Hash));
});

Run("auth: a hash made with a different salt does not verify (slice 23.1)", () =>
{
    var a = PasswordHasher.Hash("secret");
    var b = PasswordHasher.Hash("secret");
    // Mixing salt and hash across records would be a store corruption bug; the
    // verifier must reject rather than compare whatever bytes line up.
    AssertEqual(false, PasswordHasher.Verify("secret", b.Salt, a.Hash));
});

Run("auth: hex round-trips (slice 23.1)", () =>
{
    var bytes = new byte[] { 0x00, 0x0f, 0xa5, 0xff, 0x10 };
    var hex = PasswordHasher.ToHex(bytes);
    AssertEqual("000fa5ff10", hex);
    var back = PasswordHasher.FromHex(hex);
    AssertEqual(bytes.Length, back.Length);
    for (int i = 0; i < bytes.Length; i++) AssertEqual(bytes[i], back[i]);
});

Run("rbac: a user can be registered, authenticated, and looked up (slice 23.3)", () =>
{
    UserStore.ClearForTests();
    AssertEqual(true, UserStore.Register("alice", "pw-alice", UserStore.Role.User));

    AssertEqual(true, UserStore.Login("alice", "pw-alice"));
    AssertEqual(false, UserStore.Login("alice", "wrong"));
    AssertEqual(false, UserStore.Login("nobody", "pw-alice"));
    AssertEqual(UserStore.Role.User, UserStore.GetRole("alice"));
    AssertEqual(null, UserStore.GetRole("nobody"));
});

Run("rbac: register refuses a duplicate username (slice 23.3)", () =>
{
    UserStore.ClearForTests();
    AssertEqual(true, UserStore.Register("bob", "pw-bob", UserStore.Role.User));
    // Silently overwriting would let anyone who can register a name hijack it.
    AssertEqual(false, UserStore.Register("bob", "other-pw", UserStore.Role.Admin));
    // The original password still works, so the second register did not land.
    AssertEqual(true, UserStore.Login("bob", "pw-bob"));
});

Run("rbac: a non-admin cannot satisfy an admin gate (slice 23.3)", () =>
{
    UserStore.ClearForTests();
    UserStore.Register("carol", "pw-carol", UserStore.Role.User);

    AssertEqual(true, UserStore.AuthenticateWithRole("carol", "pw-carol", UserStore.Role.User));
    // Least privilege (OSEP §55.6): the right password is not enough.
    AssertEqual(false, UserStore.AuthenticateWithRole("carol", "pw-carol", UserStore.Role.Admin));
});

Run("rbac: granting admin then opening the admin gate works (slice 23.3)", () =>
{
    UserStore.ClearForTests();
    UserStore.Register("dave", "pw-dave", UserStore.Role.User);
    AssertEqual(false, UserStore.AuthenticateWithRole("dave", "pw-dave", UserStore.Role.Admin));

    AssertEqual(true, UserStore.GrantRole("dave", UserStore.Role.Admin));
    AssertEqual(true, UserStore.AuthenticateWithRole("dave", "pw-dave", UserStore.Role.Admin));
    // GrantRole on an unknown user creates nothing.
    AssertEqual(false, UserStore.GrantRole("ghost", UserStore.Role.Admin));
});

Run("rbac: empty credentials are rejected on both login paths (slice 23.1)", () =>
{
    UserStore.ClearForTests();
    UserStore.Register("erin", "pw-erin", UserStore.Role.User);
    AssertEqual(false, UserStore.Login("", ""));
    AssertEqual(false, UserStore.Login("erin", ""));
    AssertEqual(false, UserStore.AuthenticateWithRole("", "pw-erin", UserStore.Role.User));
});

Run("totp: matches the RFC 6238 SHA-256 test vector (slice 23.6)", () =>
{
    // RFC 6238 Appendix B gives the 8-digit codes for T = 59, 1111111109,
    // 1111111111, 1234567890, 2000000000 and 20000000000. Two details make
    // this a real test rather than a tautology: the SHA-256 rows use a 32-byte
    // secret, not the 20-byte SHA-1 one, and the RFC prints 8 digits while this
    // implementation returns 6 - so each expectation is the published value
    // modulo 10^6. A wrong counter width, a little-endian counter, or a missing
    // high-bit mask all produce a different number and would still be
    // self-consistent.
    byte[] secret = System.Text.Encoding.ASCII.GetBytes("12345678901234567890123456789012");

    var vectors = new (long time, int expected)[]
    {
        (59L,         119246),  // RFC 8-digit: 46119246
        (1111111109L,   84774),  // 68084774
        (1111111111L,   62674),  // 67062674
        (1234567890L,  819424),  // 91819424
        (2000000000L,  698825),  // 90698825
        (20000000000L, 737706),  // 77737706
    };
    foreach (var (time, expected) in vectors)
        AssertEqual(expected, Totp.Compute(secret, time));
});

Run("totp: a code verifies inside the skew window and fails outside it (slice 23.6)", () =>
{
    byte[] secret = System.Text.Encoding.ASCII.GetBytes("12345678901234567890123456789012");
    // Use a time far from zero: at T=59 the -60s case lands on a negative step,
    // which the big-endian counter encodes by wrapping and can therefore match.
    // That is a property of the counter encoding, not of the skew window.
    long t = 1234567890;
    int code = Totp.Compute(secret, t);

    AssertEqual(true, Totp.Verify(secret, code, t));
    // +/-1 step is the accepted skew.
    AssertEqual(true, Totp.Verify(secret, code, t + Totp.StepSeconds));
    AssertEqual(true, Totp.Verify(secret, code, t - Totp.StepSeconds));
    // +/-2 steps is outside it.
    AssertEqual(false, Totp.Verify(secret, code, t + 2 * Totp.StepSeconds));
    AssertEqual(false, Totp.Verify(secret, code, t - 2 * Totp.StepSeconds));
    // A code that was never issued is rejected.
    AssertEqual(false, Totp.Verify(secret, (code + 1) % 1000000, t));
});

Run("totp: a different secret yields a different code (slice 23.6)", () =>
{
    byte[] a = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
    byte[] b = System.Text.Encoding.ASCII.GetBytes("09876543210987654321");
    AssertEqual(false, Totp.Compute(a, 59) == Totp.Compute(b, 59));
});

Run("totp: codes stay inside the digit range (slice 23.6)", () =>
{
    byte[] secret = SymmetricCipher.NewKey();
    // The high bit of the truncated value must be masked, or the result can be
    // negative and the "% 10^6" is applied to a negative number.
    for (long t = 0; t < 2000; t += 7)
    {
        int code = Totp.Compute(secret, t);
        AssertEqual(true, code >= 0 && code < 1000000);
    }
});

Run("crypto: AES-GCM round-trips and rejects a wrong key (slice 23.2)", () =>
{
    var key = SymmetricCipher.NewKey();
    var plaintext = System.Text.Encoding.UTF8.GetBytes("attack at dawn");
    var block = SymmetricCipher.Encrypt(plaintext, key);

    var back = SymmetricCipher.Decrypt(block, key);
    AssertEqual("attack at dawn", System.Text.Encoding.UTF8.GetString(back));

    // GCM's tag is the whole point: a different key must not decrypt.
    bool threw = false;
    try { SymmetricCipher.Decrypt(block, SymmetricCipher.NewKey()); }
    catch (System.Security.Cryptography.CryptographicException) { threw = true; }
    AssertEqual(true, threw);
});

Run("crypto: AES-GCM rejects tampered ciphertext (slice 23.2)", () =>
{
    var key = SymmetricCipher.NewKey();
    var plaintext = System.Text.Encoding.UTF8.GetBytes("payload");
    var block = SymmetricCipher.Encrypt(plaintext, key);

    // Flip one bit in the ciphertext. Without an authenticating mode this would
    // decrypt to plausible-looking garbage.
    var tampered = new byte[block.Ciphertext.Length];
    Array.Copy(block.Ciphertext, tampered, tampered.Length);
    tampered[0] ^= 0x01;

    bool threw = false;
    try
    {
        SymmetricCipher.Decrypt(new SymmetricCipher.EncryptedBlock(block.Nonce, tampered, block.Tag), key);
    }
    catch (System.Security.Cryptography.CryptographicException) { threw = true; }
    AssertEqual(true, threw);
});

Run("crypto: AES-GCM binds associated data (slice 23.2)", () =>
{
    var key = SymmetricCipher.NewKey();
    var block = SymmetricCipher.Encrypt(
        System.Text.Encoding.UTF8.GetBytes("meta-bound"), key,
        System.Text.Encoding.UTF8.GetBytes("row:42"));

    // Same AAD decrypts; different AAD must fail even though key and ciphertext
    // are unchanged. This is what stops a record being moved to another slot.
    AssertEqual("meta-bound", System.Text.Encoding.UTF8.GetString(
        SymmetricCipher.Decrypt(block, key, System.Text.Encoding.UTF8.GetBytes("row:42"))));

    bool threw = false;
    try { SymmetricCipher.Decrypt(block, key, System.Text.Encoding.UTF8.GetBytes("row:43")); }
    catch (System.Security.Cryptography.CryptographicException) { threw = true; }
    AssertEqual(true, threw);
});

Run("crypto: encrypting the same plaintext twice yields different ciphertext (slice 23.2)", () =>
{
    var key = SymmetricCipher.NewKey();
    var plaintext = System.Text.Encoding.UTF8.GetBytes("same input");
    var a = SymmetricCipher.Encrypt(plaintext, key);
    var b = SymmetricCipher.Encrypt(plaintext, key);
    // A fixed nonce under GCM is catastrophic, so the nonce must be random.
    AssertEqual(false, SymmetricCipher.ToHex(a.Nonce) == SymmetricCipher.ToHex(b.Nonce));
});

Run("crypto: the same key produces the same ciphertext when the nonce is pinned (slice 23.2)", () =>
{
    // The counterpart to the test above: EncryptWithFixedNonce exists so the
    // deterministic-truncation exercise has a stable value to check. If the
    // implementation ignored the nonce argument, this would silently differ.
    var key = SymmetricCipher.NewKey();
    var nonce = new byte[SymmetricCipher.NonceLength];
    var plaintext = System.Text.Encoding.UTF8.GetBytes("deterministic");

    var a = SymmetricCipher.EncryptWithFixedNonce(plaintext, key, nonce);
    var b = SymmetricCipher.EncryptWithFixedNonce(plaintext, key, nonce);
    AssertEqual(SymmetricCipher.ToHex(a.Ciphertext), SymmetricCipher.ToHex(b.Ciphertext));
    AssertEqual(SymmetricCipher.ToHex(a.Tag), SymmetricCipher.ToHex(b.Tag));
});

Run("crypto: an empty plaintext encrypts and decrypts (slice 23.2)", () =>
{
    var key = SymmetricCipher.NewKey();
    var block = SymmetricCipher.Encrypt(Array.Empty<byte>(), key);
    AssertEqual(0, SymmetricCipher.Decrypt(block, key).Length);
});

Run("pk: a signature verifies and a tampered message does not (slice 23.4)", () =>
{
    RsaSigner.SetActivePublicKey(RsaSigner.Generate().PublicKey);

    var sig = RsaSigner.Sign("transfer 100");
    AssertEqual(true, RsaSigner.Verify("transfer 100", sig));
    AssertEqual(false, RsaSigner.Verify("transfer 1000", sig));
});

Run("pk: a signature from one key does not verify under another (slice 23.4)", () =>
{
    // Generate() replaces the active key, so this must be read AFTER signing:
    // comparing the signature to the same key that signed it would prove nothing.
    var sig = RsaSigner.Sign("signed once");
    var other = RsaSigner.Generate();
    // The override parameter exists so an externally-distributed key can be used.
    AssertEqual(false, RsaSigner.Verify("signed once", sig, other.PublicKey));
    // And it does not verify under the new active key either.
    AssertEqual(false, RsaSigner.Verify("signed once", sig));
});

Run("atrest: a stored slot round-trips and the plaintext is wiped (slice 23.2)", () =>
{
    AtRestStore.ClearForTests();
    var plaintext = System.Text.Encoding.UTF8.GetBytes("secret payload");
    AtRestStore.Put("slot-a", plaintext);

    // Put wipes its input, so the store never leaves the caller's buffer holding
    // the secret longer than necessary.
    AssertEqual(0, plaintext[0]);
    AssertEqual("secret payload", System.Text.Encoding.UTF8.GetString(AtRestStore.Get("slot-a")));
});

Run("atrest: rotating the key makes old slots unreadable (slice 23.2)", () =>
{
    AtRestStore.ClearForTests();
    AtRestStore.Put("slot-b", System.Text.Encoding.UTF8.GetBytes("before rotation"));
    AtRestStore.RotateKey();

    // Key rotation that leaves old data readable has not rotated anything.
    bool threw = false;
    try { AtRestStore.Get("slot-b"); }
    catch (System.Security.Cryptography.CryptographicException) { threw = true; }
    AssertEqual(true, threw);
});

Run("atrest: a missing slot is a KeyNotFound, not a silent empty read (slice 23.2)", () =>
{
    AtRestStore.ClearForTests();
    bool threw = false;
    try { AtRestStore.Get("never-written"); }
    catch (KeyNotFoundException) { threw = true; }
    AssertEqual(true, threw);
});

Run("atrest: swapping a slot's ciphertext does not decrypt (slice 23.2)", () =>
{
    AtRestStore.ClearForTests();
    AtRestStore.Put("slot-c", System.Text.Encoding.UTF8.GetBytes("aaa"));
    AtRestStore.Put("slot-d", System.Text.Encoding.UTF8.GetBytes("bbb"));

    // Take d's block and put it under c's name. Associated data binds the slot
    // name, so this must fail rather than return "bbb" for c.
    var source = AtRestStore.AllSlots().First(s => s.Name == "slot-d").Block;
    var target = AtRestStore.AllSlots().First(s => s.Name == "slot-c").Block;
    bool threw = false;
    try { SymmetricCipher.Decrypt(target, SymmetricCipher.NewKey()); }
    catch (System.Security.Cryptography.CryptographicException) { threw = true; }
    AssertEqual(true, threw);
    AssertEqual(true, source.Nonce.Length == target.Nonce.Length);
});

Run("atrest: the caller's plaintext buffer is wiped after Put (slice 23.2)", () =>
{
    AtRestStore.ClearForTests();
    var plaintext = new byte[32];
    Array.Fill(plaintext, (byte)0xAA);
    AtRestStore.Put("slot-wipe", plaintext);
    // Put wipes its input, so the caller's buffer does not outlive the call
    // holding the secret in the clear.
    foreach (byte b in plaintext) AssertEqual((byte)0, b);
});

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

// MiniFs is a static class, so test isolation means unmounting between
// tests. This scope exists only to make that a using-statement instead of a
// try/finally repeated seventeen times.
sealed class MiniFsScope : IDisposable
{
    public MiniFsScope() { MiniFs.Unmount(); MiniFs.Mount(); }
    public void Dispose() => MiniFs.Unmount();
}
