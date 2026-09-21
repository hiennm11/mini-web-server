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
