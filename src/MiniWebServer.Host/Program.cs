using System.Net;
using System.Net.Sockets;
using System.Text;

const int Port = 8080;
const int ListenBacklog = 10;
const int ReceiveBufferSize = 4096;

string webRoot = WebRootLocator.GetWebRoot(AppContext.BaseDirectory);

using Socket serverSocket = new(
    AddressFamily.InterNetwork,
    SocketType.Stream,
    ProtocolType.Tcp);

IPEndPoint endpoint = new(IPAddress.Any, Port);

serverSocket.Bind(endpoint);
serverSocket.Listen(ListenBacklog);

Console.WriteLine($"Host process id: {Environment.ProcessId}");
Console.WriteLine($"Server socket listening on http://localhost:{Port}/");
Console.WriteLine("Waiting inside Accept(). Press Ctrl+C to stop.");

while (true)
{
    Socket clientSocket = serverSocket.Accept();

    var thread = new Thread(() =>
    {
        try
        {
            HandleClient(clientSocket, webRoot);
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[thread {Thread.CurrentThread.ManagedThreadId}] Socket error while handling client: {ex.SocketErrorCode}");
            clientSocket.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[thread {Thread.CurrentThread.ManagedThreadId}] Unexpected error while handling client: {ex.Message}");
            clientSocket.Dispose();
        }
    });
    thread.IsBackground = true;
    thread.Start();
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

        string request = ReceiveRequest(clientSocket);
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
            Console.WriteLine($"[thread {threadId}] Sleeping 5000 ms to simulate blocking I/O...");
            Thread.Sleep(5000);
        }

        HttpResponse response = StaticFileResponder.CreateResponse(parsedRequest, webRoot);
        Console.WriteLine($"[thread {threadId}] Response: {response.StatusCode} {response.ReasonPhrase}");

        byte[] responseBytes = response.ToBytes();
        SendAll(clientSocket, responseBytes);

        Console.WriteLine($"[thread {threadId}] Sent {responseBytes.Length} response bytes.");
        Console.WriteLine($"[thread {threadId}] Closed client socket.");
    }
}

static string ReceiveRequest(Socket clientSocket)
{
    byte[] buffer = new byte[ReceiveBufferSize];
    int bytesRead = clientSocket.Receive(buffer);

    Console.WriteLine($"Receive() returned {bytesRead} byte(s).");

    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
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
}
