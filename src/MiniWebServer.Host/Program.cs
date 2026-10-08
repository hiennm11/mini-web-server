using System.Net;
using System.Net.Sockets;
using System.Text;

const int Port = 8080;
const int ListenBacklog = 128;
const int MaxRequestBytes = 1_024 * 1_024;
const int RaceIterations = 1_000_000;
const int WorkerCount = 8;

bool asyncMode = args.Contains("--async");

int? maxThreads = null;
int? minThreads = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--max-threads" && i + 1 < args.Length && int.TryParse(args[++i], out var mx))
    {
        maxThreads = mx;
    }
    else if (args[i] == "--min-threads" && i + 1 < args.Length && int.TryParse(args[++i], out var mn))
    {
        minThreads = mn;
    }
}

if (maxThreads.HasValue || minThreads.HasValue)
{
    ThreadPool.GetMinThreads(out var curMin, out var curIo);
    ThreadPool.GetMaxThreads(out var curMax, out var curIoMax);
    int newMin = minThreads ?? curMin;
    int newMax = maxThreads ?? curMax;
    bool okMin = ThreadPool.SetMinThreads(newMin, curIo);
    bool okMax = ThreadPool.SetMaxThreads(newMax, curIoMax);
    if (!okMin || !okMax)
    {
        Console.Error.WriteLine($"[threadpool] failed to set min={newMin} max={newMax}");
    }
    ThreadPool.GetMinThreads(out var appliedMin, out _);
    ThreadPool.GetMaxThreads(out var appliedMax, out _);
    Console.WriteLine($"[threadpool] cap applied: min={appliedMin} max={appliedMax} worker threads (was min={curMin} max={curMax})");
}

string webRoot = WebRootLocator.GetWebRoot(AppContext.BaseDirectory);

string minifsImagePath = Environment.GetEnvironmentVariable("MINIFS_IMAGE") ?? "minifs.img";
MiniWebServer.Host.MiniFs.MiniFs.MountFromFile(minifsImagePath);
MiniWebServer.Host.MiniFs.MiniFs.InitRoot();
Console.WriteLine($"[minifs] mounted (image={minifsImagePath}, exists={File.Exists(minifsImagePath)}): {MiniWebServer.Host.MiniFs.MiniFs.Superblock.NumDataBlocks} data blocks free of {MiniWebServer.Host.MiniFs.MiniFs.Superblock.TotalBlocks} total blocks");

if (asyncMode)
{
    Console.WriteLine($"Host process id: {Environment.ProcessId}");
    Console.WriteLine($"[mode] async/event-based (OSEP Ch. 33)");
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    await AsyncServer.RunAsync(Port, webRoot, cts.Token);
    return;
}

using Socket serverSocket = new(
    AddressFamily.InterNetwork,
    SocketType.Stream,
    ProtocolType.Tcp);

IPEndPoint endpoint = new(IPAddress.Any, Port);

serverSocket.Bind(endpoint);
serverSocket.Listen(ListenBacklog);

Console.WriteLine($"Host process id: {Environment.ProcessId}");
Console.WriteLine($"Server socket listening on http://localhost:{Port}/");
Console.WriteLine($"[mode] worker-pool bounded ({WorkerCount} threads)");
Console.WriteLine("Accept loop is running. Press Ctrl+C to stop.");

WorkerPool.ClientHandler = HandleClient;
WorkerPool.Start(WorkerCount, webRoot);

while (true)
{
    Socket clientSocket = serverSocket.Accept();
    if (!WorkerPool.TryEnqueue(clientSocket))
    {
        // Queue is at capacity. Reply 503 and close so the accept loop
        // keeps draining the kernel backlog instead of parking.
        Console.WriteLine($"[accept] Rejecting connection from {clientSocket.RemoteEndPoint}: queue full");
        var busy = new HttpResponse(
            503,
            "Service Unavailable",
            "text/plain; charset=UTF-8",
            Encoding.UTF8.GetBytes("Server is at capacity; retry later.\n"));
        try
        {
            // Drain a small prefix of the request so Dispose() doesn't RST
            // the client (Windows sends RST if a socket is closed with unread
            // data in the receive buffer). Best-effort, short timeout.
            clientSocket.ReceiveTimeout = 50;
            var drain = new byte[256];
            try { while (clientSocket.Receive(drain) > 0) { } } catch { }
            clientSocket.SendTimeout = 5000;
            SendAll(clientSocket, busy.ToBytes());
        }
        catch
        {
            // best-effort: client may have disconnected already
        }
        clientSocket.Dispose();
    }
}

static void HandleClient(Socket clientSocket, string webRoot)
{
    using (clientSocket)
    {
        int threadId = Thread.CurrentThread.ManagedThreadId;

        // Per-thread stack value. Only this handler thread can see it.
        int localRequestId = Interlocked.Increment(ref RequestStats.TotalRequests);

        Console.WriteLine();
        Console.WriteLine($"[thread {threadId}] Accepted client socket from {clientSocket.RemoteEndPoint}");
        Console.WriteLine($"[thread {threadId}] Local request id: {localRequestId}, shared total seen so far: {RequestStats.TotalRequests}");

        byte[] requestBytes = ReceiveRequest(clientSocket);
        string request = Encoding.UTF8.GetString(requestBytes);
        Console.WriteLine($"[thread {threadId}] Raw HTTP request bytes decoded as UTF-8:");
        Console.WriteLine(request);

        HttpRequest parsedRequest = HttpRequestParser.Parse(request);
        Console.WriteLine($"[thread {threadId}] Parsed HTTP request:");
        Console.WriteLine($"[thread {threadId}] Method: {parsedRequest.Method}");
        Console.WriteLine($"[thread {threadId}] Path: {parsedRequest.Path}");
        Console.WriteLine($"[thread {threadId}] Version: {parsedRequest.Version}");
        Console.WriteLine($"[thread {threadId}] Headers: {parsedRequest.Headers.Count}");

        if (parsedRequest.Path == "/slow")
        {
            Console.WriteLine($"[thread {threadId}] Sleeping 30000 ms to simulate blocking I/O...");
            Thread.Sleep(30000);
        }

        HttpResponse response;
        if (parsedRequest.Path == "/race")
        {
            Console.WriteLine($"[thread {threadId}] Running {RaceIterations} non-atomic increments on RequestStats.UnsafeCounter...");
            for (int i = 0; i < RaceIterations; i++)
            {
                RequestStats.UnsafeCounter++;
            }
            int observed = RequestStats.UnsafeCounter;
            Console.WriteLine($"[thread {threadId}] /race done. UnsafeCounter is now {observed}");
            response = new HttpResponse(
                200,
                "OK",
                "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes($"UnsafeCounter = {observed}\n"));
        }
        else if (parsedRequest.Path == "/race-safe")
        {
            Console.WriteLine($"[thread {threadId}] Running {RaceIterations} increments on RequestStats.SafeCounter under lock...");
            int observed;
            lock (RequestStats.SafeCounterLock)
            {
                for (int i = 0; i < RaceIterations; i++)
                {
                    RequestStats.SafeCounter++;
                }
                observed = RequestStats.SafeCounter;
            }
            Console.WriteLine($"[thread {threadId}] /race-safe done. SafeCounter is now {observed}");
            response = new HttpResponse(
                200,
                "OK",
                "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes($"SafeCounter = {observed}\n"));
        }
        else if (parsedRequest.Path == "/stats")
        {
            var p = System.Diagnostics.Process.GetCurrentProcess();
            int threads = p.Threads.Count;
            long workingSet = p.WorkingSet64;
            long privateBytes = p.PrivateMemorySize64;
            ThreadPool.GetMinThreads(out var tpMin, out _);
            ThreadPool.GetMaxThreads(out var tpMax, out _);
            ThreadPool.GetAvailableThreads(out var tpAvail, out _);
            int tpActive = tpMax - tpAvail;
            string body =
                $"threads = {threads}\n" +
                $"working_set_bytes = {workingSet}\n" +
                $"private_bytes = {privateBytes}\n" +
                $"threadpool_min = {tpMin}\n" +
                $"threadpool_max = {tpMax}\n" +
                $"threadpool_active = {tpActive}\n" +
                $"total_requests = {RequestStats.TotalRequests}\n";
            response = new HttpResponse(
                200,
                "OK",
                "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes(body));
        }
        else if (parsedRequest.Path == "/stats-fast")
        {
            var (t, ws, pb) = RequestStatsCache.Read();
            string body =
                $"threads = {t}\n" +
                $"working_set_bytes = {ws}\n" +
                $"private_bytes = {pb}\n" +
                $"total_requests = {RequestStats.TotalRequests}\n";
            response = new HttpResponse(
                200,
                "OK",
                "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes(body));
        }
        else if (parsedRequest.Path == "/stats-refresh")
        {
            RequestStatsCache.Refresh();
            response = new HttpResponse(
                200,
                "OK",
                "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes("refreshed\n"));
        }
        else if (parsedRequest.Path == "/fs-stats")
        {
            var sb = MiniWebServer.Host.MiniFs.MiniFs.Superblock;
            string body =
                $"magic = 0x{sb.Magic:X8}\n" +
                $"total_inodes = {sb.TotalInodes}\n" +
                $"total_blocks = {sb.TotalBlocks}\n" +
                $"free_inodes = {sb.FreeInodes}\n" +
                $"free_data_blocks = {sb.FreeDataBlocks}\n" +
                $"inodes_in_use = {MiniWebServer.Host.MiniFs.MiniFs.InodesInUse()}\n" +
                $"data_blocks_in_use = {MiniWebServer.Host.MiniFs.MiniFs.DataBlocksInUse()}\n" +
                $"inode_bitmap_block = {sb.InodeBitmapBlock}\n" +
                $"data_bitmap_block = {sb.DataBitmapBlock}\n" +
                $"inode_table_start = {sb.InodeTableStart}\n" +
                $"data_blocks_start = {sb.DataBlocksStart}\n" +
                $"num_data_blocks = {sb.NumDataBlocks}\n" +
                $"disk_size_bytes = {MiniWebServer.Host.MiniFs.MiniFs.DiskSizeBytes}\n" +
                $"block_size = {MiniWebServer.Host.MiniFs.Constants.BLOCK_SIZE}\n" +
                $"mounted = {MiniWebServer.Host.MiniFs.MiniFs.IsMounted}\n";
            response = new HttpResponse(
                200,
                "OK",
                "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes(body));
        }
        else if (parsedRequest.Path.StartsWith("/fs/list"))
        {
            string param = "";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && kv.Substring(0, eq) == "path") { param = Uri.UnescapeDataString(kv.Substring(eq + 1)); break; }
                }
            }
            string listPath = string.IsNullOrEmpty(param) ? "/" : param;
            int dirIno = MiniWebServer.Host.MiniFs.MiniFs.WalkPath(listPath);
            if (dirIno == 0)
            {
                response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("Not Found\n"));
            }
            else
            {
                var entries = MiniWebServer.Host.MiniFs.MiniFs.Readdir(dirIno);
                var sb = new StringBuilder();
                sb.AppendLine($"path: {listPath}");
                sb.AppendLine($"entries: {entries.Length}");
                foreach (var (name, ino) in entries)
                {
                    var inode = MiniWebServer.Host.MiniFs.MiniFs.Iget(ino);
                    string typeStr = inode.Type switch
                    {
                        MiniWebServer.Host.MiniFs.Inode.TYPE_FILE => "file",
                        MiniWebServer.Host.MiniFs.Inode.TYPE_DIR => "dir",
                        _ => "free"
                    };
                    sb.AppendLine($"  ino={ino,4}  type={typeStr,-4}  size={inode.Size,8}  name={name}");
                }
                response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(sb.ToString()));
            }
        }
        else if (parsedRequest.Path.StartsWith("/fs/stat"))
        {
            string param = "";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && kv.Substring(0, eq) == "path") { param = Uri.UnescapeDataString(kv.Substring(eq + 1)); break; }
                }
            }
            int ino = MiniWebServer.Host.MiniFs.MiniFs.WalkPath(param);
            if (ino == 0)
            {
                response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("Not Found\n"));
            }
            else
            {
                var inode = MiniWebServer.Host.MiniFs.MiniFs.Iget(ino);
                string typeStr = inode.Type switch
                {
                    MiniWebServer.Host.MiniFs.Inode.TYPE_FILE => "file",
                    MiniWebServer.Host.MiniFs.Inode.TYPE_DIR => "dir",
                    _ => "free"
                };
                string body = $"ino = {ino}\ntype = {typeStr}\nsize = {inode.Size}\nnlink = {inode.Nlink}\ndirect_blocks = ";
                for (int i = 0; i < MiniWebServer.Host.MiniFs.Constants.NDIRECT; i++)
                    body += (inode.DirectBlocks[i] >= 0 ? inode.DirectBlocks[i].ToString() : "-") + (i < MiniWebServer.Host.MiniFs.Constants.NDIRECT - 1 ? "," : "");
                body += "\n";
                response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(body));
            }
        }
        else if (parsedRequest.Path.StartsWith("/fs/create"))
        {
            string param = "";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && kv.Substring(0, eq) == "path") { param = Uri.UnescapeDataString(kv.Substring(eq + 1)); break; }
                }
            }
            if (string.IsNullOrEmpty(param))
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("path required\n"));
            }
            else
            {
                int ino = MiniWebServer.Host.MiniFs.MiniFs.CreateFile(param);
                if (ino < 0) response = new HttpResponse(500, "Internal Server Error", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("create failed\n"));
                else response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes($"created ino={ino}\n"));
            }
        }
        else if (parsedRequest.Path.StartsWith("/fs/write"))
        {
            // POST body is the content; ?path= sets the path
            string param = "";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && kv.Substring(0, eq) == "path") { param = Uri.UnescapeDataString(kv.Substring(eq + 1)); break; }
                }
            }
            if (string.IsNullOrEmpty(param))
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("path required\n"));
            }
            else
            {
                int ino = MiniWebServer.Host.MiniFs.MiniFs.WalkPath(param);
                if (ino == 0)
                {
                    response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("Not Found\n"));
                }
                else
                {
                    var inode = MiniWebServer.Host.MiniFs.MiniFs.Iget(ino);
                    if (inode.Type != MiniWebServer.Host.MiniFs.Inode.TYPE_FILE)
                    {
                        response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("not a file\n"));
                    }
                    else
                    {
                        // Body starts after \r\n\r\n
                        int hdrEnd = HttpRequestReceiver.FindHeaderEnd(requestBytes);
                        if (hdrEnd < 0)
                        {
                            response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("missing header terminator\n"));
                        }
                        else
                        {
                            int bodyOff = hdrEnd + 4;
                            int bodyLen = requestBytes.Length - bodyOff;
                            // Copy body to its own buffer so Writei's offset param
                            // means "file offset" not "buffer offset"
                            var body = new byte[bodyLen];
                            Array.Copy(requestBytes, bodyOff, body, 0, bodyLen);
                            int n = MiniWebServer.Host.MiniFs.MiniFs.Writei(ino, body, 0, bodyLen);
                            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes($"wrote {n} bytes\n"));
                        }
                    }
                }
            }
        }
        else if (parsedRequest.Path.StartsWith("/fs/read"))
        {
            string param = "";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && kv.Substring(0, eq) == "path") { param = Uri.UnescapeDataString(kv.Substring(eq + 1)); break; }
                }
            }
            int ino = MiniWebServer.Host.MiniFs.MiniFs.WalkPath(param);
            if (ino == 0)
            {
                response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("Not Found\n"));
            }
            else
            {
                var inode = MiniWebServer.Host.MiniFs.MiniFs.Iget(ino);
                if (inode.Type != MiniWebServer.Host.MiniFs.Inode.TYPE_FILE)
                {
                    response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("not a file\n"));
                }
                else
                {
                    var buf = new byte[inode.Size];
                    int n = MiniWebServer.Host.MiniFs.MiniFs.Readi(ino, buf, 0, buf.Length);
                    response = new HttpResponse(200, "OK", "application/octet-stream", new ReadOnlySpan<byte>(buf, 0, n).ToArray());
                }
            }
        }
        else if (parsedRequest.Path.StartsWith("/fs/unlink"))
        {
            string param = "";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && kv.Substring(0, eq) == "path") { param = Uri.UnescapeDataString(kv.Substring(eq + 1)); break; }
                }
            }
            if (string.IsNullOrEmpty(param))
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("path required\n"));
            }
            else
            {
                bool ok = MiniWebServer.Host.MiniFs.MiniFs.UnlinkFile(param);
                if (!ok) response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("not found or not a file\n"));
                else response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("unlinked\n"));
            }
        }
        else if (parsedRequest.Path.StartsWith("/fs/mkdir"))
        {
            string param = "";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && kv.Substring(0, eq) == "path") { param = Uri.UnescapeDataString(kv.Substring(eq + 1)); break; }
                }
            }
            if (string.IsNullOrEmpty(param))
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("path required\n"));
            }
            else
            {
                int ino = MiniWebServer.Host.MiniFs.MiniFs.CreateDir(param);
                if (ino < 0) response = new HttpResponse(500, "Internal Server Error", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("mkdir failed\n"));
                else response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes($"mkdir ino={ino}\n"));
            }
        }
        else if (parsedRequest.Path.StartsWith("/fs/rmdir"))
        {
            string param = "";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && kv.Substring(0, eq) == "path") { param = Uri.UnescapeDataString(kv.Substring(eq + 1)); break; }
                }
            }
            if (string.IsNullOrEmpty(param))
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("path required\n"));
            }
            else
            {
                var result = MiniWebServer.Host.MiniFs.MiniFs.UnlinkDir(param);
                switch (result)
                {
                    case MiniWebServer.Host.MiniFs.MiniFs.UnlinkDirResult.Ok:
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("rmdir ok\n"));
                        break;
                    case MiniWebServer.Host.MiniFs.MiniFs.UnlinkDirResult.NotFound:
                        response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("not found\n"));
                        break;
                    case MiniWebServer.Host.MiniFs.MiniFs.UnlinkDirResult.NotADirectory:
                        response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("not a directory\n"));
                        break;
                    case MiniWebServer.Host.MiniFs.MiniFs.UnlinkDirResult.NotEmpty:
                        response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("directory not empty\n"));
                        break;
                    case MiniWebServer.Host.MiniFs.MiniFs.UnlinkDirResult.InvalidName:
                        response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("invalid name\n"));
                        break;
                    default:
                        response = new HttpResponse(500, "Internal Server Error", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("rmdir failed\n"));
                        break;
                }
            }
        }
        else if (parsedRequest.Path.StartsWith("/fs-save"))
        {
            string param = "";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && kv.Substring(0, eq) == "path") { param = Uri.UnescapeDataString(kv.Substring(eq + 1)); break; }
                }
            }
            if (string.IsNullOrEmpty(param))
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("path required\n"));
            }
            else
            {
                bool ok = MiniWebServer.Host.MiniFs.MiniFs.SaveToFile(param);
                if (!ok) response = new HttpResponse(500, "Internal Server Error", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("save failed (not mounted?)\n"));
                else response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes($"saved to {param}\n"));
            }
        }
        else if (parsedRequest.Path.StartsWith("/fs-inject-orphan-multi"))
        {
            // DEBUG route: write a fake MULTI-BLOCK TxB (slice 12.6
            // format) into the journal with two blockNo entries but no
            // matching DATA or TxE. Replay should discard it.
            var blk = new byte[4096];
            BitConverter.GetBytes(0xAABBCCDDu).CopyTo(blk, 0);  // TXB_MAGIC
            BitConverter.GetBytes(1234).CopyTo(blk, 4);          // fake TID
            BitConverter.GetBytes(2).CopyTo(blk, 8);             // count = 2
            BitConverter.GetBytes(100).CopyTo(blk, 12);          // blockNo 1
            BitConverter.GetBytes(101).CopyTo(blk, 16);          // blockNo 2
            MiniWebServer.Host.MiniFs.MiniFs.WriteBlockNoLog(8, blk);
            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("orphan-multi-txb-injected\n"));
        }
        else if (parsedRequest.Path.StartsWith("/fs-inject-orphan"))
        {
            // DEBUG route: write a fake single-block TxB into the journal
            // with no matching TxE. After save + restart + replay, this
            // should be silently discarded.
            var blk = new byte[4096];
            BitConverter.GetBytes(0xAABBCCDDu).CopyTo(blk, 0);  // TXB_MAGIC
            BitConverter.GetBytes(999).CopyTo(blk, 4);           // fake TID
            BitConverter.GetBytes(1).CopyTo(blk, 8);            // count = 1
            BitConverter.GetBytes(42).CopyTo(blk, 12);           // fake blockNo
            MiniWebServer.Host.MiniFs.MiniFs.WriteBlockNoLog(8, blk);
            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("orphan-txb-injected\n"));
        }
        else if (parsedRequest.Path.StartsWith("/fs-inject-orphan-multi"))
        {
            // DEBUG route: write a fake MULTI-BLOCK TxB (slice 12.6
            // format) into the journal with two blockNo entries but no
            // matching DATA or TxE. Replay should discard it.
            var blk = new byte[4096];
            BitConverter.GetBytes(0xAABBCCDDu).CopyTo(blk, 0);  // TXB_MAGIC
            BitConverter.GetBytes(1234).CopyTo(blk, 4);          // fake TID
            BitConverter.GetBytes(2).CopyTo(blk, 8);             // count = 2
            BitConverter.GetBytes(100).CopyTo(blk, 12);          // blockNo 1
            BitConverter.GetBytes(101).CopyTo(blk, 16);          // blockNo 2
            MiniWebServer.Host.MiniFs.MiniFs.WriteBlockNoLog(8, blk);
            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("orphan-multi-txb-injected\n"));
        }
        else if (parsedRequest.Path.StartsWith("/fs-dump-block"))
        {
            // DEBUG route: dump a block as hex. ?blockNo=N
            int blockNo = -1;
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && kv.Substring(0, eq) == "blockNo" && int.TryParse(kv.Substring(eq + 1), out var n))
                        blockNo = n;
                }
            }
            if (blockNo < 0)
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes("blockNo required\n"));
            }
            else
            {
                var buf = new byte[4096];
                MiniWebServer.Host.MiniFs.MiniFs.ReadBlock(blockNo, buf);
                var sb = new StringBuilder();
                for (int i = 0; i < buf.Length; i++)
                {
                    sb.Append($"{buf[i]:X2} ");
                    if ((i + 1) % 32 == 0) sb.Append('\n');
                }
                response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(sb.ToString()));
            }
        }
        else if (parsedRequest.Path.StartsWith("/pager/run"))
        {
            // MiniPager demo. ?workload=seq|rand|two|array|exceed|cow&frames=N&tlb=N&pt=linear|level2&policy=fifo|lru|random
            string workload = "seq";
            int numFrames = 16;
            int tlbCapacity = 0;  // 0 = TLB disabled
            bool twoLevel = false; // slice 17.1
            string policy = "fifo"; // slice 18.1
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = kv.Substring(0, eq);
                    var v = kv.Substring(eq + 1);
                    if (k == "workload") workload = v;
                    else if (k == "frames" && int.TryParse(v, out var nf)) numFrames = nf;
                    else if (k == "tlb" && int.TryParse(v, out var tlb)) tlbCapacity = tlb;
                    else if (k == "pt" && v == "level2") twoLevel = true;
                    else if (k == "policy" && (v == "lru" || v == "random")) policy = v;
                }
            }

            string output;
            if (workload == "exceed")
            {
                output = MiniWebServer.Host.MiniPager.PagerRunner.RunReplacement(
                    MiniWebServer.Host.MiniPager.Workloads.ExceedsMemory(),
                    Math.Max(2, numFrames), tlbCapacity, policy);
            }
            else if (workload == "cow")
            {
                output = MiniWebServer.Host.MiniPager.PagerRunner.RunCow(numFrames);
            }
            else
            {
                output = workload switch
                {
                    "seq" => MiniWebServer.Host.MiniPager.PagerRunner.RunSingle(
                        MiniWebServer.Host.MiniPager.Workloads.SequentialSingleProcess(), numFrames, tlbCapacity, twoLevel, policy),
                    "rand" => MiniWebServer.Host.MiniPager.PagerRunner.RunSingle(
                        MiniWebServer.Host.MiniPager.Workloads.RandomSingleProcess(), numFrames, tlbCapacity, twoLevel, policy),
                    "two" => MiniWebServer.Host.MiniPager.PagerRunner.RunTwoOverlap(
                        MiniWebServer.Host.MiniPager.Workloads.TwoProcessesOverlap(), numFrames, tlbCapacity, twoLevel, policy),
                    "array" => MiniWebServer.Host.MiniPager.PagerRunner.RunSingle(
                        MiniWebServer.Host.MiniPager.Workloads.ArrayAccessOsep(), numFrames, tlbCapacity, twoLevel, policy),
                    _ => "",
                };
            }
            if (string.IsNullOrEmpty(output))
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes("unknown workload (use seq|rand|two|array|exceed|cow)\n"));
            }
            else
            {
                response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes(output));
            }
        }
        else if (parsedRequest.Path.StartsWith("/cv/run"))
        {
            // Slice 29.1 (condition variables). ?scenario=lost-wakeup|single-cv|two-cv|covering-condition
            string cvScenario = "two-cv";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    if (kv.Substring(0, eq) == "scenario") cvScenario = kv.Substring(eq + 1);
                }
            }
            try
            {
                var cvResult = MiniWebServer.Host.MiniScheduler.ConditionVariableDemos.Run(cvScenario);
                response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes(
                        MiniWebServer.Host.MiniScheduler.ConditionVariableDemos.Format(cvResult)));
            }
            catch (ArgumentException ex)
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes(ex.Message + "\n"));
            }
        }
        else if (parsedRequest.Path.StartsWith("/deadlock/run"))
        {
            // Slice 30.1 (deadlock prevention + avoidance). ?scenario=naive|ordering|batch|preempt|banker
            string dlScenario = "ordering";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    if (kv.Substring(0, eq) == "scenario") dlScenario = kv.Substring(eq + 1);
                }
            }
            try
            {
                var dlResult = MiniWebServer.Host.MiniScheduler.DeadlockSim.Run(dlScenario);
                response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes(
                        MiniWebServer.Host.MiniScheduler.DeadlockSim.Format(dlResult)));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes(ex.Message + "\n"));
            }
        }
        else if (parsedRequest.Path.StartsWith("/tlb/run"))
        {
            // Slice 28.1 (ASID-tagged TLB). ?scenario=flushall-vs-flushasid&tlb=N&context_switches=N
            string tlbScenario = "flushall-vs-flushasid";
            int tlbCapacity = 16;
            int contextSwitches = 10;
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = kv.Substring(0, eq);
                    var v = kv.Substring(eq + 1);
                    if (k == "scenario") tlbScenario = v;
                    else if (k == "tlb" && int.TryParse(v, out var tc)) tlbCapacity = tc;
                    else if (k == "context_switches" && int.TryParse(v, out var cs)) contextSwitches = cs;
                }
            }
            if (tlbScenario != "flushall-vs-flushasid")
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes("unknown tlb scenario (use flushall-vs-flushasid)\n"));
            }
            else
            {
                string tlbOutput = MiniWebServer.Host.MiniPager.PagerRunner.RunAsidContextSwitch(
                    tlbCapacity, contextSwitches);
                response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes(tlbOutput));
            }
        }
        else if (parsedRequest.Path.StartsWith("/scheduler/run"))
        {
            // MiniScheduler demo. ?algo=mlfq|stride|lottery|baseline&policy=fifo|sjf|stcf|rr&workload=two|cpu|mixed|proportional|convoy|latearrivals|equal|response&ticks=N&q=N&boost=M&quantum=K
            string algo = "mlfq";
            string baselinePolicy = "fifo";
            string workload = "mixed";
            int totalTicks = 80;
            int numQueues = 4;
            int boostEvery = 50;
            int quantum = 1;
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = kv.Substring(0, eq);
                    var v = kv.Substring(eq + 1);
                    if (k == "algo") algo = v;
                    else if (k == "policy") baselinePolicy = v;
                    else if (k == "workload") workload = v;
                    else if (k == "ticks" && int.TryParse(v, out var t)) totalTicks = t;
                    else if (k == "q" && int.TryParse(v, out var qn)) numQueues = qn;
                    else if (k == "boost" && int.TryParse(v, out var b)) boostEvery = b;
                    else if (k == "quantum" && int.TryParse(v, out var qs)) quantum = qs;
                }
            }

// M35 / OSEP Ch. 7: the Ch. 7 baselines live on their own branch because
            // they use a different job model (arrival time is what §7.6's
            // response time is measured from) and run to completion rather than
            // for a fixed tick budget. Building Ch. 7 workloads here would mean
            // unifying two models the chapter never unifies.
            if (algo == "baseline")
            {
                System.Collections.Generic.List<MiniWebServer.Host.MiniScheduler.BaselineJob> baseJobs =
                    MiniWebServer.Host.MiniScheduler.BaselineWorkload.FromName(workload);
                if (baseJobs.Count == 0)
                {
                    response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(
                            $"unknown workload (use {string.Join('|', MiniWebServer.Host.MiniScheduler.BaselineWorkload.Names)})\n"));
                }
                else if (baselinePolicy is not ("fifo" or "sjf" or "stcf" or "rr"))
                {
                    // An unknown policy would otherwise fall through to the
                    // default and answer 200 with FIFO's numbers - a client
                    // asking for a typo would get a plausible-looking result.
                    response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes($"unknown baseline policy '{baselinePolicy}' (use fifo|sjf|stcf|rr)\n"));
                }
                else
                {
                    var policyEnum = baselinePolicy switch
                    {
                        "fifo" => MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo,
                        "sjf" => MiniWebServer.Host.MiniScheduler.BaselinePolicy.Sjf,
                        "stcf" => MiniWebServer.Host.MiniScheduler.BaselinePolicy.Stcf,
                        _ => MiniWebServer.Host.MiniScheduler.BaselinePolicy.RoundRobin,
                    };
                    try
                    {
                        var result = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(
                            policyEnum, baseJobs, quantum);
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(FormatBaselineSchedule(policyEnum, result, workload, quantum)));
                    }
                    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
                    {
                        // quantum=0 on RR, or a quantum on a run-to-completion
                        // policy: both are client mistakes, not server errors.
                        response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(ex.Message + "\n"));
                    }
                }
            }
            else
            {
            System.Collections.Generic.List<MiniWebServer.Host.MiniScheduler.Job> jobs = workload switch
            {
                "two" => MiniWebServer.Host.MiniScheduler.Workloads.TwoJobs(),
                "cpu" => MiniWebServer.Host.MiniScheduler.Workloads.TwoCpuBound(),
                "mixed" => MiniWebServer.Host.MiniScheduler.Workloads.MixedWorkload(),
                "proportional" => MiniWebServer.Host.MiniScheduler.Workloads.ProportionalWorkload(),
                _ => new System.Collections.Generic.List<MiniWebServer.Host.MiniScheduler.Job>(),
            };
            if (jobs.Count == 0)
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes("unknown workload (use two|cpu|mixed|proportional)\n"));
            }
            else
            {
                string output = algo switch
                {
                    "stride" => new MiniWebServer.Host.MiniScheduler.StrideScheduler(jobs).Run(totalTicks),
                    "lottery" => new MiniWebServer.Host.MiniScheduler.LotteryScheduler(jobs).Run(totalTicks),
                    _ => MiniWebServer.Host.MiniScheduler.SchedulerRunner.RunMlfq(
                        jobs, totalTicks, numQueues, null, boostEvery),
                };
                response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes(output));
            }
            }
        }
        else if (parsedRequest.Path.StartsWith("/heap/run"))
        {
            // M36 / OSEP Ch. 17. ?scenario=split|coalesce|strategies|buddy&policy=first|best|worst|next
            response = BuildHeapResponse(parsedRequest.Path);
        }
        else if (parsedRequest.Path.StartsWith("/multicpu/run"))
        {
            // Multi-CPU demo. ?mode=sqms|mqms|ws&workload=sqms|imbalance&cpus=N&ticks=M&peek=K
            string mode = "sqms";
            string workload = "sqms";
            int cpus = 2;
            int maxTicks = 30;
            int peekInterval = 5;
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = kv.Substring(0, eq);
                    var v = kv.Substring(eq + 1);
                    if (k == "mode") mode = v;
                    else if (k == "workload") workload = v;
                    else if (k == "cpus" && int.TryParse(v, out var c)) cpus = c;
                    else if (k == "ticks" && int.TryParse(v, out var t)) maxTicks = t;
                    else if (k == "peek" && int.TryParse(v, out var p)) peekInterval = p;
                }
            }

            var multiMode = mode switch
            {
                "mqms" => MiniWebServer.Host.MiniScheduler.MultiCpuMode.Mqms,
                "ws" => MiniWebServer.Host.MiniScheduler.MultiCpuMode.MqmsWorkStealing,
                _ => MiniWebServer.Host.MiniScheduler.MultiCpuMode.Sqms,
            };
            var multicpuJobs = workload switch
            {
                "sqms" => MiniWebServer.Host.MiniScheduler.Workloads.SqmsDemoWorkload(),
                "imbalance" => MiniWebServer.Host.MiniScheduler.Workloads.MqmsImbalanceWorkload(),
                "extreme" => MiniWebServer.Host.MiniScheduler.Workloads.MqmsExtremeImbalanceWorkload(),
                _ => new System.Collections.Generic.List<MiniWebServer.Host.MiniScheduler.Job>(),
            };
            if (multicpuJobs.Count == 0)
            {
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes("unknown workload (use sqms|imbalance|extreme)\n"));
            }
            else
            {
                string output = new MiniWebServer.Host.MiniScheduler.MultiCpuScheduler(
                    multicpuJobs, cpus, multiMode, peekInterval).Run(maxTicks);
                response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes(output));
            }
        }
        else if (parsedRequest.Path.StartsWith("/dining/run"))
        {
            // Dining philosophers demo. ?mode=broken|fixed&philosophers=N&seconds=M&think=T&eat=E
            string mode = "fixed";
            int philosophers = 5;
            int seconds = 3;
            int thinkMs = 50;
            int eatMs = 25;
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = kv.Substring(0, eq);
                    var v = kv.Substring(eq + 1);
                    if (k == "mode") mode = v;
                    else if (k == "philosophers" && int.TryParse(v, out var ph)) philosophers = ph;
                    else if (k == "seconds" && int.TryParse(v, out var s)) seconds = s;
                    else if (k == "think" && int.TryParse(v, out var t)) thinkMs = t;
                    else if (k == "eat" && int.TryParse(v, out var e)) eatMs = e;
                }
            }
            var diningMode = mode == "broken"
                ? MiniWebServer.Host.MiniScheduler.DiningMode.Broken
                : MiniWebServer.Host.MiniScheduler.DiningMode.Fixed;
            var dining = new MiniWebServer.Host.MiniScheduler.DiningPhilosophers(
                philosophers, diningMode, seconds, thinkMs: thinkMs, eatMs: eatMs);
            var stats = dining.Run();
            string output = dining.FormatReport();
            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes(output));
        }
        else if (parsedRequest.Path.StartsWith("/lockfree/bench"))
        {
            // Lock-free benchmark. ?impl=atomic|locked|stack-atomic|stack-locked&threads=N&ops=M
            string impl = "atomic";
            int threads = 4;
            int ops = 100_000;
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = kv.Substring(0, eq);
                    var v = kv.Substring(eq + 1);
                    if (k == "impl") impl = v;
                    else if (k == "threads" && int.TryParse(v, out var t)) threads = t;
                    else if (k == "ops" && int.TryParse(v, out var o)) ops = o;
                }
            }
            var lockFreeImpl = impl switch
            {
                "locked" => MiniWebServer.Host.MiniScheduler.LockFreeImpl.LockedCounter,
                "stack-atomic" => MiniWebServer.Host.MiniScheduler.LockFreeImpl.LockFreeStack,
                "stack-locked" => MiniWebServer.Host.MiniScheduler.LockFreeImpl.LockedStack,
                _ => MiniWebServer.Host.MiniScheduler.LockFreeImpl.AtomicCounter,
            };
            var bench = new MiniWebServer.Host.MiniScheduler.LockFreeBenchmark(threads, ops);
            string output = bench.Run(lockFreeImpl);
            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes(output));
        }
        else if (parsedRequest.Path.StartsWith("/ffs/run"))
        {
            // FFS placement simulator. ?scenario=manyfiles|largefile&groups=N&inodes=N&blocks=N&threshold=M
            string scenario = "manyfiles";
            int groups = 8;
            int inodesPerGroup = 5;
            int blocksPerGroup = 16;
            int threshold = 12;
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = kv.Substring(0, eq);
                    var v = kv.Substring(eq + 1);
                    if (k == "scenario") scenario = v;
                    else if (k == "groups" && int.TryParse(v, out var gv)) groups = gv;
                    else if (k == "inodes" && int.TryParse(v, out var iv)) inodesPerGroup = iv;
                    else if (k == "blocks" && int.TryParse(v, out var bv)) blocksPerGroup = bv;
                    else if (k == "threshold" && int.TryParse(v, out var tv)) threshold = tv;
                }
            }

            var ffs = new MiniWebServer.Host.MiniScheduler.FFS(groups, inodesPerGroup, blocksPerGroup, threshold);

            if (scenario == "largefile")
            {
                // Single big file: /a with 30 blocks (matches OSEP §41.6 example).
                ffs.CreateDir("/", parent: null);
                ffs.CreateFile("/a", "/", 30);
            }
            else
            {
                // manyfiles scenario (OSEP §41.7 question 4):
                // root + /a + /b, files /a/c, /a/d, /a/e, /b/f — each 2 blocks.
                ffs.CreateDir("/", parent: null);
                ffs.CreateDir("/a", parent: "/");
                ffs.CreateDir("/b", parent: "/");
                ffs.CreateFile("/a/c", "/a", 2);
                ffs.CreateFile("/a/d", "/a", 2);
                ffs.CreateFile("/a/e", "/a", 2);
                ffs.CreateFile("/b/f", "/b", 2);
            }
            string output = ffs.FormatLayout();
            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes(output));
        }
        else if (parsedRequest.Path.StartsWith("/raid/run"))
        {
            // RAID simulator (M24 / OSEP Ch. 38). ?level=0|1|4|5&disks=N&blocks=M&failed=K
            // Default scenario: write a deterministic payload across all stripes,
            // optionally fail disk K, then read everything back and report.
            int level = 5;
            int disks = 4;
            int blocks = 4;
            int failed = -1;
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = kv.Substring(0, eq);
                    var v = kv.Substring(eq + 1);
                    if (k == "level" && int.TryParse(v, out var lv)) level = lv;
                    else if (k == "disks" && int.TryParse(v, out var dv)) disks = dv;
                    else if (k == "blocks" && int.TryParse(v, out var bv)) blocks = bv;
                    else if (k == "failed" && int.TryParse(v, out var fv)) failed = fv;
                }
            }
            if (level != 0 && level != 1 && level != 4 && level != 5)
                level = 5;
            var raidLevel = level switch
            {
                0 => MiniWebServer.Host.MiniScheduler.RaidLevel.Raid0,
                1 => MiniWebServer.Host.MiniScheduler.RaidLevel.Raid1,
                4 => MiniWebServer.Host.MiniScheduler.RaidLevel.Raid4,
                5 => MiniWebServer.Host.MiniScheduler.RaidLevel.Raid5,
                _ => MiniWebServer.Host.MiniScheduler.RaidLevel.Raid5,
            };
            var raid = new MiniWebServer.Host.MiniScheduler.Raid(raidLevel, disks, blocks);

            // Write a deterministic payload: each logical block gets a unique byte.
            // For RAID 0 each logical block is its own disk slot; for RAID 1 each is a
            // position on both disks; for RAID 4/5 we fill stripes.
            string writeTrace;
            if (raidLevel == MiniWebServer.Host.MiniScheduler.RaidLevel.Raid0)
            {
                // Total logical blocks = disks * blocks. Write b'A'..b'Z' wrapping.
                int total = disks * blocks;
                writeTrace = $"wrote {total} logical blocks across {disks} disks";
                for (int i = 0; i < total; i++)
                {
                    byte b = (byte)('A' + (i % 26));
                    raid.WriteRaid0(i, b);
                }
            }
            else if (raidLevel == MiniWebServer.Host.MiniScheduler.RaidLevel.Raid1)
            {
                writeTrace = $"wrote {blocks} logical blocks mirrored across 2 disks";
                for (int i = 0; i < blocks; i++)
                {
                    byte b = (byte)('A' + (i % 26));
                    raid.WriteRaid1(i, b);
                }
            }
            else
            {
                // RAID 4/5: fill `blocks` stripes.
                int dataDisks = disks - 1;
                writeTrace = $"wrote {blocks} stripes with {dataDisks} data bytes each";
                for (int s = 0; s < blocks; s++)
                {
                    var data = new byte[dataDisks];
                    for (int i = 0; i < dataDisks; i++)
                        data[i] = (byte)('A' + ((s * dataDisks + i) % 26));
                    raid.WriteStripeRaidParity(s, data);
                }
            }

            // Optionally fail a disk.
            string readTrace;
            if (failed >= 0)
            {
                raid.FailDisk(failed);
            }
            // Read back.
            var reads = new System.Collections.Generic.List<string>();
            try
            {
                if (raidLevel == MiniWebServer.Host.MiniScheduler.RaidLevel.Raid0)
                {
                    int total = disks * blocks;
                    for (int i = 0; i < total; i++)
                        reads.Add(((char)raid.ReadRaid0(i)).ToString());
                }
                else if (raidLevel == MiniWebServer.Host.MiniScheduler.RaidLevel.Raid1)
                {
                    for (int i = 0; i < blocks; i++)
                        reads.Add(((char)raid.ReadRaid1(i)).ToString());
                }
                else
                {
                    int dataDisks = disks - 1;
                    for (int s = 0; s < blocks; s++)
                    {
                        var cells = new System.Collections.Generic.List<string>();
                        for (int i = 0; i < dataDisks; i++)
                            cells.Add(((char)raid.ReadRaidParity(s, i)).ToString());
                        cells.Add("P" + ((char)raid.ReadParity(s)).ToString());
                        reads.Add($"stripe{s}=[{string.Join(",", cells)}]");
                    }
                }
                readTrace = $"read OK: {string.Join(" ", reads)}";
            }
            catch (Exception ex)
            {
                readTrace = $"read FAIL: {ex.Message}";
            }

            string raidOutput = raid.FormatLayout()
                + Environment.NewLine
                + $"trace: {writeTrace}"
                + Environment.NewLine
                + $"trace: {readTrace}";
            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes(raidOutput));
        }
        else if (parsedRequest.Path.StartsWith("/lfs/run"))
        {
            // LFS simulator (M25 / OSEP Ch. 43). ?scenario=create|rewrite|clean&segments=N&blocks=M&files=K
            // Default scenario: create K files with one block each, then flush,
            // and (for rewrite) overwrite some of them to introduce garbage,
            // then (for clean) run the segment cleaner.
            string scenario = "create";
            int segments = 8;
            int blocks = 6;
            int files = 3;
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = kv.Substring(0, eq);
                    var v = kv.Substring(eq + 1);
                    if (k == "scenario") scenario = v;
                    else if (k == "segments" && int.TryParse(v, out var sv)) segments = sv;
                    else if (k == "blocks" && int.TryParse(v, out var bv)) blocks = bv;
                    else if (k == "files" && int.TryParse(v, out var fv)) files = fv;
                }
            }
            // Slice 31.1 + 31.2 scenarios (OSEP §43.3 + §43.12). These do not need the
            // M25 simulator — the cost model and the CR protocol are standalone —
            // so they are dispatched before it is constructed.
            if (scenario is "segment-size-sweep" or "cost-model"
                or "dual-cr-recovery" or "cr-alternation" or "cr-crash-during-write")
            {
                try
                {
                    var ext = MiniWebServer.Host.MiniScheduler.LfsExtensionsDemos.Run(scenario);
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(
                            MiniWebServer.Host.MiniScheduler.LfsExtensionsDemos.Format(ext)));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(ex.Message + "\n"));
                }
            }
            else if (scenario is not ("create" or "rewrite" or "clean"))
            {
                // An unrecognised scenario is a client error, not a reason to run
                // the default workload: falling through would answer 200 with
                // unrelated output for a typo.
                response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes(
                        $"unknown lfs scenario '{scenario}' (use create, rewrite, clean, "
                        + "cost-model, segment-size-sweep, dual-cr-recovery, cr-alternation, "
                        + "cr-crash-during-write)\n"));
            }
            else
            {
            var lfs = new MiniWebServer.Host.MiniScheduler.Lfs(segments, blocks);

            string trace = "";
            string lfsOutput;
            try
            {
                // Create N files with one block each.
                for (int i = 0; i < files; i++)
                {
                    lfs.CreateFile($"/f{i}");
                    lfs.WriteData($"/f{i}", 0, (byte)('A' + (i % 26)));
                }
                lfs.Flush();
                trace += $"create: wrote {files} files, live={lfs.LiveBlockCount} dead={lfs.DeadBlockCount}" + Environment.NewLine;

                if (scenario == "rewrite" || scenario == "clean")
                {
                    // Rewrite all but the last file to introduce garbage.
                    for (int i = 0; i < files - 1; i++)
                    {
                        lfs.WriteData($"/f{i}", 0, (byte)('a' + (i % 26)));
                    }
                    lfs.Flush();
                    trace += $"rewrite: live={lfs.LiveBlockCount} dead={lfs.DeadBlockCount}" + Environment.NewLine;
                }

                if (scenario == "clean")
                {
                    // Run the cleaner several times.
                    int cleaned = 0;
                    for (int i = 0; i < 5; i++)
                    {
                        var report = lfs.Clean();
                        if (report.CleanedSegment < 0) break;
                        cleaned++;
                    }
                    trace += $"clean: ran {cleaned} times, free segments={lfs.FreeSegmentCount}" + Environment.NewLine;
                }

                // Smoke-read every file.
                var reads = new System.Collections.Generic.List<string>();
                for (int i = 0; i < files; i++)
                {
                    byte v = lfs.Read($"/f{i}", 0);
                    reads.Add($"/f{i}={(char)v}");
                }
                trace += $"read OK: {string.Join(" ", reads)}";

                lfsOutput = lfs.FormatLayout() + Environment.NewLine + "trace:" + Environment.NewLine + trace;
            }
            catch (Exception ex)
            {
                trace += $"FAIL: {ex.Message}";
                try { lfsOutput = lfs.FormatLayout() + Environment.NewLine + "trace:" + Environment.NewLine + trace; }
                catch (Exception ex2) { lfsOutput = $"LFS error: {ex.Message} (also layout failed: {ex2.Message})"; }
            }

            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes(lfsOutput));
            }
        }
        else if (parsedRequest.Path.StartsWith("/ssd/run"))
        {
            // SSD simulator (M26 / OSEP Ch. 44). ?scenario=write|gc|wear&blocks=N&pages=K
            // Default scenario: write a deterministic payload of LBAs, then either
            // rewrite some of them (to introduce dead pages) and run GC, or just
            // dump the wear report.
            string scenario = "write";
            int blocks = 4;
            int pagesPerBlock = 4;
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = kv.Substring(0, eq);
                    var v = kv.Substring(eq + 1);
                    if (k == "scenario") scenario = v;
                    else if (k == "blocks" && int.TryParse(v, out var bv)) blocks = bv;
                    else if (k == "pages" && int.TryParse(v, out var pv)) pagesPerBlock = pv;
                }
            }
            // M32 / OSEP §44.9 adds scenarios that compare FTL mapping
            // strategies rather than drive M26's page-level simulator. They
            // share this route because they answer the same question: what a
            // write costs on flash.
            if (scenario is "ftl-comparison" or "mapping-cost" or "merges")
            {
                int writes = 200;
                int lbaSpace = 1024;
                int logBlocks = 4;
                string ftl = "all";
                string workload = "random";
                foreach (var kv in MiniWebServer.Host.MiniScheduler.FtlDemos.QueryParts(parsedRequest.Path))
                {
                    if (kv.Key == "ftl") ftl = kv.Value;
                    else if (kv.Key == "workload") workload = kv.Value;
                    else if (kv.Key == "writes" && int.TryParse(kv.Value, out var wv)) writes = wv;
                    else if (kv.Key == "lbas" && int.TryParse(kv.Value, out var lv)) lbaSpace = lv;
                    else if (kv.Key == "logblocks" && int.TryParse(kv.Value, out var gv)) logBlocks = gv;
                }

                // An unknown FTL name or an out-of-range device size is a
                // client error, not a reason to run the default workload and
                // answer 200 with unrelated output.
                if (ftl is not ("all" or "pagelevel" or "blocklevel" or "hybrid"))
                {
                    response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(
                            $"unknown ftl '{ftl}' (use all, pagelevel, blocklevel, hybrid)\n"));
                }
                else if (writes <= 0 || lbaSpace <= 0 || logBlocks < 0)
                {
                    response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes($"writes and lbas must be positive, logblocks non-negative\n"));
                }
                else if (workload is not ("seq" or "random"))
                {
                    response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes($"unknown workload '{workload}' (use seq, random)\n"));
                }
                else
                {
                    string ftlOutput = scenario switch
                    {
                        "ftl-comparison" => MiniWebServer.Host.MiniScheduler.FtlDemos.RunComparison(ftl, writes, workload, lbaSpace, logBlocks),
                        "mapping-cost" => MiniWebServer.Host.MiniScheduler.FtlDemos.RunMappingCost(logBlocks),
                        _ => MiniWebServer.Host.MiniScheduler.FtlDemos.RunMerges(pagesPerBlock),
                    };
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(ftlOutput));
                }
            }
            else
            {
            var ssd = new MiniWebServer.Host.MiniScheduler.Ssd(blocks, pagesPerBlock);
            string trace = "";
            string ssdOutput;
            try
            {
                // Write a deterministic payload. Keep the count small enough
                // that the gc/wear scenarios have headroom for rewrites +
                // migration: a 4-block x 4-page device has 16 pages; we want
                // ~8 initial writes so a 4-write rewrite can migrate via GC.
                int lbaCount = Math.Min(blocks * pagesPerBlock / 2, 8);
                for (int i = 0; i < lbaCount; i++)
                {
                    ssd.Write(i, (byte)('A' + (i % 26)));
                }
                trace += $"write: wrote {lbaCount} LBAs, mapping size={ssd.MappingSize}" + Environment.NewLine;

                if (scenario == "gc" || scenario == "wear")
                {
                    // Rewrite the first half to introduce dead pages.
                    int half = lbaCount / 2;
                    for (int i = 0; i < half; i++)
                    {
                        ssd.Write(i, (byte)('a' + (i % 26)));
                    }
                    trace += $"rewrite: dead pages={ssd.DeadPageCount}" + Environment.NewLine;
                }

                if (scenario == "gc")
                {
                    // Run GC multiple times.
                    int cleaned = 0;
                    for (int i = 0; i < 5; i++)
                    {
                        var report = ssd.CollectGarbage();
                        if (report.CleanedBlock < 0) break;
                        cleaned++;
                    }
                    trace += $"gc: ran {cleaned} times, dead pages now={ssd.DeadPageCount}" + Environment.NewLine;
                }

                // Smoke-read every LBA.
                var reads = new System.Collections.Generic.List<string>();
                for (int i = 0; i < lbaCount; i++)
                {
                    byte v;
                    try { v = ssd.Read(i); }
                    catch (Exception ex) { reads.Add($"LBA{i}=ERR({ex.Message})"); continue; }
                    reads.Add($"LBA{i}={(char)v}");
                }
                trace += $"read OK: {string.Join(" ", reads)}";

                ssdOutput = ssd.FormatLayout() + Environment.NewLine + "trace:" + Environment.NewLine + trace;
                if (scenario == "wear") ssdOutput += Environment.NewLine + ssd.FormatWearReport();
            }
            catch (Exception ex)
            {
                trace += $"FAIL: {ex.Message}";
                try { ssdOutput = ssd.FormatLayout() + Environment.NewLine + "trace:" + Environment.NewLine + trace; }
                catch (Exception ex2) { ssdOutput = $"SSD error: {ex.Message} (also layout failed: {ex2.Message})"; }
            }

            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes(ssdOutput));
            }
        }
        else if (parsedRequest.Path.StartsWith("/integrity/run"))
        {
            // Integrity simulator (M27 / OSEP Ch. 45). ?scenario=compute|corrupt|scrub&blocks=N&blockSize=M
            // Default scenario:
            //   compute: write a payload, then run all three checksums + report.
            //   corrupt: write + inject each fault + show detection.
            //   scrub: write N blocks + inject faults + run scrubber + report.
            string scenario = "compute";
            int blocks = 8;
            int blockSize = 16;
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var kv in parsedRequest.Path.Substring(qIdx + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = kv.Substring(0, eq);
                    var v = kv.Substring(eq + 1);
                    if (k == "scenario") scenario = v;
                    else if (k == "blocks" && int.TryParse(v, out var bv)) blocks = bv;
                    else if (k == "blockSize" && int.TryParse(v, out var sv)) blockSize = sv;
                }
            }
            // M33 / OSEP §45.7-§45.8 adds the scheduling half of scrubbing: M27's
            // Scrub() is one-shot, while the chapter describes a system that
            // "periodically read[s] through every block" on a policy. These
            // scenarios do not need M27's store layout, so they are dispatched
            // before it is constructed.
            if (scenario is "scrub-schedule" or "scrub-sweep" or "checksum-overhead")
            {
                response = BuildScrubResponse(parsedRequest.Path, scenario, blocks, blockSize);
            }
            else
            {
            var store = new MiniWebServer.Host.MiniScheduler.IntegrityStore(diskId: 0, blocks: blocks, blockSize: blockSize);

            string trace = "";
            string output;
            try
            {
                // Always write block 0 with a fixed payload.
                var payload = new byte[] { 0x36, 0x5e, 0xc4, 0xcd, 0xba, 0x14, 0x8a, 0x92 };
                store.Write(0, payload);

                if (scenario == "compute")
                {
                    byte xor = MiniWebServer.Host.MiniScheduler.IntegrityChecksums.Xor(payload);
                    byte add = MiniWebServer.Host.MiniScheduler.IntegrityChecksums.Additive(payload);
                    var (s1, s2) = MiniWebServer.Host.MiniScheduler.IntegrityChecksums.Fletcher(payload);
                    trace += $"payload (hex): {BitConverter.ToString(payload)}" + Environment.NewLine;
                    trace += $"xor checksum: 0x{xor:X2}" + Environment.NewLine;
                    trace += $"additive checksum: 0x{add:X2}" + Environment.NewLine;
                    trace += $"fletcher checksum: (s1=0x{s1:X2}, s2=0x{s2:X2})" + Environment.NewLine;
                }
                else if (scenario == "corrupt")
                {
                    // Inject one of each fault and report detection.
                    store.InjectCorruption(0, 0, 0x01);  // flip bit 0 of byte 0
                    var blk = store.GetBlock(0);
                    var failures = store.Verify(blk);
                    trace += $"after corruption: {failures.Count} failures detected:" + Environment.NewLine;
                    foreach (var f in failures) trace += $"  - {f}" + Environment.NewLine;
                }
                else if (scenario == "scrub")
                {
                    // Write 8 blocks + inject faults on 2 of them.
                    for (int i = 0; i < blocks; i++)
                    {
                        store.Write(i, new byte[] { (byte)('A' + i), (byte)('0' + i) });
                    }
                    store.InjectCorruption(2, 0, 0x10);
                    store.InjectMisdirectedWrite(5, 99);
                    var report = store.Scrub();
                    trace += $"scrub: {report.OkCount} OK, {report.BadCount} BAD" + Environment.NewLine;
                    foreach (var (bid, fails) in report.BadBlocks)
                    {
                        trace += $"  block {bid}:" + Environment.NewLine;
                        foreach (var f in fails) trace += $"    - {f}" + Environment.NewLine;
                    }
                }
                output = store.FormatLayout() + Environment.NewLine + "trace:" + Environment.NewLine + trace;
            }
            catch (Exception ex)
            {
                trace += $"FAIL: {ex.Message}";
                output = "trace:" + Environment.NewLine + trace;
            }

            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes(output));
            }
        }
        else if (parsedRequest.Path.StartsWith("/device/run"))
        {
            // Device simulator (M34 / OSEP §36.2-§36.6).
            //   ?scenario=canonical-protocol&payload=N&latency=T
            //   ?scenario=pio-vs-dma&transfer_bytes=N
            //   ?scenario=interrupt-vs-poll&latency=T
            //   ?scenario=mmio
            response = BuildDeviceResponse(parsedRequest.Path);
        }
        else if (parsedRequest.Path.StartsWith("/auth/"))
        {
            // Password-based authentication (M23 / OSEP Ch. 54). Routes:
            //   POST /auth/register?user=X&pass=Y   register
            //   POST /auth/login?user=X&pass=Y      verify
            //   GET  /auth/dump                     list stored salt+hash entries
            //   GET  /auth/run?scenario=...         demo scenarios
            //
            // Note: query-string passwords are visible in process logs and on the wire.
            // OSEP §57.4 makes clear that's wrong for real systems. Real auth runs over
            // TLS with the password in the POST body, not the URL.
            var authPath = parsedRequest.Path;
            int authQ = authPath.IndexOf('?');
            string authQuery = authQ >= 0 ? authPath.Substring(authQ + 1) : "";
            string authUser = "";
            string authPass = "";
            string authScenario = "";
            string authRoleQuery = "";   // M23.3: optional ?role=admin at /auth/register
            foreach (var kv in authQuery.Split('&'))
            {
                int eq = kv.IndexOf('=');
                if (eq <= 0) continue;
                var k = Uri.UnescapeDataString(kv.Substring(0, eq));
                var v = Uri.UnescapeDataString(kv.Substring(eq + 1));
                if      (k == "user")     authUser = v;
                else if (k == "pass")     authPass = v;
                else if (k == "scenario") authScenario = v;
                else if (k == "role")     authRoleQuery = v;
            }

            // Strip /auth/ prefix so the route switch is local.
            string routeName = authPath.Substring("/auth/".Length);
            int routeQ = routeName.IndexOf('?');
            if (routeQ >= 0) routeName = routeName.Substring(0, routeQ);

            string authBody;
            // `response` is set below on every code path; null! here lets the
            // compiler see definite assignment while we keep it logically
            // uninitialized for the nested-switch pattern. Use `response ??=`
            // at the bottom of each case to keep the wiring short.
            response = null!;
            switch (routeName)
            {
                case "register":
                {
                    if (string.IsNullOrEmpty(authUser) || string.IsNullOrEmpty(authPass))
                    {
                        authBody = "missing user and/or pass\n";
                        response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(authBody));
                        break;
                    }
                    // M23.3: optional ?role=admin|user parameter. The chapter's
                    // "least privilege" default is User; Admin must be requested.
                    var requestedRole = authRoleQuery == "admin"
                        ? MiniWebServer.Host.MiniAuth.UserStore.Role.Admin
                        : MiniWebServer.Host.MiniAuth.UserStore.Role.User;
                    bool ok = MiniWebServer.Host.MiniAuth.UserStore.Register(authUser, authPass, requestedRole);
                    if (!ok)
                    {
                        authBody = "username already taken (or empty user/pass)\n";
                        response = new HttpResponse(409, "Conflict", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(authBody));
                        break;
                    }
                    authBody = $"OK  user={authUser}  role={requestedRole}\n";
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(authBody));
                    break;
                }
                case "grant":
                {
                    // OSEP §55.6 RBAC: "give a user or a process the minimum privileges required".
                    // We expose grant purely for the smoke demo — a real system would require
                    // an already-admin caller to grant new privileges to others.
                    if (string.IsNullOrEmpty(authUser) || string.IsNullOrEmpty(authScenario))
                    {
                        authBody = "missing user and/or role\n";
                        response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(authBody));
                        break;
                    }
                    var newRole = authScenario == "admin"
                        ? MiniWebServer.Host.MiniAuth.UserStore.Role.Admin
                        : MiniWebServer.Host.MiniAuth.UserStore.Role.User;
                    bool grantOk = MiniWebServer.Host.MiniAuth.UserStore.GrantRole(authUser, newRole);
                    if (!grantOk)
                    {
                        authBody = $"unknown user '{authUser}' (they need to /auth/register first)\n";
                        response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(authBody));
                        break;
                    }
                    authBody = $"OK  {authUser} granted role={newRole}\n";
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(authBody));
                    break;
                }
                case "role":
                {
                    // Read a user's role. OSEP §53.4 fail-safe defaults: returns
                    // "unknown" rather than "no such user" so attackers can't probe usernames.
                    authBody = $"role: {(MiniWebServer.Host.MiniAuth.UserStore.GetRole(authUser)?.ToString() ?? "unknown")}\n";
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(authBody));
                    break;
                }
                case "auth-as":
                {
                    // Authenticate + check role. Returns 200 with body "admin ok" /
                    // "user ok" / "invalid credentials". Used by /protected/secret gate.
                    // Internal helper; not advertised in the slice doc.
                    bool isAdmin = authScenario == "admin";
                    bool passOk = MiniWebServer.Host.MiniAuth.UserStore.AuthenticateWithRole(
                        authUser, authPass,
                        isAdmin ? MiniWebServer.Host.MiniAuth.UserStore.Role.Admin : MiniWebServer.Host.MiniAuth.UserStore.Role.User);
                    if (!passOk)
                    {
                        authBody = "invalid credentials\n";
                        response = new HttpResponse(401, "Unauthorized", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(authBody));
                        break;
                    }
                    authBody = isAdmin ? "admin ok\n" : "user ok\n";
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(authBody));
                    break;
                }
                case "login":
                {
                    if (string.IsNullOrEmpty(authUser) || string.IsNullOrEmpty(authPass))
                    {
                        // Fail-safe defaults (OSEP §53.4): identical 401 either way.
                        authBody = "invalid credentials\n";
                        response = new HttpResponse(401, "Unauthorized", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(authBody));
                        break;
                    }
                    bool ok = MiniWebServer.Host.MiniAuth.UserStore.Login(authUser, authPass);
                    if (!ok)
                    {
                        authBody = "invalid credentials\n";
                        response = new HttpResponse(401, "Unauthorized", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(authBody));
                        break;
                    }
                    authBody = $"OK  user={authUser}\n";
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(authBody));
                    break;
                }
                case "dump":
                {
                    var (_, lines) = MiniWebServer.Host.MiniAuth.UserStore.DumpSummary();
                    var sb = new System.Text.StringBuilder();
                    sb.Append("users: ").Append(lines.Length).Append('\n');
                    sb.Append("stored form: hash + salt only (never plaintext)\n");
                    sb.Append($"failed_logins: {MiniWebServer.Host.MiniAuth.UserStore.FailedLoginAttempts}\n");
                    sb.Append($"successful_logins: {MiniWebServer.Host.MiniAuth.UserStore.SuccessfulLogins}\n");
                    sb.Append("---\n");
                    foreach (var line in lines) sb.Append(line).Append('\n');
                    authBody = sb.ToString();
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(authBody));
                    break;
                }
                case "run":
                {
                    if (string.IsNullOrEmpty(authScenario)) authScenario = "register";
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"scenario: {authScenario}\n");
                    switch (authScenario)
                    {
                        case "register":
                        {
                            sb.Append("registers two demo users, alice & bob, both with password 'hunter2'\n");
                            sb.Append("(pre-clear via /auth/run?scenario=clear if reusing the server)\n");
                            // They must use a fresh server for register to succeed the second time.
                            sb.Append("see HashAttack scenario for the salt demonstration.\n");
                            authBody = sb.ToString();
                            break;
                        }
                        case "hashattack":
                        {
                            // Demonstrate OSEP §54.4: same password, two users, different salts → different hashes.
                            // We re-register (silently ignores conflict) so a fresh server works.
                            MiniWebServer.Host.MiniAuth.UserStore.ClearForTests();
                            MiniWebServer.Host.MiniAuth.UserStore.Register("alice", "hunter2");
                            MiniWebServer.Host.MiniAuth.UserStore.Register("bob",   "hunter2");
                            var (_, lines) = MiniWebServer.Host.MiniAuth.UserStore.DumpSummary();
                            sb.Append("Two users, identical plaintext password 'hunter2':\n");
                            foreach (var line in lines) sb.Append(line).Append('\n');
                            sb.Append("Observation: salts differ, hashes differ.\n");
                            sb.Append("OSEP §54.4: per-user salt defeats rainbow-table precomputation.\n");
                            authBody = sb.ToString();
                            break;
                        }
                        case "login":
                        {
                            sb.Append("login route summary:\n");
                            sb.Append($"  failed_logins   = {MiniWebServer.Host.MiniAuth.UserStore.FailedLoginAttempts}\n");
                            sb.Append($"  successful_logins = {MiniWebServer.Host.MiniAuth.UserStore.SuccessfulLogins}\n");
                            sb.Append("Try: POST /auth/login?user=alice&pass=hunter2 (correct)\n");
                            sb.Append("  vs /auth/login?user=alice&pass=wrong     (fails, identical error)\n");
                            authBody = sb.ToString();
                            break;
                        }
                        case "dictionary":
                        {
                            // OSEP §54.4 "drastically slowing down": PBKDF2 verify is slow on purpose.
                            // We measure how long it takes to PBKDF2-verify each of the top 5 common
                            // passwords against a registered user. This is the worst-case work an
                            // attacker does per guess against a stolen hash file.
                            MiniWebServer.Host.MiniAuth.UserStore.ClearForTests();
                            MiniWebServer.Host.MiniAuth.UserStore.Register("victim", "p4ssw0rd");
                            string[] guesses = { "123456", "password", "12345", "qwerty", "p4ssw0rd" };
                            sb.Append("dictionary attack simulation (OSEP §54.4):\n");
                            sb.Append($"target user: victim (password = 'p4ssw0rd')\n");
                            sb.Append($"each PBKDF2 verify costs ~50-100 ms of HMAC-SHA256 work.\n");
                            sb.Append("---\n");
                            foreach (var guess in guesses)
                            {
                                // Time one PBKDF2 verify call so the per-guess cost is visible.
// Worst case for the attacker: identical cost on every guess.
                                byte[] salt;
                                byte[] expectedHash;
                                {
                                    var (count, lines) = MiniWebServer.Host.MiniAuth.UserStore.DumpSummary();
                                    // victim is the only user, just registered above.
                                    // The dump line adds a `role=` column (M23.3); use a name-based
                                    // parse instead of fixed tab indices so adding columns stays safe.
                                    string saltHex = "", hashHex = "";
                                    foreach (var field in lines[0].Split('\t'))
                                    {
                                        const string saltPrefix = "salt=";
                                        const string hashPrefix = "sha256(pbkdf2)=";
                                        if (field.StartsWith(saltPrefix)) saltHex = field.Substring(saltPrefix.Length);
                                        else if (field.StartsWith(hashPrefix)) hashHex = field.Substring(hashPrefix.Length);
                                    }
                                    salt         = MiniWebServer.Host.MiniAuth.PasswordHasher.FromHex(saltHex);
                                    expectedHash = MiniWebServer.Host.MiniAuth.PasswordHasher.FromHex(hashHex);
                                }
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                MiniWebServer.Host.MiniAuth.PasswordHasher.Verify(guess, salt, expectedHash);
                                sw.Stop();
                                bool hit = MiniWebServer.Host.MiniAuth.UserStore.Login("victim", guess);
                                sb.Append($"  guess=\"{guess,-10}\"  -> {(hit ? "HIT" : "miss")}  ({sw.ElapsedMilliseconds:N0} ms per verify call)\n");
                            }
                            sb.Append("---\n");
                            sb.Append("OSEP §54.4: a 100k-iter PBKDF2 makes a 100k-guess attack take hours,\n");
                            sb.Append("not the milliseconds a fast SHA-256 verifier would.\n");
                            authBody = sb.ToString();
                            break;
                        }
                        case "clear":
                        {
                            MiniWebServer.Host.MiniAuth.UserStore.ClearForTests();
                            sb.Append("user store cleared.\n");
                            authBody = sb.ToString();
                            break;
                        }
                        default:
                        {
                            authBody = $"unknown scenario '{authScenario}' (try: register, login, hashattack, dictionary, clear)\n";
                            response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                                Encoding.UTF8.GetBytes(authBody));
                            break;
                        }
                    }
                    response ??= new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(authBody));
                    break;
                }
                default:
                {
                    authBody = $"unknown auth route '{routeName}' (try: /auth/register, /auth/login, /auth/dump, /auth/run)\n";
                    response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes(authBody));
                    break;
                }
            }
        }
        else if (parsedRequest.Path.StartsWith("/crypto/"))
        {
            // M23.2: AES-256-GCM at-rest encryption + tamper detection (OSEP §56.2 + §56.7).
            //   /crypto/keygen              rotate the in-memory key
            //   /crypto/encrypt?name=X&msg=Y   store slot X with plaintext msg Y (URL-decoded)
            //   /crypto/decrypt?name=X      retrieve + verify slot X
            //   /crypto/tamper-demo?name=X  flip 1 byte of the ciphertext, attempt decrypt
            //   /crypto/nonce-reuse-demo    encrypt two msgs with the same nonce → XOR trick
            //   /crypto/dump                list stored at-rest form (nonce + len + tag, no plaintext)
            var cryptoPath = parsedRequest.Path;
            int cQ = cryptoPath.IndexOf('?');
            string cryptoQuery = cQ >= 0 ? cryptoPath.Substring(cQ + 1) : "";
            string cryptoName = "";
            string cryptoMsg = "";
            foreach (var kv in cryptoQuery.Split('&'))
            {
                int eq = kv.IndexOf('=');
                if (eq <= 0) continue;
                var k = Uri.UnescapeDataString(kv.Substring(0, eq));
                var v = Uri.UnescapeDataString(kv.Substring(eq + 1));
                if      (k == "name") cryptoName = v;
                else if (k == "msg")  cryptoMsg = v;
            }
            string cryptoRoute = cryptoPath.Substring("/crypto/".Length);
            int cRouteQ = cryptoRoute.IndexOf('?');
            if (cRouteQ >= 0) cryptoRoute = cryptoRoute.Substring(0, cRouteQ);

            response = null!;
            string cryptoBody;
            try
            {
                switch (cryptoRoute)
                {
                    case "keygen":
                    {
                        var oldKey = MiniWebServer.Host.MiniCrypto.AtRestStore.CurrentKey;
                        MiniWebServer.Host.MiniCrypto.AtRestStore.RotateKey();
                        var newKey = MiniWebServer.Host.MiniCrypto.AtRestStore.CurrentKey;
                        cryptoBody = $"old key: {MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(oldKey)}\n" +
                                     $"new key: {MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(newKey)}\n" +
                                     $"OSEP §56.6: a key freshly chosen gives perfect forward secrecy for data not yet encrypted under it.\n";
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(cryptoBody));
                        break;
                    }
                    case "encrypt":
                    {
                        if (string.IsNullOrEmpty(cryptoName) || string.IsNullOrEmpty(cryptoMsg))
                        {
                            response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                                Encoding.UTF8.GetBytes("name and msg required\n"));
                            break;
                        }
                        var pt = Encoding.UTF8.GetBytes(cryptoMsg);
                        var slot = MiniWebServer.Host.MiniCrypto.AtRestStore.Put(cryptoName, pt);
                        cryptoBody = $"stored slot '{cryptoName}'\n" +
                                     $"  created    = {slot.CreatedAt:O}\n" +
                                     $"  nonce       = {MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(slot.Block.Nonce)}\n" +
                                     $"  ciphertext  = {MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(slot.Block.Ciphertext)}\n" +
                                     $"  auth tag    = {MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(slot.Block.Tag)}\n" +
                                     $"OSEP §56.7: only the ciphertext + tag + nonce are persisted; plaintext is not.\n";
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(cryptoBody));
                        break;
                    }
                    case "decrypt":
                    {
                        var pt = MiniWebServer.Host.MiniCrypto.AtRestStore.Get(cryptoName);
                        cryptoBody = $"decrypted slot '{cryptoName}':\n  plaintext = {Encoding.UTF8.GetString(pt)}\n";
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(cryptoBody));
                        break;
                    }
                    case "tamper-demo":
                    {
                        // Encrypt a known plaintext, flip 1 byte of the ciphertext, try to decrypt.
                        if (string.IsNullOrEmpty(cryptoName))
                        {
                            cryptoName = "tamper-target";
                        }
                        var slots = new System.Collections.Generic.List<MiniWebServer.Host.MiniCrypto.AtRestStore.Slot>(
                            MiniWebServer.Host.MiniCrypto.AtRestStore.AllSlots());
                        if (slots.Count != 1)
                        {
                            // No slot yet, or more than one \u2014 make it deterministic: clear and seed.
                            MiniWebServer.Host.MiniCrypto.AtRestStore.ClearForTests();
                            MiniWebServer.Host.MiniCrypto.AtRestStore.Put(cryptoName, Encoding.UTF8.GetBytes("transfer $100 to savings"));
                            slots = new System.Collections.Generic.List<MiniWebServer.Host.MiniCrypto.AtRestStore.Slot>(
                                MiniWebServer.Host.MiniCrypto.AtRestStore.AllSlots());
                        }
                        var realSlot = slots[0];
                        // Flip bit 5 of byte 7 of the ciphertext.
                        var brokenCt = (byte[])realSlot.Block.Ciphertext.Clone();
                        brokenCt[7] ^= 0x20;
                        var tamperedBlock = new MiniWebServer.Host.MiniCrypto.SymmetricCipher.EncryptedBlock(
                            realSlot.Block.Nonce, brokenCt, realSlot.Block.Tag);
                        cryptoBody = $"slot '{cryptoName}' ciphertext, byte 7 bit 5 flipped.\n" +
                                     $"now attempting decrypt with the original tag...\n";
                        try
                        {
                            var attempted = MiniWebServer.Host.MiniCrypto.SymmetricCipher.Decrypt(
                                tamperedBlock, MiniWebServer.Host.MiniCrypto.AtRestStore.CurrentKey);
                            // Should never reach here \u2014 GCM authenticates the ciphertext.
                            cryptoBody += $"UNEXPECTED: decryption succeeded with tampered ciphertext: {Encoding.UTF8.GetString(attempted)}\n";
                            response = new HttpResponse(500, "Internal Server Error", "text/plain; charset=UTF-8",
                                Encoding.UTF8.GetBytes(cryptoBody));
                        }
                        catch (System.Security.Cryptography.CryptographicException ex)
                        {
                            cryptoBody += $"caught CryptographicException: {ex.Message}\n" +
                                          $"OSEP \u00a756.4: AES-GCM tag mismatch fails closed. The flipped byte broke the auth tag.\n";
                            response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                                Encoding.UTF8.GetBytes(cryptoBody));
                        }
                        break;
                    }
                    case "nonce-reuse-demo":
                    {
                        // OSEP §56.6 / §56.5: reusing a (key, nonce) pair lets an
                        // attacker XOR two ciphertexts to get the XOR of the
                        // plaintexts. With one known plaintext, the other falls.
                        var key = MiniWebServer.Host.MiniCrypto.AtRestStore.CurrentKey;
                        var nonce = new byte[MiniWebServer.Host.MiniCrypto.SymmetricCipher.NonceLength];
                        // Use a fixed, all-zero nonce for this demo so the
                        // auth-tag check still passes (AesGcm doesn't reject
                        // reuse, only the application has to avoid it).
                        Array.Fill<byte>(nonce, 0x42);
                        var p1 = Encoding.UTF8.GetBytes("budget meeting 2026 income");
                        var p2 = Encoding.UTF8.GetBytes("budget meeting 2026 losses");
                        var c1 = MiniWebServer.Host.MiniCrypto.SymmetricCipher.EncryptWithFixedNonce(p1, key, nonce);
                        var c2 = MiniWebServer.Host.MiniCrypto.SymmetricCipher.EncryptWithFixedNonce(p2, key, nonce);
                        // c1 ⊕ c2 == p1 ⊕ p2 (because same keystream).
                        var xor_c = MiniWebServer.Host.MiniCrypto.SymmetricCipher.Xor(c1.Ciphertext, c2.Ciphertext);
                        var xor_p = MiniWebServer.Host.MiniCrypto.SymmetricCipher.Xor(p1, p2);
                        cryptoBody = "OSEP §56.5 / §56.6: nonce-reuse attack\n" +
                                     $"  p1: \"{Encoding.UTF8.GetString(p1)}\"\n" +
                                     $"  p2: \"{Encoding.UTF8.GetString(p2)}\"\n" +
                                     $"  ciphertext XOR  = {MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(xor_c)}\n" +
                                     $"  plaintext XOR  = {MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(xor_p)}\n" +
                                     $"  equal?         = {System.Linq.Enumerable.SequenceEqual(xor_c, xor_p)}\n" +
                                     "If an attacker learns p1, they recover p2 = (p1 ⊕ p2) ⊕ p1 in O(n).\n" +
                                     "Real systems generate a fresh nonce per encrypt call (we do by default).\n";
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(cryptoBody));
                        break;
                    }
                    case "rsa-keygen":
                    {
                        // M23.4 — public-key cryptography (OSEP §56.3). Generate a fresh
                        // RSA-2048 keypair. The public key is returned in SPKI DER + hex;
                        // the private key stays in process memory (OSEP §56.6 threat model).
                        var kp = MiniWebServer.Host.MiniCrypto.RsaSigner.Generate();
                        // Truncate the public-key hex for readability — the SPKI blob is
                        // ~294 bytes for RSA-2048. Show first 32 + last 32 chars.
                        string pubHex = MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(kp.PublicKey);
                        string pubHexShown = pubHex.Length <= 96 ? pubHex : $"{pubHex.Substring(0, 64)}...{pubHex.Substring(pubHex.Length - 64)}";
                        cryptoBody = $"public key (hex, SPKI/DER, length={kp.PublicKey.Length} bytes):\n" +
                                     $"  {pubHexShown}\n" +
                                     $"private key kept in process memory (OSEP §56.6: \"bet entirely on secrecy of the key\").\n" +
                                     "OSEP §57.3: distribute the public key via X.509 cert; we return raw SPKI.\n";
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(cryptoBody));
                        break;
                    }
                    case "sign":
                    {
                        // Sign msg with the in-memory private key. Returns hex signature.
                        if (string.IsNullOrEmpty(cryptoMsg))
                        {
                            response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                                Encoding.UTF8.GetBytes("msg required\n"));
                            break;
                        }
                        var sig = MiniWebServer.Host.MiniCrypto.RsaSigner.Sign(cryptoMsg);
                        cryptoBody = $"message: \"{cryptoMsg}\"\n" +
                                     $"signature (hex, {sig.Length} bytes):\n" +
                                     $"  {MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(sig)}\n" +
                                     $"OSEP §56.3: anyone with the public key can verify but only the holder\n" +
                                     $"of the private key can produce the signature.\n";
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(cryptoBody));
                        break;
                    }
                    case "verify":
                    {
                        // Verify a signature. ?msg=X&sig=Y  (or with optional &pubkey=HEX for an externally-distributed key).
                        if (string.IsNullOrEmpty(cryptoMsg))
                        {
                            response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                                Encoding.UTF8.GetBytes("msg and sig required\n"));
                            break;
                        }
                        string cryptoSig = "";
                        string cryptoPubkey = "";
                        // Re-parse to pull sig + pubkey out of the same query.
                        foreach (var kv2 in cryptoQuery.Split('&'))
                        {
                            int eq3 = kv2.IndexOf('=');
                            if (eq3 <= 0) continue;
                            var k3 = Uri.UnescapeDataString(kv2.Substring(0, eq3));
                            var v3 = Uri.UnescapeDataString(kv2.Substring(eq3 + 1));
                            if      (k3 == "sig")    cryptoSig = v3;
                            else if (k3 == "pubkey") cryptoPubkey = v3;
                        }
                        if (string.IsNullOrEmpty(cryptoSig))
                        {
                            response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                                Encoding.UTF8.GetBytes("sig query parameter required\n"));
                            break;
                        }
                        byte[] sigBytes = MiniWebServer.Host.MiniCrypto.SymmetricCipher.FromHex(cryptoSig);
                        byte[]? pubkeyBytes = string.IsNullOrEmpty(cryptoPubkey)
                            ? null
                            : MiniWebServer.Host.MiniCrypto.SymmetricCipher.FromHex(cryptoPubkey);
                        bool ok = MiniWebServer.Host.MiniCrypto.RsaSigner.Verify(cryptoMsg, sigBytes, pubkeyBytes);
                        cryptoBody = $"message: \"{cryptoMsg}\"\n" +
                                     $"signature length: {sigBytes.Length} bytes\n" +
                                     $"public key: {(pubkeyBytes is null ? "(in-memory)" : pubkeyBytes.Length + " bytes, externally supplied")}\n" +
                                     $"verify result: {(ok ? "OK" : "FAIL")}\n" +
                                     (ok
                                         ? "OSEP §56.3: signature matches the public key — caller is the signer.\n"
                                         : "OSEP §56.3: signature does NOT match — message tampered, wrong key, or wrong message.\n");
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(cryptoBody));
                        break;
                    }
                    case "import-pubkey":
                    {
                        // Adopt a different public key (server is now a verifier for someone
                        // else's keypair). Used by the cross-process verify demo.
                        if (string.IsNullOrEmpty(cryptoMsg))
                        {
                            response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                                Encoding.UTF8.GetBytes("pubkey (hex) required in msg parameter\n"));
                            break;
                        }
                        var pubkeyBytes = MiniWebServer.Host.MiniCrypto.SymmetricCipher.FromHex(cryptoMsg);
                        MiniWebServer.Host.MiniCrypto.RsaSigner.SetActivePublicKey(pubkeyBytes);
                        cryptoBody = $"set active public key to {pubkeyBytes.Length} bytes\n";
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(cryptoBody));
                        break;
                    }
                    case "handshake":
                    {
                        // M23.5 — simulated TLS-style handshake (OSEP §57.5).
                        // Two parties exchange nonces, derive a per-session symmetric key
                        // via HKDF-SHA256 (RFC 5869). Same primitive OpenSSL / SChannel use.
                        //
                        // We model client + server in the same process. The "transport" is
                        // just a pair of in-memory byte arrays the route manipulates.
                        var (clientNonce, serverNonce, sessionKey) = MiniWebServer.Host.MiniCrypto.Handshake.RunDemo();
                        // Now demonstrate: encrypt + decrypt a payload using the session key
                        // via the M23.2 AEAD (proves the handshake produced a usable key).
                        const string sample = "budget meeting 2026 Q3: 5% growth";
                        byte[] payload = System.Text.Encoding.UTF8.GetBytes(sample);
                        var enc = MiniWebServer.Host.MiniCrypto.SymmetricCipher.Encrypt(payload, sessionKey);
                        var dec = MiniWebServer.Host.MiniCrypto.SymmetricCipher.Decrypt(enc, sessionKey);
                        cryptoBody = "OSEP §57.5 handshake demo (simplified TLS-like):\n" +
                                     "  step 1: client  → ClientHello (random nonce)\n" +
                                     $"    client_nonce = {MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(clientNonce)}\n" +
                                     "  step 2: server  → ServerHello (random nonce)\n" +
                                     $"    server_nonce = {MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(serverNonce)}\n" +
                                     "  step 3: both    → HKDF-SHA256(shared_secret, clientNonce || serverNonce)\n" +
                                     $"    session_key  = {MiniWebServer.Host.MiniCrypto.SymmetricCipher.ToHex(sessionKey)}\n" +
                                     "  step 4:        → encrypt(payload, session_key) + decrypt (M23.2 AEAD)\n" +
                                     $"    plaintext    = {System.Text.Encoding.UTF8.GetString(dec)}\n" +
                                     "    (nonce + ciphertext + tag) is what would actually go on the wire.\n" +
                                     "OSEP §57.5: in real TLS the client also signs the handshake, but for\n" +
                                     "this lab we skip auth and just demonstrate the key derivation.\n";
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(cryptoBody));
                        break;
                    }
                    case "totp-demo":
                    {
                        // M23.6 — TOTP (OSEP §54.5 "what you have").
                        // RFC 6238 TOTP = HMAC-SHA256(shared-secret, floor(unix_time / 30))
                        // truncated to 6 digits. We compute + show the current code, then
                        // a window-skew to show ±1 step tolerance.
                        if (string.IsNullOrEmpty(cryptoMsg))
                        {
                            response = new HttpResponse(400, "Bad Request", "text/plain; charset=UTF-8",
                                Encoding.UTF8.GetBytes("secret (hex) and code required in msg parameter\n"));
                            break;
                        }
                        // msg is treated as the shared secret in hex form.
                        var secret = MiniWebServer.Host.MiniCrypto.SymmetricCipher.FromHex(cryptoMsg);
                        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var code = MiniWebServer.Host.MiniCrypto.Totp.Compute(secret, now);
                        bool verify = MiniWebServer.Host.MiniCrypto.Totp.Verify(secret, code, now);
                        bool verifyPrevStep = MiniWebServer.Host.MiniCrypto.Totp.Verify(secret, code, now - 30);   // ±1 step
                        bool verifyTwoSteps = MiniWebServer.Host.MiniCrypto.Totp.Verify(secret, code, now - 60);   // -2 steps
                        bool verifyWrong = MiniWebServer.Host.MiniCrypto.Totp.Verify(secret, code + 1, now);
                        cryptoBody = $"OSEP §54.5 — TOTP (auth by what-you-have):\n" +
                                     $"  now     = {now} (unix seconds)\n" +
                                     $"  step    = {now / 30} (TOTP step = 30 seconds, RFC 6238)\n" +
                                     $"  code    = {code:D6}  (6 digits, HMAC-SHA256 truncated)\n" +
                                     $"  verify(code, now)         = {verify}\n" +
                                     $"  verify(code, now-30s)     = {verifyPrevStep}  (window of \\u00b11 step, accept)\n" +
                                     $"  verify(code, now-60s)     = {verifyTwoSteps} (\\u00b12 steps, outside window, reject)\n" +
                                     $"  verify(code+1, now) wrong = {verifyWrong}\n" +
                                     $"OSEP §54.5: SMS / TOTP apps / YubiKeys all implement this with one of\n" +
                                     "three schemes (HOTP RFC 4226 + TOTP RFC 6238 + U2F FIDO).\n";
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(cryptoBody));
                        break;
                    }
                    case "dump":
                    {
                        var (n, lines) = MiniWebServer.Host.MiniCrypto.AtRestStore.DumpSummary();
                        var sb = new System.Text.StringBuilder();
                        sb.Append("slots: ").Append(n).Append('\n');
                        sb.Append("at-rest form: nonce + ciphertext + 128-bit auth tag (never plaintext)\n");
                        sb.Append("---\n");
                        foreach (var ln in lines) sb.Append(ln).Append('\n');
                        response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes(sb.ToString()));
                        break;
                    }
                    default:
                    {
                        response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8",
                            Encoding.UTF8.GetBytes($"unknown /crypto/* route '{cryptoRoute}'\n"));
                        break;
                    }
                }
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                // Auth-tag failure on a decrypt / tamper-demo — report as 401-style.
                response = new HttpResponse(401, "Unauthorized", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes($"decryption failed: {ex.Message}\n"));
            }
            catch (KeyNotFoundException ex)
            {
                response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes($"{ex.Message}\n"));
            }

            // Make sure response is always assigned (the compiler can't track it through
            // every switch arm under nested control flow + try/catch).
            response ??= new HttpResponse(500, "Internal Server Error", "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes("(crypto route fell through)\n"));
        }
        else if (parsedRequest.Path.StartsWith("/protected/"))
        {
            // M23.3 RBAC: gate /protected/* by Admin role. OSEP §55.4 RBAC "principle of
            // least privilege": only admin role can read /protected/secret.
            // Caller must prove identity via Basic-style query (?user=X&pass=Y).
            string protPath = parsedRequest.Path;
            int pQ = protPath.IndexOf('?');
            string protQuery = pQ >= 0 ? protPath.Substring(pQ + 1) : "";
            string protUser = "";
            string protPass = "";
            foreach (var kv in protQuery.Split('&'))
            {
                int eq = kv.IndexOf('=');
                if (eq <= 0) continue;
                var k = Uri.UnescapeDataString(kv.Substring(0, eq));
                var v = Uri.UnescapeDataString(kv.Substring(eq + 1));
                if      (k == "user") protUser = v;
                else if (k == "pass") protPass = v;
            }
            // The route body is the user's auth — we use AuthenticateWithRole to gate.
            bool ok = MiniWebServer.Host.MiniAuth.UserStore.AuthenticateWithRole(
                protUser, protPass, MiniWebServer.Host.MiniAuth.UserStore.Role.Admin);
            string routeTail = protPath.Substring("/protected/".Length);
            int pQ2 = routeTail.IndexOf('?');
            if (pQ2 >= 0) routeTail = routeTail.Substring(0, pQ2);
            if (!ok)
            {
                // OSEP §53.4: identical "denied" for unknown-user and wrong-password.
                response = new HttpResponse(403, "Forbidden", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes("denied: admin role required\n"));
            }
            else if (routeTail == "secret")
            {
                // OSEP §54.7 password-vault idea (mini): once the caller is admin, return
                // a synthesized secret they'd otherwise need to physically obtain.
                string body = $"OK admin={protUser} secret=\"the cake is a lie\"\n";
                response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes(body));
            }
            else
            {
                response = new HttpResponse(404, "Not Found", "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes($"unknown /protected/{routeTail}\n"));
            }
        }
        else if (parsedRequest.Path.StartsWith("/qstats"))
        {
            int q = WorkerPool.QueueLength;
            int w = WorkerPool.WorkerCount;
            int cap = WorkerPool.Capacity;
            bool atCap = WorkerPool.IsAtCapacity;
            string body =
                $"worker_count = {w}\n" +
                $"queue_length = {q}\n" +
                $"capacity = {cap}\n" +
                $"at_capacity = {atCap.ToString().ToLowerInvariant()}\n" +
                $"total_requests = {RequestStats.TotalRequests}\n";
            response = new HttpResponse(
                200,
                "OK",
                "text/plain; charset=UTF-8",
                Encoding.UTF8.GetBytes(body));
        }
        else if (parsedRequest.Path.StartsWith("/read-syscall"))
        {
            // Parse ?path= query. Path may be /read-syscall?path=foo.html
            string pathParam = "index.html";
            int qIdx = parsedRequest.Path.IndexOf('?');
            if (qIdx >= 0)
            {
                string qs = parsedRequest.Path.Substring(qIdx + 1);
                foreach (var kv in qs.Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && kv.Substring(0, eq) == "path")
                    {
                        pathParam = Uri.UnescapeDataString(kv.Substring(eq + 1));
                        break;
                    }
                }
            }
            string root = Path.GetFullPath(webRoot);
            string relativePath = pathParam.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath))
            {
                response = new HttpResponse(
                    404,
                    "Not Found",
                    "text/plain; charset=UTF-8",
                    Encoding.UTF8.GetBytes("Not Found\n"));
            }
            else
            {
                byte[] body = RawFileAccess.ReadAllBytesRaw(fullPath);
                string header = $"file: {pathParam}\nbytes: {body.Length}\n---\n";
                byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                var combined = new byte[headerBytes.Length + body.Length];
                Buffer.BlockCopy(headerBytes, 0, combined, 0, headerBytes.Length);
                Buffer.BlockCopy(body, 0, combined, headerBytes.Length, body.Length);
                response = new HttpResponse(
                    200,
                    "OK",
                    "text/plain; charset=UTF-8",
                    combined);
            }
        }
        else
        {
            response = StaticFileResponder.CreateResponse(parsedRequest, webRoot);
        }
        Console.WriteLine($"[thread {threadId}] Response: {response.StatusCode} {response.ReasonPhrase}");

        byte[] responseBytes = response.ToBytes();
        SendAll(clientSocket, responseBytes);

        Console.WriteLine($"[thread {threadId}] Sent {responseBytes.Length} response bytes.");
        Console.WriteLine($"[thread {threadId}] Closed client socket.");
    }
}

/// <summary>
/// Formats a Ch. 7 baseline run for <c>/scheduler/run?algo=baseline</c>.
/// Reports all four policies on one workload when the caller asked for none
/// in particular, because §7.10's point is the trade-off and a single policy's
/// number says nothing on its own.
/// </summary>
static string FormatBaselineSchedule(
    MiniWebServer.Host.MiniScheduler.BaselinePolicy policy,
    MiniWebServer.Host.MiniScheduler.ScheduleResult r,
    string workload,
    int quantum)
{
    var sb = new StringBuilder();
    sb.AppendLine($"=== {policy} on the '{workload}' workload (M35 / OSEP Ch. 7) ===");
    sb.AppendLine();
    sb.AppendLine("job |  len | arrival | first run | complete | turnaround | response");
    sb.AppendLine("----+------+---------+-----------+----------+------------+---------");
    // The per-job rows are rebuilt from the trace, which is the only place the
    // individual timings survive. Job identity comes from the trace; the metric
    // vectors come from `ScheduleResult`, which is ordered by *input* order.
    // Taking names from the trace's execution order and indexing `Response` with
    // that index silently pairs the wrong job's numbers whenever execution order
    // differs from input order - SJF on the convoy workload runs B, C, A and
    // printed B with A's response.
    //
    // `r.Arrivals` is keyed by name so the lookup cannot go wrong.
    var names = r.Trace.Where(t => t.Job != "-").Select(t => t.Job).Distinct().ToList();
    foreach (var name in names)
    {
        int firstRun = r.Trace.First(t => t.Job == name).Time;
        int completion = r.Trace.Last(t => t.Job == name).Time + 1;
        int length = r.Trace.Count(t => t.Job == name);
        int arrival = r.Arrivals.TryGetValue(name, out int a) ? a : firstRun;
        sb.AppendLine($"{name,3} | {length,4} | {arrival,7} | {firstRun,9} | {completion,8} | " +
                      $"{completion - arrival,10} | {firstRun - arrival,8}");
    }
    sb.AppendLine();
    sb.AppendLine($"avg turnaround: {r.AvgTurnaround:F2}   (eq 7.1, completion - arrival)");
    sb.AppendLine($"avg response:   {r.AvgResponse:F2}   (eq 7.2, first run - arrival)");
    sb.AppendLine($"avg wait:       {r.AvgWait:F2}");
    sb.AppendLine($"completion order: {r.CompletionOrder}   over {r.Makespan} ticks");
    sb.AppendLine();

    // The comparison table is the reason the slice exists: §7.10 summarises the
    // chapter as "The first runs the shortest job remaining and thus optimizes
    // turnaround time; the second alternates between all jobs and thus optimizes
    // response time. Both are bad where the other is good."
    //
    // RR's row uses the caller's quantum, not a hardcoded 1. Hardcoding it made
    // the table silently contradict the requested run's own numbers whenever
    // quantum != 1 - on the convoy workload, quantum 25 gives 66.67 turnaround
    // and 20.00 response against the hardcoded row's 59.67 and 1.00, so the
    // table would have shown the requested policy twice with different answers.
    var all = new (MiniWebServer.Host.MiniScheduler.BaselinePolicy P, string Name)[]
    {
        (MiniWebServer.Host.MiniScheduler.BaselinePolicy.Fifo, "FIFO"),
        (MiniWebServer.Host.MiniScheduler.BaselinePolicy.Sjf, "SJF"),
        (MiniWebServer.Host.MiniScheduler.BaselinePolicy.Stcf, "STCF"),
        (MiniWebServer.Host.MiniScheduler.BaselinePolicy.RoundRobin, "RR"),
    };
    sb.AppendLine($"all four policies on this workload (RR with quantum={quantum}):");
    sb.AppendLine("policy | avg turnaround | avg response | order");
    sb.AppendLine("-------+----------------+--------------+------");
    var source = MiniWebServer.Host.MiniScheduler.BaselineWorkload.FromName(workload);
    foreach (var (p, name) in all)
    {
        // Only RR has a time slice. Passing the caller's quantum to FIFO/SJF/STCF
        // makes `Run` reject the combination, which threw out of the formatter and
        // turned a valid `?policy=rr&quantum=25` request into a 400 - discarding a
        // run that had already succeeded.
        int rowQuantum = p == MiniWebServer.Host.MiniScheduler.BaselinePolicy.RoundRobin ? quantum : 1;
        var run = MiniWebServer.Host.MiniScheduler.BaselineScheduler.Run(p, source, rowQuantum);
        string mark = p == policy ? "  <- requested" : "";
        sb.AppendLine($"{name,-6} | {run.AvgTurnaround,14:F2} | {run.AvgResponse,12:F2} | {run.CompletionOrder}{mark}");
    }
    sb.AppendLine();
    sb.AppendLine("§7.10: \"The first runs the shortest job remaining and thus optimizes turnaround");
    sb.AppendLine("time; the second alternates between all jobs and thus optimizes response time.");
    sb.AppendLine("Both are bad where the other is good, alas, an inherent trade-off.\"");
    return sb.ToString();
}

/// <summary>
/// The M36 scenarios behind <c>/heap/run</c> (OSEP §17.2-§17.4).
/// </summary>
static HttpResponse BuildHeapResponse(string path)
{
    string scenario = "strategies";
    string policyName = "best";
    int heapSize = 4096;
    int requestSize = 15;
    int headerBytes = 8;
    bool coalesce = true;

    int qIdx = path.IndexOf('?');
    if (qIdx >= 0)
    {
        foreach (var kv in path.Substring(qIdx + 1).Split('&'))
        {
            int eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            var k = kv.Substring(0, eq);
            var v = kv.Substring(eq + 1);

            // The two string parameters are taken first. Parsing every value as an
            // integer before looking at the key rejects `scenario=strategies` with
            // "must be an integer", which is both wrong and unactionable.
            if (k == "scenario") { scenario = v; continue; }
            if (k == "policy") { policyName = v; continue; }

            if (!int.TryParse(v, out int n))
            {
                return HttpBad($"parameter '{k}' must be an integer, got '{v}'");
            }
            switch (k)
            {
                case "size": heapSize = n; break;
                case "request": requestSize = n; break;
                case "header": headerBytes = n; break;
                case "coalesce":
                    // "0" and "false" mean off; anything else is a client error
                    // rather than a silent truthy value.
                    if (v is not ("0" or "false" or "1" or "true"))
                        return HttpBad($"coalesce must be 0/1/true/false, got '{v}'");
                    coalesce = v is "1" or "true";
                    break;
                default:
                    return HttpBad($"unknown parameter '{k}'");
            }
        }
    }

    MiniWebServer.Host.MiniScheduler.FitPolicy? policy = policyName switch
    {
        "first" => MiniWebServer.Host.MiniScheduler.FitPolicy.First,
        "best" => MiniWebServer.Host.MiniScheduler.FitPolicy.Best,
        "worst" => MiniWebServer.Host.MiniScheduler.FitPolicy.Worst,
        "next" => MiniWebServer.Host.MiniScheduler.FitPolicy.Next,
        _ => null,
    };
    if (policy is null)
        return HttpBad($"unknown policy '{policyName}' (use first|best|worst|next)");

    if (heapSize < 1) return HttpBad($"size must be positive, got {heapSize}");
    if (requestSize < 1) return HttpBad($"request must be positive, got {requestSize}");

    string body;
    try
    {
        body = scenario switch
        {
            "split" => FormatHeapSplit(heapSize, requestSize, headerBytes, coalesce),
            "coalesce" => FormatHeapCoalesce(heapSize),
            "strategies" => FormatHeapStrategies(requestSize),
            "buddy" => FormatBuddy(requestSize, heapSize),
            _ => $"unknown scenario '{scenario}' (use split|coalesce|strategies|buddy)\n",
        };
    }
    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
    {
        // A non-power-of-two buddy heap, a header larger than the heap, a request
        // of zero: all client errors, not server errors.
        return HttpBad(ex.Message);
    }

    bool unknown = body.StartsWith("unknown scenario");
    return new HttpResponse(unknown ? 400 : 200, unknown ? "Bad Request" : "OK",
        "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(body));

    HttpResponse HttpBad(string message) =>
        new(400, "Bad Request", "text/plain; charset=UTF-8",
            Encoding.UTF8.GetBytes(message + "\n"));
}

/// <summary>§17.2: splitting and the header, on one heap.</summary>
static string FormatHeapSplit(int heapSize, int requestSize, int headerBytes, bool coalesce)
{
    // §17.2 embeds the free list inside the free space, so the node's own header is
    // unavailable to the caller: a 4096-byte heap starts with 4088 free, and a
    // 100-byte request shrinks that to 3980 ("4088 minus 108"). Without the node
    // header the arithmetic would read 3988 and quietly disagree with the chapter.
    var heap = MiniWebServer.Host.MiniScheduler.HeapAllocator.Heap(
        heapSize, MiniWebServer.Host.MiniScheduler.FitPolicy.First,
        headerBytes, coalesce, nodeHeaderBytes: headerBytes);
    var sb = new StringBuilder();
    sb.AppendLine($"=== Splitting a request (M36 / OSEP §17.2) ===");
    sb.AppendLine($"heap {heapSize} bytes, allocation header {headerBytes} bytes, coalesce={coalesce}");
    sb.AppendLine();
    sb.AppendLine($"start: {heap.FreeChunks.Count} free extent(s), {heap.FreeBytes} bytes");
    sb.AppendLine();

    long got = heap.Malloc(requestSize);
    sb.AppendLine($"malloc({requestSize}) -> {(got < 0 ? "NULL" : got.ToString())}");
    if (got < 0)
    {
        sb.AppendLine("  §17.2: a request that no single extent can satisfy fails even");
        sb.AppendLine("  when the total free space is larger than the request.");
    }
    else
    {
        int charged = requestSize + headerBytes;
        sb.AppendLine($"  the library charged {charged} bytes ({requestSize} + a {headerBytes}-byte header)");
        sb.AppendLine("  §17.2: \"the library does not search for a free chunk of size N; rather,");
        sb.AppendLine("  it searches for a free chunk of size N plus the size of the header.\"");
        sb.AppendLine();
        sb.AppendLine("free list:");
        foreach (var c in heap.FreeChunks)
            sb.AppendLine($"  addr:{c.Start} len:{c.Length}");
        sb.AppendLine();
        sb.AppendLine($"layout (1 char = 1 byte, # = allocated): {heap.FormatLayout(1)}");
        sb.AppendLine($"heap spans offsets 0..{heapSize - 1}");
    }
    return sb.ToString();
}

/// <summary>§17.2: the same heap with and without coalescing.</summary>
static string FormatHeapCoalesce(int heapSize)
{
    var sb = new StringBuilder();
    sb.AppendLine($"=== Coalescing (M36 / OSEP §17.2) ===");
    sb.AppendLine($"heap {heapSize} bytes, three 10-byte allocations then all freed");
    sb.AppendLine();

    foreach (bool coalesce in new[] { false, true })
    {
        var heap = MiniWebServer.Host.MiniScheduler.HeapAllocator.Heap(
            heapSize, MiniWebServer.Host.MiniScheduler.FitPolicy.First, coalesce: coalesce);
        var held = new List<long>();
        while (heap.CanSatisfy(10)) held.Add(heap.Malloc(10));
        foreach (long p in held) heap.Free(p);

        sb.AppendLine($"coalesce={coalesce}: {heap.FreeChunks.Count} extent(s), " +
                      $"{heap.FreeBytes} free, largest {heap.LargestFreeChunk}");
        foreach (var c in heap.FreeChunks) sb.AppendLine($"  addr:{c.Start} len:{c.Length}");
    }

    sb.AppendLine();
    sb.AppendLine("§17.2: \"If we simply add this free space back into our list without too much");
    sb.AppendLine("thinking, we might end up with a list that looks like this ... while the entire");
    sb.AppendLine("heap is now free, it is seemingly divided into three chunks ... with coalescing,");
    sb.AppendLine("our final list should look like this: head addr:0 len:30\"");
    return sb.ToString();
}

/// <summary>§17.3's 10/30/20 worked example under every policy.</summary>
static string FormatHeapStrategies(int requestSize)
{
    int[] sizes = { 10, 30, 20 };
    var sb = new StringBuilder();
    sb.AppendLine($"=== Fit policies on the chapter's list (M36 / OSEP §17.3) ===");
    sb.AppendLine($"free list 10/30/20, request {requestSize}");
    sb.AppendLine();
    sb.AppendLine("policy | chose offset | resulting extents        | wasted in splinters");
    sb.AppendLine("-------+--------------+--------------------------+--------------------");
    foreach (var (p, name) in new[]
    {
        (MiniWebServer.Host.MiniScheduler.FitPolicy.Best, "best"),
        (MiniWebServer.Host.MiniScheduler.FitPolicy.Worst, "worst"),
        (MiniWebServer.Host.MiniScheduler.FitPolicy.First, "first"),
        (MiniWebServer.Host.MiniScheduler.FitPolicy.Next, "next"),
    })
    {
        var heap = MiniWebServer.Host.MiniScheduler.HeapAllocator.FixedExtentHeap(sizes, p);
        long got = heap.Malloc(requestSize);
        string extents = string.Join(" ", heap.FreeChunks.Select(c => c.Length.ToString()));
        // A splinter is an extent too small to serve the next request of this size.
        int wasted = heap.FreeChunks.Where(c => c.Length < requestSize).Sum(c => c.Length);
        sb.AppendLine($"{name,-6} | {(got < 0 ? "NULL" : got.ToString()),12} | {extents,-24} | {wasted}");
    }
    sb.AppendLine();
    sb.AppendLine("§17.3: \"By returning a block that is close to what the user asks, best fit tries to");
    sb.AppendLine("reduce wasted space. However, there is a cost; naive implementations pay a heavy");
    sb.AppendLine("performance penalty when performing an exhaustive search for the correct free block.\"");
    sb.AppendLine();
    sb.AppendLine("§17.3 also notes: \"First fit has the advantage of speed ... but sometimes pollutes");
    sb.AppendLine("the beginning of the free list with small objects.\" The splinter column is that cost.");
    return sb.ToString();
}

/// <summary>§17.4's buddy split and the recursive coalesce back up.</summary>
static string FormatBuddy(int requestSize, int heapSize)
{
    // §17.4: "free memory is first conceptually thought of as one big space of
    // size 2^N", so a non-power-of-two heap is rejected by the allocator. The
    // default is the chapter's own 64 KB.
    var buddy = new MiniWebServer.Host.MiniScheduler.BuddyAllocator(
        heapSize == 4096 ? 64 * 1024 : heapSize);
    var sb = new StringBuilder();
    sb.AppendLine($"=== Buddy allocation (M36 / OSEP §17.4) ===");
    sb.AppendLine($"{buddy.HeapSize} byte heap, request {requestSize} bytes");
    sb.AppendLine();
    sb.AppendLine("64 KB");
    sb.AppendLine("  |-- 32 KB free");
    sb.AppendLine("  +-- 32 KB  (split for the request)");

    long got = buddy.Allocate(requestSize);
    sb.AppendLine();
    sb.AppendLine($"allocate({requestSize}) -> offset {got}, block {buddy.BlockSizeOf(got)} bytes");
    sb.AppendLine($"  internal fragmentation: {buddy.BlockSizeOf(got) - requestSize} bytes wasted inside the block");
    sb.AppendLine($"  §17.4: \"you are only allowed to give out power-of-two-sized blocks\"");
    sb.AppendLine();
    sb.AppendLine("free blocks after the split:");
    foreach (var (start, size) in buddy.FreeBlocks())
        sb.AppendLine($"  addr:{start} len:{size}");
    sb.AppendLine($"free total: {buddy.FreeBytes} bytes");
    sb.AppendLine();
    sb.AppendLine("buddy address = address XOR block size (§17.4: the pair \"only differs by a single bit\")");
    sb.AppendLine($"  buddy of addr:{got} len:{buddy.BlockSizeOf(got)} is addr:{MiniWebServer.Host.MiniScheduler.BuddyAllocator.BuddyAddress(got, buddy.BlockSizeOf(got))}");
    sb.AppendLine();
    sb.AppendLine("freeing both halves coalesces recursively back to one 64 KB extent:");
    long second = buddy.Allocate(buddy.BlockSizeOf(got));
    buddy.Free(got);
    if (second >= 0) buddy.Free(second);
    sb.AppendLine($"  free blocks: {buddy.FreeBlockCount}, free bytes: {buddy.FreeBytes}");
    return sb.ToString();
}

/// <summary>
/// The M34 scenarios behind <c>/device/run</c> (OSEP §36.2-§36.6).
/// </summary>
static HttpResponse BuildDeviceResponse(string path)
{
    string scenario = "pio-vs-dma";
    int transferBytes = 4096;
    int payloadSize = 256;
    int latencyTicks = 4;

    foreach (var kv in MiniWebServer.Host.MiniScheduler.FtlDemos.QueryParts(path))
    {
        switch (kv.Key)
        {
            case "scenario":
                scenario = kv.Value;
                break;
            case "transfer_bytes" or "bytes":
                if (!int.TryParse(kv.Value, out transferBytes) || transferBytes < 1)
                    return DeviceBadRequest($"transfer_bytes must be a positive integer, got '{kv.Value}'");
                break;
            case "payload":
                if (!int.TryParse(kv.Value, out payloadSize) || payloadSize < 1)
                    return DeviceBadRequest($"payload must be a positive integer, got '{kv.Value}'");
                break;
            case "latency" or "latency_ticks":
                if (!int.TryParse(kv.Value, out latencyTicks) || latencyTicks < 0)
                    return DeviceBadRequest($"latency must be a non-negative integer, got '{kv.Value}'");
                break;
        }
    }

// The scenarios that run a device size it from the requested transfer, so a
    // caller asking for a 4 KB transfer gets a device that can hold one. The
    // arithmetic-only scenarios never touch a device at all.
    if (scenario is "canonical-protocol" or "mmio")
    {
        if (payloadSize < transferBytes) payloadSize = transferBytes;
        // Both demonstrations read a fixed 16 bytes from block 0, so a device
        // smaller than that cannot serve them. Answering 400 beats letting the
        // read throw past the route.
        const int demonstrationRead = 16;
        if (payloadSize < demonstrationRead)
            return DeviceBadRequest($"payload must be at least {demonstrationRead} bytes for {scenario}, got {payloadSize}");
    }

    string body = scenario switch
    {
        "canonical-protocol" => MiniWebServer.Host.MiniScheduler.DeviceDemos.FormatCanonicalProtocol(payloadSize, latencyTicks),
        "pio-vs-dma" => MiniWebServer.Host.MiniScheduler.DeviceDemos.FormatPioVsDma(transferBytes),
        "interrupt-vs-poll" => MiniWebServer.Host.MiniScheduler.DeviceDemos.FormatInterruptVsPoll(latencyTicks),
        "mmio" => FormatMmioVsPorts(payloadSize, latencyTicks),
        _ => $"unknown device scenario '{scenario}' (use canonical-protocol, pio-vs-dma, interrupt-vs-poll, mmio)\n",
    };
    int status = body.StartsWith("unknown device scenario") ? 400 : 200;
    return new HttpResponse(status, status == 400 ? "Bad Request" : "OK",
        "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(body));

    static HttpResponse DeviceBadRequest(string message) =>
        new(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(message + "\n"));
}

/// <summary>
/// §36.6's two ways of reaching a device register, run side by side.
/// </summary>
static string FormatMmioVsPorts(int payloadSize, int latencyTicks)
{
    var mmio = new MiniWebServer.Host.MiniScheduler.Device(payloadSize, latencyTicks);
    var ports = new MiniWebServer.Host.MiniScheduler.Device(payloadSize, latencyTicks);
    for (int i = 0; i < payloadSize; i++)
    {
        mmio.BackingStore[i] = (byte)('A' + (i % 26));
        ports.BackingStore[i] = (byte)('A' + (i % 26));
    }

    const int readLength = 16;
    var viaMmio = mmio.CanonicalRead(0, readLength);
    var viaPorts = ports.CanonicalReadViaPorts(0, readLength);

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("=== Memory-mapped I/O vs explicit I/O instructions (M34 / OSEP §36.6) ===");
    sb.AppendLine($"both ran §36.3's four-step protocol for a {readLength}-byte read");
    sb.AppendLine();
    sb.AppendLine("access        | registers read | cpu cycles | data");
    sb.AppendLine("---------------|----------------|------------|------");
    sb.AppendLine($"memory-mapped | {mmio.PollsExecuted,14} | {mmio.CpuCycles,10} | {string.Join("", viaMmio.Select(b => (char)b))}");
    sb.AppendLine($"port I/O      | {ports.PollsExecuted,14} | {ports.CpuCycles,10} | {string.Join("", viaPorts.Select(b => (char)b))}");
    sb.AppendLine();
    bool same = viaMmio.SequenceEqual(viaPorts) && mmio.CpuCycles == ports.CpuCycles;
    sb.AppendLine($"identical result and identical cost: {(same ? "yes" : "NO")}");
    sb.AppendLine();
    sb.AppendLine("§36.6, verbatim: \"There is not some great advantage to one approach or the other.");
    sb.AppendLine("The memory-mapped approach is nice in that no new instructions are needed to support it,");
    sb.AppendLine("but both approaches are still in use today.\"");
    sb.AppendLine();
    sb.AppendLine("§36.6 also notes such instructions \"are usually privileged. The OS controls devices, and");
    sb.AppendLine("the OS thus is the only entity allowed to directly communicate with them.\"");
    return sb.ToString();
}

/// <summary>
/// The M33 scenarios behind <c>/integrity/run</c> (OSEP §45.7-§45.8).
/// </summary>
/// <remarks>
/// Kept out of the route body so the dispatcher stays readable, matching how
/// M31 and M32 keep their demo formatting next to the simulator it describes.
/// </remarks>
static HttpResponse BuildScrubResponse(string path, string scenario, int blocks, int blockSize)
{
    int batchSize = Math.Max(1, blocks / 4);
    int diskBlocks = Math.Max(1, blocks);
    double blockMtbfHours = 100_000;
    double intervalHours = 24;
    int injected = 3;

    // blockSize is only used by two of the three scenarios, but a non-positive
    // value would throw inside a constructor rather than reaching a report, so
    // it is rejected here where the caller can still answer 400.
    if (blockSize < 1)
        return BadRequest($"blockSize must be a positive integer, got '{blockSize}'");

    foreach (var kv in MiniWebServer.Host.MiniScheduler.FtlDemos.QueryParts(path))
    {
        switch (kv.Key)
        {
            case "batch_size" or "batchSize":
                if (!int.TryParse(kv.Value, out batchSize) || batchSize < 1)
                    return BadRequest($"batch_size must be a positive integer, got '{kv.Value}'");
                break;
            case "blocks" or "diskBlocks":
                if (!int.TryParse(kv.Value, out diskBlocks) || diskBlocks < 1)
                    return BadRequest($"blocks must be a positive integer, got '{kv.Value}'");
                break;
            case "interval_hours" or "intervalHours":
                // NaN and the infinities all fail these: a NaN interval would
                // make every candidate period NaN, and an infinite one would
                // print as a row of infinities.
                if (!double.TryParse(kv.Value, System.Globalization.CultureInfo.InvariantCulture, out intervalHours)
                    || double.IsNaN(intervalHours) || double.IsInfinity(intervalHours) || intervalHours <= 0)
                    return BadRequest($"interval_hours must be a positive finite number, got '{kv.Value}'");
                break;
            case "block_mtbf_hours" or "mtbf":
                // An infinite MTBF is meaningful and supported by the model -
                // a block that never fails is always caught - so only NaN and
                // the non-positive values are rejected here.
                if (!double.TryParse(kv.Value, System.Globalization.CultureInfo.InvariantCulture, out blockMtbfHours)
                    || double.IsNaN(blockMtbfHours) || blockMtbfHours <= 0)
                    return BadRequest($"block_mtbf_hours must be a positive number, got '{kv.Value}'");
                break;
            case "faults":
                if (!int.TryParse(kv.Value, out injected) || injected < 0)
                    return BadRequest($"faults must be a non-negative integer, got '{kv.Value}'");
                break;
        }
    }

    if (batchSize > diskBlocks)
        return BadRequest($"batch_size {batchSize} exceeds the {diskBlocks} blocks on the disk");

    // The weekly candidate is seven intervals long and a 1%-batch sweep is a
    // hundred intervals long; both can overflow to infinity for a large but
    // perfectly parseable interval, which would print as garbage rather than
    // fail. Preflight instead of formatting nonsense.
    double widestPeriod = intervalHours * Math.Max(1.0, Math.Ceiling(diskBlocks / (double)Math.Max(1, Math.Min(batchSize, diskBlocks))));
    if (double.IsInfinity(widestPeriod) || double.IsNaN(widestPeriod))
        return BadRequest($"interval_hours={intervalHours} with {diskBlocks} blocks overflows the sweep period");

    string body = scenario switch
    {
        "scrub-schedule" => MiniWebServer.Host.MiniScheduler.Scrubber.FormatScheduleComparison(
            diskBlocks, blockMtbfHours, BuildCandidates(diskBlocks, intervalHours)),
        "scrub-sweep" => RunScrubSweep(diskBlocks, blockSize, batchSize, injected),
        _ => FormatChecksumOverhead(blockSize),
    };
    return new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(body));

    static HttpResponse BadRequest(string message) =>
        new(400, "Bad Request", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(message + "\n"));
}

/// <summary>
/// The schedules §45.7 names, plus the smaller-batch variants a real system
/// picks to bound how much work one background pass does.
/// </summary>
static (string, double, int)[] BuildCandidates(int blocks, double intervalHours)
{
    int whole = blocks;
    // §45.7 names both cadences: "Typical systems schedule scans on a nightly
    // or weekly basis". Weekly is seven times the nightly interval - not seven
    // divided by twenty-four, which would make the "weekly" row the most
    // frequent one on the table.
    double weekly = intervalHours * 7;
    return new (string, double, int)[]
    {
        ($"{intervalHours:g}h, whole disk", intervalHours, whole),
        (weekly >= 1000 ? "weekly, whole disk" : $"{weekly:g}h, whole disk", weekly, whole),
        ($"{intervalHours:g}h, quarter disk", intervalHours, Math.Max(1, whole / 4)),
        ($"{intervalHours:g}h, 1% of disk", intervalHours, Math.Max(1, whole / 100)),
    };
}

/// <summary>
/// Drive a real scrubber over a disk with injected faults, showing which pass
/// finds each one.
/// </summary>
static string RunScrubSweep(int blocks, int blockSize, int batchSize, int faults)
{
    var store = new MiniWebServer.Host.MiniScheduler.IntegrityStore(diskId: 0, blocks: blocks, blockSize: blockSize);
    // The payload has to fit the block: IntegrityStore rejects a write longer
    // than the block, and a one-byte block would otherwise take the route down
    // with an exception instead of a report.
    var seed = new byte[Math.Min(2, blockSize)];
    for (int i = 0; i < blocks; i++)
    {
        for (int b = 0; b < seed.Length; b++) seed[b] = (byte)('A' + ((i + b) % 26));
        store.Write(i, seed);
    }

    // Spread the faults so they land in different passes.
    var injected = new List<int>();
    int step = Math.Max(1, blocks / Math.Max(1, faults));
    for (int f = 0; f < faults && f * step < blocks; f++)
    {
        int b = (f * step + blockSize / 2) % blocks;
        store.InjectCorruption(b, 0, (byte)(1 << (f % 8)));
        injected.Add(b);
    }

    var scrubber = new MiniWebServer.Host.MiniScheduler.Scrubber(store, batchSize);
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("=== Incremental scrub sweep (M33 / OSEP §45.7) ===");
    sb.AppendLine($"{blocks} blocks, batch {batchSize}, {injected.Count} faults injected at {string.Join(", ", injected)}");
    sb.AppendLine();
    sb.AppendLine("pass | cursor before | blocks | ok | bad | found");
    sb.AppendLine("-----|---------------|--------|----|-----|------");
    int passes = (blocks + batchSize - 1) / batchSize;
    for (int p = 1; p <= passes; p++)
    {
        int before = scrubber.Cursor;
        var report = scrubber.ScrubBatch();
        sb.AppendLine($"{p,4} | {before,13} | {batchSize,6} | {report.OkCount,2} | {report.BadCount,3} | {string.Join(" ", report.BadBlocks.Select(x => x.BlockId))}");
    }

    sb.AppendLine();
    sb.AppendLine($"after {passes} pass(es): {scrubber.BlocksScrubbed} block reads over a {blocks}-block disk.");
    if (scrubber.BlocksScrubbed > blocks)
    {
        // A batch is always a whole batch, so a disk whose size is not a
        // multiple of the batch wraps and re-reads the head of the disk on the
        // final pass. Saying "exactly once" would be false here.
        sb.AppendLine($"The last pass wrapped: {scrubber.BlocksScrubbed - blocks} block(s) were read twice rather than");
        sb.AppendLine($"the disk ending mid-batch. Use batch_size dividing {blocks} for a sweep with no repeats.");
    }
    else
    {
        sb.AppendLine("Every block was covered exactly once.");
    }
    sb.AppendLine();
    sb.AppendLine("§45.7: \"By periodically reading through every block of the system, and checking");
    sb.AppendLine("whether checksums are still valid, the disk system can reduce the chances that");
    sb.AppendLine("all copies of a certain data item become corrupted.\" A batched pass that kept");
    sb.AppendLine("restarting at block 0 would never reach the tail of the disk.");
    return sb.ToString();
}

/// <summary>
/// The §45.8 overhead figures, including the chapter's own rounding.
/// </summary>
static string FormatChecksumOverhead(int blockSize)
{
    double pct = MiniWebServer.Host.MiniScheduler.ChecksumOverhead.SpacePercent(
        MiniWebServer.Host.MiniScheduler.ChecksumOverhead.TypicalChecksumBytes, blockSize);
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("=== Checksumming overhead (M33 / OSEP §45.8) ===");
    sb.AppendLine($"block size: {blockSize} bytes, checksum: {MiniWebServer.Host.MiniScheduler.ChecksumOverhead.TypicalChecksumBytes} bytes");
    sb.AppendLine($"on-disk overhead at this block size: {pct:F4}%");
    sb.AppendLine();
    sb.AppendLine("§45.8, verbatim: \"A typical ratio might be an 8-byte checksum per 4 KB data");
    sb.AppendLine("block, for a 0.19% on-disk space overhead.\"");
    sb.AppendLine();
    sb.AppendLine($"At the chapter's 4 KB block that ratio is exactly 8/4096 = {100.0 * 8 / 4096:F6}%,");
    sb.AppendLine("which the text prints as 0.19%.");
    sb.AppendLine();
    sb.AppendLine("§45.8 on time: \"the CPU must compute the checksum over each block, both when");
    sb.AppendLine("the data is stored ... and when it is accessed\". Space overheads are small; the");
    sb.AppendLine("time cost, and the I/O of background scrubbing, are what the schedule has to trade.");
    return sb.ToString();
}

static byte[] ReceiveRequest(Socket clientSocket)
{
    byte[] buffer = new byte[MaxRequestBytes];
    int total = 0;
    int headerEnd = -1;
    int contentLength = 0;
    int totalReceiveCalls = 0;

    while (total < MaxRequestBytes)
    {
        int n = clientSocket.Receive(buffer, total, buffer.Length - total, SocketFlags.None);
        totalReceiveCalls++;
        if (n <= 0)
        {
            break;
        }
        total += n;

        if (headerEnd < 0)
        {
            headerEnd = HttpRequestReceiver.FindHeaderEnd(buffer.AsSpan(0, total));
        }
        if (headerEnd >= 0 && contentLength == 0)
        {
            contentLength = HttpRequestReceiver.ParseContentLength(buffer.AsSpan(0, headerEnd));
        }

        if (headerEnd >= 0)
        {
            int needed = headerEnd + HttpRequestReceiver.HeaderDelimiter.Length + contentLength;
            if (total >= needed)
            {
                Console.WriteLine($"Receive() returned {n} byte(s) on call #{totalReceiveCalls}; total {total} byte(s); request complete.");
                return buffer.AsSpan(0, needed).ToArray();
            }
        }
    }

    throw new InvalidOperationException(
        $"Request incomplete after {totalReceiveCalls} receive call(s) and {total} byte(s); headerEnd={headerEnd}, contentLength={contentLength}.");
}

static void SendAll(Socket clientSocket, byte[] responseBytes)
{
    int totalSent = 0;

    while (totalSent < responseBytes.Length)
    {
        int sent = clientSocket.Send(
            responseBytes,
            totalSent,
            responseBytes.Length - totalSent,
            SocketFlags.None);

        if (sent == 0)
        {
            throw new SocketException((int)SocketError.ConnectionReset);
        }

        totalSent += sent;
    }
}

// Shared across every handler thread inside this process. Local variables
// inside `HandleClient` stay per-thread on the handler's stack; a static
// field lives once and is visible to every thread that reaches it.
public static class RequestStats
{
    public static int TotalRequests = 0;

    // Shared across every handler thread. Intentionally non-atomic.
    // Two concurrent /race handlers will increment this with bare ++
    // and produce totals lower than 2 * RaceIterations. The lesson is
    // the race, not the fix. Milestone 5 introduces the lock fix.
    public static int UnsafeCounter = 0;

    // Lock-protected counter. Each handler enters the lock, increments
    // under the lock, and exits. Same shared-memory footprint as
    // UnsafeCounter but the critical section is bounded by the lock,
    // so concurrent /race-safe jobs always produce the deterministic
    // expected total.
    public static readonly object SafeCounterLock = new();
    public static int SafeCounter = 0;
}
