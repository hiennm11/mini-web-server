# Raw Socket Server Milestone 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve the current single-threaded raw socket server so request receiving, connection handling, and learning feedback are clearer while preserving its low-level educational shape.

**Architecture:** Keep the host as one console executable using `System.Net.Sockets.Socket` directly. Extract tiny helper methods inside `Program.cs` only when they make the socket lifecycle easier to see: create server socket, receive raw request bytes, create raw HTTP response bytes, send all response bytes, and close the client socket.

**Tech Stack:** .NET 10 console app, C#, `System.Net.Sockets.Socket`, `System.Text.Encoding`, PowerShell, `curl`.

---

## File Structure

- Modify: `src/MiniWebServer.Host/Program.cs`
  - Owns the listening server socket and single-threaded accept loop.
  - Adds small helpers for receiving a request, sending a complete response, and handling one client socket.
  - Keeps comments educational and close to OSTEP/.NET concepts.
- No new test project for this milestone.
  - Verification is manual and observable: `dotnet build`, `dotnet run`, `curl`, and console logs.

## Task 1: Make The Socket Lifecycle Explicit

**Files:**
- Modify: `src/MiniWebServer.Host/Program.cs`

- [ ] **Step 1: Replace the current top-level implementation with explicit constants and helper boundaries**

Update `src/MiniWebServer.Host/Program.cs` to this complete content:

```csharp
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
    HandleClient(clientSocket);
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
```

- [ ] **Step 2: Build the solution**

Run:

```powershell
dotnet build MiniWebServer.sln
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 3: Commit the lifecycle cleanup**

Run:

```powershell
git add src/MiniWebServer.Host/Program.cs
git commit -m "Improve raw socket server lifecycle"
```

Expected: a commit containing only `src/MiniWebServer.Host/Program.cs`.

## Task 2: Add Minimal Socket Error Handling

**Files:**
- Modify: `src/MiniWebServer.Host/Program.cs`

- [ ] **Step 1: Wrap each client connection so one failed client does not stop the host**

In `src/MiniWebServer.Host/Program.cs`, replace the accept loop:

```csharp
while (true)
{
    Socket clientSocket = serverSocket.Accept();
    HandleClient(clientSocket);
}
```

with:

```csharp
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
```

- [ ] **Step 2: Build the solution**

Run:

```powershell
dotnet build MiniWebServer.sln
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 3: Commit the error handling**

Run:

```powershell
git add src/MiniWebServer.Host/Program.cs
git commit -m "Handle per-client socket errors"
```

Expected: a commit containing only `src/MiniWebServer.Host/Program.cs`.

## Task 3: Verify The Learning Behavior

**Files:**
- No file changes.

- [ ] **Step 1: Start the server**

Run:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Expected console output includes:

```text
Host process id: <number>
Server socket listening on http://localhost:8080/
Waiting inside Accept(). Press Ctrl+C to stop.
```

Learning observation:

```text
Before any browser or curl request arrives, the process is alive but blocked in Accept().
OSTEP concept: the host process is running until it reaches blocking I/O, then the OS parks that thread until a client connection arrives.
.NET concept: Socket.Accept() blocks the current managed thread, which is backed by an OS thread.
```

- [ ] **Step 2: Send one request from a second terminal**

Run:

```powershell
curl http://localhost:8080/
```

Expected client output:

```text
Hello World!
```

Expected server output includes:

```text
Accepted client socket from <endpoint>
Receive() returned <number> byte(s).
Raw HTTP request bytes decoded as UTF-8:
GET / HTTP/1.1
Sent <number> response bytes.
Closed client socket.
```

- [ ] **Step 3: Send a path request from the second terminal**

Run:

```powershell
curl http://localhost:8080/ostep
```

Expected client output:

```text
Hello World!
```

Expected server output includes a raw request line similar to:

```text
GET /ostep HTTP/1.1
```

Learning observation:

```text
The current server does not route yet. It receives the path as raw request text, logs it, and always returns the same response.
This prepares Milestone 2, where the server will parse method, path, version, and headers.
```

- [ ] **Step 4: Stop the server**

Press `Ctrl+C` in the server terminal.

Expected: the host process stops.

## Task 4: Capture Milestone 1 Notes

**Files:**
- Create: `docs/learning/milestone-1-raw-socket-server.md`

- [ ] **Step 1: Create the learning notes file**

Create `docs/learning/milestone-1-raw-socket-server.md` with this content:

```markdown
# Milestone 1: Raw Socket Server

## What We Built

The host process creates a TCP server socket, binds it to port `8080`, listens for pending connections, blocks in `Accept()`, receives raw request bytes, sends a manually formatted HTTP response, and closes the client socket.

## Generic OS Concept

A server is a long-running process that asks the operating system for network resources. Calls such as bind, listen, accept, receive, and send cross the boundary between application code and the OS network stack.

`Accept()` is blocking I/O. When no client is waiting, the server thread cannot keep executing user code. The OS waits for a network event and schedules other runnable work until a client connection arrives.

## C#/.NET Concept

`System.Net.Sockets.Socket` exposes TCP socket operations directly. `Socket.Accept()` blocks the current managed thread. `Socket.Receive()` copies bytes from the socket into a `byte[]` buffer. `Encoding.UTF8.GetString(...)` turns those bytes into text for learning and logging.

HTTP is written manually as bytes:

- status line
- headers
- blank line
- body

`Content-Length` must match the byte length of the response body.

## Experiment

Run the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Then send a request:

```powershell
curl http://localhost:8080/
```

Before the request arrives, the server waits inside `Accept()`. After the request arrives, the server receives raw bytes, logs them, sends `Hello World!`, closes the client socket, and returns to `Accept()`.

## Next Question

The server can see request text such as `GET /ostep HTTP/1.1`, but it does not understand it yet. Milestone 2 will parse the raw HTTP request into method, path, version, and headers.
```

- [ ] **Step 2: Build the solution**

Run:

```powershell
dotnet build MiniWebServer.sln
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 3: Commit the learning notes**

Run:

```powershell
git add docs/learning/milestone-1-raw-socket-server.md
git commit -m "Add raw socket server learning notes"
```

Expected: a commit containing only `docs/learning/milestone-1-raw-socket-server.md`.

## Self-Review

- Spec coverage: this plan covers Milestone 1 from the approved learning design: better receive buffer, clearer logging, simple error handling, clean connection lifecycle, and an experiment showing blocking `Accept()` behavior.
- Deferred intentionally: HTTP parsing, static files, concurrency, race conditions, thread pools, and async sockets belong to later milestones.
- Verification: `dotnet build`, `dotnet run`, and `curl` are enough for this milestone because no pure parsing logic has been extracted yet.

