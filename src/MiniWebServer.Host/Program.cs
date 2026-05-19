using System.Net;
using System.Net.Sockets;
using System.Text;

const int Port = 8080;
const int ListenBacklog = 10;
const int ReceiveBufferSize = 4096;
const string ResponseBody = "Hello World!";

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

    try
    {
        HandleClient(clientSocket);
    }
    catch (SocketException ex)
    {
        Console.WriteLine($"Socket error while handling client: {ex.SocketErrorCode}");
        clientSocket.Dispose();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unexpected error while handling client: {ex.Message}");
        clientSocket.Dispose();
    }
}

static void HandleClient(Socket clientSocket)
{
    using (clientSocket)
    {
        Console.WriteLine();
        Console.WriteLine($"Accepted client socket from {clientSocket.RemoteEndPoint}");

        string request = ReceiveRequest(clientSocket);
        Console.WriteLine("Raw HTTP request bytes decoded as UTF-8:");
        Console.WriteLine(request);

        HttpRequest parsedRequest = HttpRequestParser.Parse(request);
        Console.WriteLine("Parsed HTTP request:");
        Console.WriteLine($"Method: {parsedRequest.Method}");
        Console.WriteLine($"Path: {parsedRequest.Path}");
        Console.WriteLine($"Version: {parsedRequest.Version}");
        Console.WriteLine($"Headers: {parsedRequest.Headers.Count}");

        byte[] responseBytes = CreateHttpResponse(ResponseBody);
        SendAll(clientSocket, responseBytes);

        Console.WriteLine($"Sent {responseBytes.Length} response bytes.");
        Console.WriteLine("Closed client socket.");
    }
}

static string ReceiveRequest(Socket clientSocket)
{
    byte[] buffer = new byte[ReceiveBufferSize];
    int bytesRead = clientSocket.Receive(buffer);

    Console.WriteLine($"Receive() returned {bytesRead} byte(s).");

    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
}

static byte[] CreateHttpResponse(string body)
{
    byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

    string headers =
        "HTTP/1.1 200 OK\r\n" +
        "Content-Type: text/plain; charset=UTF-8\r\n" +
        $"Content-Length: {bodyBytes.Length}\r\n" +
        "Connection: close\r\n" +
        "\r\n";

    byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
    byte[] responseBytes = new byte[headerBytes.Length + bodyBytes.Length];

    Buffer.BlockCopy(headerBytes, 0, responseBytes, 0, headerBytes.Length);
    Buffer.BlockCopy(bodyBytes, 0, responseBytes, headerBytes.Length, bodyBytes.Length);

    return responseBytes;
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
