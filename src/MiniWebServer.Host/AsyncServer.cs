using System.Net;
using System.Net.Sockets;
using System.Text;
using MiniWebServer.Host;

/// <summary>
/// Async / event-based server. A single OS thread runs the accept loop
/// via <see cref="Socket.AcceptAsync"/>. Each accepted connection is
/// handled by a <see cref="Task"/> that drives async I/O with
/// <see cref="Socket.ReceiveAsync"/> and <see cref="Socket.SendAsync"/>.
/// No worker pool, no per-connection OS thread. Equivalent to the
/// OSEP §33 single-CPU event-loop server, translated to .NET async
/// idioms.
/// </summary>
public static class AsyncServer
{
    public static async Task RunAsync(int port, string webRoot, CancellationToken ct)
    {
        using var serverSocket = new Socket(
            AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        var endpoint = new IPEndPoint(IPAddress.Any, port);
        serverSocket.Bind(endpoint);
        serverSocket.Listen(10);

        Console.WriteLine($"[async] Server socket listening on http://localhost:{port}/");
        Console.WriteLine("[async] Accept loop is single-threaded. Press Ctrl+C to stop.");

        while (!ct.IsCancellationRequested)
        {
            Socket clientSocket;
            try
            {
                clientSocket = await serverSocket.AcceptAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Fire-and-forget handler. Each connection is one Task;
            // the OS thread count does not grow with the connection
            // count. The Task may park on async I/O; another connection
            // can be processed on the same thread while it is parked.
            _ = HandleClientAsync(clientSocket, webRoot, ct);
        }
    }

    private static async Task HandleClientAsync(Socket clientSocket, string webRoot, CancellationToken ct)
    {
        using (clientSocket)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            int localRequestId = Interlocked.Increment(ref RequestStats.TotalRequests);

            Console.WriteLine($"[async] [thread {threadId}] Accepted client from {clientSocket.RemoteEndPoint}");

            try
            {
                // Use HttpRequestReceiver helpers to determine request
                // completeness, but loop via ReceiveAsync so we never
                // block the event thread.
                byte[] buffer = new byte[ServerConfig.MaxRequestBytes];
                int total = 0;
                int headerEnd = -1;
                int contentLength = 0;

                while (total < ServerConfig.MaxRequestBytes && !ct.IsCancellationRequested)
                {
                    int readBytes = await clientSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer, total, buffer.Length - total),
                        SocketFlags.None);

                    if (readBytes <= 0) break;
                    total += readBytes;

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
                        if (total >= needed) break;
                    }
                }

                if (total == 0)
                {
                    return;
                }

                byte[] requestBytes = buffer.AsSpan(0, total).ToArray();
                string request = Encoding.UTF8.GetString(requestBytes);

                HttpRequest parsedRequest = HttpRequestParser.Parse(request);
                Console.WriteLine($"[async] [thread {threadId}] Path: {parsedRequest.Path}");

                if (parsedRequest.Path == "/slow")
                {
                    Console.WriteLine($"[async] [thread {threadId}] Sleeping 30000 ms to simulate blocking I/O...");
                    await Task.Delay(30000, ct);
                }

                HttpResponse response;
                if (parsedRequest.Path == "/race")
                {
                    Console.WriteLine($"[async] [thread {threadId}] Running {ServerConfig.RaceIterations} non-atomic increments on RequestStats.UnsafeCounter...");
                    for (int i = 0; i < ServerConfig.RaceIterations; i++)
                    {
                        RequestStats.UnsafeCounter++;
                    }
                    int observed = RequestStats.UnsafeCounter;
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes($"UnsafeCounter = {observed}\n"));
                }
                else if (parsedRequest.Path == "/race-safe")
                {
                    int observed;
                    lock (RequestStats.SafeCounterLock)
                    {
                        for (int i = 0; i < ServerConfig.RaceIterations; i++)
                        {
                            RequestStats.SafeCounter++;
                        }
                        observed = RequestStats.SafeCounter;
                    }
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8",
                        Encoding.UTF8.GetBytes($"SafeCounter = {observed}\n"));
                }
                else if (parsedRequest.Path == "/stats")
                {
                    var p = System.Diagnostics.Process.GetCurrentProcess();
                    ThreadPool.GetMinThreads(out var tpMin, out _);
                    ThreadPool.GetMaxThreads(out var tpMax, out _);
                    ThreadPool.GetAvailableThreads(out var tpAvail, out _);
                    int tpActive = tpMax - tpAvail;
                    string body =
                        $"threads = {p.Threads.Count}\n" +
                        $"working_set_bytes = {p.WorkingSet64}\n" +
                        $"private_bytes = {p.PrivateMemorySize64}\n" +
                        $"threadpool_min = {tpMin}\n" +
                        $"threadpool_max = {tpMax}\n" +
                        $"threadpool_active = {tpActive}\n" +
                        $"total_requests = {RequestStats.TotalRequests}\n" +
                        $"async_active_tasks = {localRequestId}\n";
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(body));
                }
                else if (parsedRequest.Path == "/qstats")
                {
                    string body =
                        $"worker_count = 0 (async mode)\n" +
                        $"queue_length = 0 (async mode)\n" +
                        $"total_requests = {RequestStats.TotalRequests}\n";
                    response = new HttpResponse(200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(body));
                }
                else
                {
                    response = StaticFileResponder.CreateResponse(parsedRequest, webRoot);
                }

                byte[] responseBytes = response.ToBytes();
                await clientSocket.SendAsync(
                    new ArraySegment<byte>(responseBytes),
                    SocketFlags.None);

                Console.WriteLine($"[async] [thread {threadId}] Sent {responseBytes.Length} response bytes.");
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[async] [thread {threadId}] Error: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}