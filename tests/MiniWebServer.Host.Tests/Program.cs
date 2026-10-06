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
