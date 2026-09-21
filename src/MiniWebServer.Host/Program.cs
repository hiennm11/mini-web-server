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
        else if (parsedRequest.Path.StartsWith("/scheduler/run"))
        {
            // MiniScheduler demo. ?algo=mlfq|stride|lottery&workload=two|cpu|mixed|proportional&ticks=N&q=N&boost=M
            string algo = "mlfq";
            string workload = "mixed";
            int totalTicks = 80;
            int numQueues = 4;
            int boostEvery = 50;
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
                    else if (k == "workload") workload = v;
                    else if (k == "ticks" && int.TryParse(v, out var t)) totalTicks = t;
                    else if (k == "q" && int.TryParse(v, out var qn)) numQueues = qn;
                    else if (k == "boost" && int.TryParse(v, out var b)) boostEvery = b;
                }
            }

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
