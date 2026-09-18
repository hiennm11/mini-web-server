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

MiniWebServer.Host.MiniFs.MiniFs.Mount();
Console.WriteLine($"[minifs] mounted: {MiniWebServer.Host.MiniFs.MiniFs.Superblock.NumDataBlocks} data blocks free of {MiniWebServer.Host.MiniFs.MiniFs.Superblock.TotalBlocks} total blocks");

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
        else if (parsedRequest.Path == "/qstats")
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
