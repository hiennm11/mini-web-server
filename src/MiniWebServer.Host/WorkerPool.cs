using System.Net.Sockets;

/// <summary>
/// Fixed-size pool of worker threads. The accept loop hands each accepted
/// client <c>Socket</c> to <see cref="TryEnqueue"/>; an idle worker thread
/// wakes up via <see cref="Monitor.Wait(object)"/> (the .NET equivalent of
/// <c>pthread_cond_wait</c>) and runs <c>HandleClient</c> on it. Bounded
/// concurrency: the OS only knows about <see cref="WorkerCount"/>
/// threads plus the accept thread. The queue absorbs bursts up to
/// <see cref="MaxQueueSize"/>; over-capacity accepted sockets are rejected
/// with <c>503 Service Unavailable</c> by the accept loop. Queue length,
/// capacity, and capacity-full state are exposed through <see cref="QueueLength"/>,
/// <see cref="Capacity"/>, and <see cref="IsAtCapacity"/> for the <c>/qstats</c>
/// route.
/// </summary>
public static class WorkerPool
{
    /// <summary>
    /// Maximum number of accepted sockets held in the queue at one time.
    /// Sized for learning, not for production. See M8 in
    /// <c>docs/learning/m8-bounded-queue/s1-bounded-queue.md</c>.
    /// </summary>
    public const int MaxQueueSize = 64;

    private static readonly object PoolLock = new();
    private static readonly Queue<Socket> Pending = new();
    private static Thread[]? Workers;
    private static string WebRoot = "";
    private static volatile bool Stopping;

    public static int WorkerCount => Workers?.Length ?? 0;

    public static int Capacity => MaxQueueSize;

    public static int QueueLength
    {
        get
        {
            lock (PoolLock)
            {
                return Pending.Count;
            }
        }
    }

    public static bool IsAtCapacity
    {
        get
        {
            lock (PoolLock)
            {
                return Pending.Count >= MaxQueueSize;
            }
        }
    }

    public static Action<Socket, string>? ClientHandler;

    public static void Start(int workerCount, string webRoot)
    {
        if (Workers is not null)
        {
            throw new InvalidOperationException("WorkerPool already started.");
        }
        WebRoot = webRoot;
        Workers = new Thread[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            int workerId = i;
            var t = new Thread(() => WorkerLoop(workerId))
            {
                IsBackground = true,
                Name = $"mws-worker-{workerId}",
            };
            Workers[i] = t;
            t.Start();
        }
    }

    /// <summary>
    /// Non-blocking enqueue. Returns <c>true</c> if the socket was added to
    /// the queue and a worker will eventually pick it up; returns
    /// <c>false</c> if the queue is at <see cref="MaxQueueSize"/> or the
    /// pool is shutting down. The caller is responsible for closing the
    /// socket on <c>false</c> (the accept loop replies with
    /// <c>503 Service Unavailable</c>).
    /// </summary>
    public static bool TryEnqueue(Socket clientSocket)
    {
        lock (PoolLock)
        {
            if (Stopping || Pending.Count >= MaxQueueSize)
            {
                return false;
            }
            Pending.Enqueue(clientSocket);
            Monitor.Pulse(PoolLock);
            return true;
        }
    }

    public static void Shutdown()
    {
        Stopping = true;
        lock (PoolLock)
        {
            Monitor.PulseAll(PoolLock);
        }
        Workers?.ToList().ForEach(t => t.Join(TimeSpan.FromSeconds(5)));
    }

    private static void WorkerLoop(int workerId)
    {
        while (!Stopping)
        {
            Socket? clientSocket;
            lock (PoolLock)
            {
                while (Pending.Count == 0 && !Stopping)
                {
                    Monitor.Wait(PoolLock);
                }
                if (Stopping && Pending.Count == 0)
                {
                    return;
                }
                clientSocket = Pending.Dequeue();
            }

            if (clientSocket is null)
            {
                continue;
            }

            try
            {
                HandleClientForPool(clientSocket, workerId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[worker {workerId}] error while handling client: {ex.Message}");
                try { clientSocket.Dispose(); } catch { }
            }
        }
    }

    private static void HandleClientForPool(Socket clientSocket, int workerId)
    {
        // Delegate to the top-level HandleClient registered by Program.cs.
        // We cannot call top-level methods directly from another type.
        ClientHandler?.Invoke(clientSocket, WebRoot);
    }
}