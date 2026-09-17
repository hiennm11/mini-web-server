# Milestone 6.3 (or M8): Bounded Queue + Backpressure

## Question

What does the server do when more requests arrive than workers can process?

## OSTEP Context

- Chapter(s): 30 (Condition Variables), 31 (Semaphores — bounded buffer)
- Concept: a producer/consumer queue with finite capacity requires backpressure. Producers must either block (wait for room) or fail fast (return an error). OSEP §30.4 covers the blocking form (`while (count == MAX) wait`); OSEP §31.4 covers the non-blocking form (`sem_wait`/`sem_trywait` and the bounded-buffer variant).
- Key point from OSEP: the current `WorkerPool` accepts bursts of any size — `Queue<Socket>` is unbounded. Under sustained overload, memory grows without limit (each `Socket` + its kernel buffer is a few KB; multiplied by millions of unprocessed connections, the process runs out of address space before workers ever wake up). Bounded queue + backpressure prevents that and gives the application a real choice about what "too many" means.

## C#/.NET Mechanism

- `Monitor.Wait(obj)` is the .NET equivalent of `pthread_cond_wait`. Like OSEP, the pattern is `while (queue.Count >= MaxQueueSize) Monitor.Wait(PoolLock)`. The wait atomically releases the lock and parks the calling thread on the monitor's wait queue.
- The current `Enqueue` is non-blocking: `lock { Enqueue; Pulse }`. With bounded queue + blocking producer, it becomes:
  ```csharp
  lock (PoolLock)
  {
      while (Pending.Count >= MaxQueueSize && !Stopping)
          Monitor.Wait(PoolLock);
      if (Stopping) { clientSocket.Dispose(); return; }
      Pending.Enqueue(s);
      Monitor.Pulse(PoolLock);
  }
  ```
- For the **non-blocking** variant, the producer can use a `try { ... } finally` pattern and dispose the socket if the queue is full, then return `503 Service Unavailable` to the client. Requires `Peek` or a count check before enqueueing, which is racy unless we lock (which we do anyway).
- The handle of the rejected socket is the tricky bit: the **accept loop owns** it. The server constructs an HTTP `503 Service Unavailable` response, sends it, and disposes the socket — all without going through `WorkerPool`.

## Build

Add a bounded capacity to `WorkerPool`, a max-queue setting, and a 503 response path for the accept loop when the queue is full.

Files affected:

- `src/MiniWebServer.Host/WorkerPool.cs` — add `MaxQueueSize` constant (default e.g. 64), make `Enqueue` blocking with `while (Count >= Max) Monitor.Wait`, expose `TryEnqueue` for the non-blocking 503 path. Optional: expose `IsAtCapacity` snapshot for `/qstats`.
- `src/MiniWebServer.Host/Program.cs` — accept loop branches: try `Enqueue`; if `TryEnqueue` returns false, write a 503 response on the client socket directly and close it. Also add a `/qstats` line for `capacity` and `at_capacity`.
- `docs/learning/milestone-6-bounded-worker-pool.md` — append a "6.3 bounded queue" subsection so the M6 lesson note reflects the change.

Code shape inside the accept loop:

```csharp
Socket clientSocket = serverSocket.Accept();
if (!WorkerPool.TryEnqueue(clientSocket))
{
    // Queue is full; reject the client with a 503 and close.
    HttpResponse busy = new HttpResponse(
        503, "Service Unavailable",
        "text/plain; charset=UTF-8",
        Encoding.UTF8.GetBytes("Server is at capacity; retry later.\n"));
    try
    {
        SendAll(clientSocket, busy.ToBytes());
    }
    catch { /* best effort */ }
    clientSocket.Dispose();
}
```

`TryEnqueue` shape:

```csharp
public static bool TryEnqueue(Socket s)
{
    lock (PoolLock)
    {
        if (Pending.Count >= MaxQueueSize || Stopping)
        {
            return false;
        }
        Pending.Enqueue(s);
        Monitor.Pulse(PoolLock);
        return true;
    }
}
```

`Enqueue` (blocking variant) becomes the production path if we want to use `Enqueue` and have callers wait. For now keep both; the accept loop uses `TryEnqueue`.

Keep the slice small:

- `MaxQueueSize = 64` is a learning constant, not tuned.
- The 503 response uses the existing `HttpResponse` type — no new response shape.
- No retry-after header, no metrics beyond what `/qstats` already exposes.
- No eviction policy (LRU etc.); out of scope.
- The `--async` mode is unaffected: `AsyncServer` doesn't use `WorkerPool` and has no queue to bound.

## Experiment

Terminal 1 — start the server (default mode = bounded worker pool):

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Terminal 2 — fill the queue, then a 65th request should get 503. The 64 parked slow clients stay in `/slow` for 30 seconds; one more during that window should observe the cap:

```powershell
# Fill the pool: park 64 /slow clients (8 workers x 8 = 64 = queue full)
1..64 | ForEach-Object {
    Start-Job -ScriptBlock {
        try { (Invoke-WebRequest -Uri http://localhost:8080/slow -UseBasicParsing -TimeoutSec 60).StatusCode }
        catch { 'TIMEOUT' }
    }
} | Out-Null

# wait for them all to be accepted
Start-Sleep -Seconds 2

# Sample qstats — expect queue_length close to 64-8=56 (after 8 workers picked up 8)
(Invoke-WebRequest -Uri http://localhost:8080/qstats -UseBasicParsing).Content

# 65th client: should get 503 because queue is at capacity
(Invoke-WebRequest -Uri http://localhost:8080/slow -UseBasicParsing -TimeoutSec 5).StatusCode
```

Expected:

- `/qstats` shows `queue_length = 56` (64 in flight, 8 picked up by workers, 56 still queued), `at_capacity = true` once 64 are queued.
- The 65th `curl` returns `503 Service Unavailable` immediately (no in-flight connection, no waiting).
- `worker_count` stays at 8.
- `Accept loop` log line shows "Rejecting connection: queue full" or similar.

## Observation

Answer after running the experiment:

What happens to a client when the queue is full?

Expected answer:

The client receives a `503 Service Unavailable` HTTP response immediately and the connection is closed. The server keeps its 8 workers busy and does not accept more work than it can process. The OS-level process memory stays bounded because the queue cannot grow past `MaxQueueSize`.

More precise answer:

`WorkerPool.TryEnqueue` returns `false` when `Pending.Count >= MaxQueueSize`. The accept loop catches this, constructs a `503` response, sends it, and disposes the socket. The server is now **explicit about overload** instead of silently accumulating work until it dies. This is the OSEP §31.4 bounded-buffer pattern with the producer dropping (returning 503) instead of blocking. The choice between blocking-producer and reject-producer is a policy decision; this slice picks reject for visibility and because the alternative (letting the accept thread park) defeats the point of having a bounded pool.

## Three-Question Test

1. What is the OS doing?
   - It still runs the accept thread (which now sees fewer incoming sockets because the kernel's listen backlog is also a queue, with its own `ListenBacklog = 10` cap) plus the 8 workers. The bounded `Queue<Socket>` is purely a user-space data structure.
2. Which .NET API exposes it?
   - `Monitor.Wait(obj)` for blocking producers (unused in this slice — we chose reject). The `HttpResponse` type for the 503 body. `Socket.Dispose` to release kernel resources for rejected connections.
3. Where does it break at scale?
   - At sustained overload above `MaxQueueSize`, every new request gets a 503. That's the correct behavior, but the application has no way to tell clients when to retry without adding a `Retry-After` header (future slice). Also: the kernel's listen backlog (`ListenBacklog = 10`) is much smaller than `MaxQueueSize = 64`; in practice the accept thread keeps the kernel queue drained, but if the accept thread itself blocks, the kernel can refuse connections with `ECONNREFUSED` at the OS layer.

## Learning Note

### What changed

- `WorkerPool.MaxQueueSize = 64` constant.
- `WorkerPool.TryEnqueue(Socket) : bool` — non-blocking enqueue that returns false when full or shutting down.
- `WorkerPool.IsAtCapacity` — exposes the boolean for `/qstats`.
- Accept loop in `Program.cs`: calls `TryEnqueue`; on false, writes `503 Service Unavailable`, sends, disposes.
- `/qstats` route extended with `capacity` and `at_capacity` lines.

Files affected:

- `src/MiniWebServer.Host/WorkerPool.cs`
- `src/MiniWebServer.Host/Program.cs`
- `docs/learning/milestone-6-bounded-worker-pool.md` — append "6.3 bounded queue" subsection.

### What I observed

TBD after implementation and experiment.

### OSTEP concept

OSEP §30.4 introduced the bounded-buffer producer/consumer pattern with two condition variables (`empty` and `full`). The producer waits when the buffer is full; the consumer waits when it is empty. This slice chooses a different policy: instead of blocking the producer (which would freeze the accept loop), it rejects the client with `503`. That maps to a real-world choice in production servers (e.g., HAProxy returns 503 when its connection queue is full).

The reject policy is consistent with OSEP §33.4's "no blocking calls in event-based servers" lesson — if the accept thread were to block on a full queue, the server would lose the ability to keep draining the kernel's listen backlog, and clients would see connection-refused instead of a clean 503. Rejecting at the application layer keeps the accept thread responsive and gives the client a clear retry signal (the 503 status code itself).

### .NET mechanism

`Monitor.Wait(PoolLock)` is the .NET analogue of `pthread_cond_wait(PoolLock, mutex)`. Both atomically release the lock and park the calling thread; the runtime's `ThreadPool` is not involved — the parked thread sits on the monitor's wait queue and the OS schedules it when another thread calls `Monitor.Pulse(PoolLock)` or `Monitor.PulseAll(PoolLock)`. This slice does not use `Wait` (the producer never blocks) but the existing `WorkerLoop` already does, so the helper is available if a future slice wants blocking-producer behavior.

`Socket.Dispose` releases the underlying file descriptor and any buffer pool associated with it. Without it, the OS would not know the connection is closed until the GC runs the finalizer.

### Next question

What about the async mode? `AsyncServer` has no queue — each `Task` is parked directly. How does the async server behave under sustained overload?

Next slice: bound the async server too. `ThreadPool.SetMaxThreads` caps the pool, and exceeding it queues the continuation onto the calling thread. We can measure the cutoff point with a similar stress test.

## Status

- [x] Planned
- [ ] Built
- [ ] Experimented
- [ ] Noted