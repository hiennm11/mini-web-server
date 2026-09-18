# Milestone 10: Async-Mode ThreadPool Cap

## Question

What happens to the async server when the `ThreadPool`'s worker count is capped below what the work demands?

## OSTEP Context

- Chapter(s): 33 (event-based servers), specifically §33.5 ("no blocking calls") and §33.6 (asynchronous I/O).
- Concept: the event-based server is single-threaded at the application layer, but the runtime still needs *some* threads to service CPU work and continuations. OSEP §33.5 explicitly warns that a blocking call stalls the whole event loop; in .NET, a blocking call that needs a worker thread will be scheduled on the `ThreadPool`, and the runtime may grow the pool up to its max.
- Key point: under sustained load the runtime grows the pool, then queues. The size of the pool is a knob: smaller pool = less memory, but more queuing. We want to observe this trade-off.

## C#/.NET Mechanism

- `ThreadPool.SetMaxThreads(int workerThreads, int ioCompletionThreads)` caps the worker pool. The `ioCompletionThreads` second arg is Windows-specific (Linux uses a different I/O completion model) and we leave it at default.
- `ThreadPool.GetMinThreads(out int worker, out int io)` and `GetMaxThreads(...)` expose current limits. `GetAvailableThreads(out int worker, out int io)` shows how many worker slots are free right now.
- The .NET `ThreadPool` is a "hill-climbing" pool: it grows one thread at a time when it sees starvation (continuations queueing), shrinks after idle time. A hard cap via `SetMaxThreads` is a deliberate override.
- Capping `ThreadPool` matters in **async mode** because that's where `Task` continuations run on the pool. In default mode, the WorkerPool uses its own dedicated threads (we spawn them ourselves), so the `ThreadPool` cap has no observable effect there.

## Build

Add a CLI flag `--max-threads N` (and `--min-threads N` for symmetry) that bounds the `ThreadPool` worker count at startup. Apply the cap *before* the accept loop starts so all continuations see the new limit.

```csharp
int? maxThreads = null;
int? minThreads = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--max-threads" && i + 1 < args.Length && int.TryParse(args[++i], out var mx)) maxThreads = mx;
    else if (args[i] == "--min-threads" && i + 1 < args.Length && int.TryParse(args[++i], out var mn)) minThreads = mn;
}
if (maxThreads.HasValue || minThreads.HasValue)
{
    ThreadPool.GetMinThreads(out var curMin, out var curIo);
    ThreadPool.GetMaxThreads(out var curMax, out var curIoMax);
    int newMin = minThreads ?? curMin;
    int newMax = maxThreads ?? curMax;
    if (!ThreadPool.SetMinThreads(newMin, curIo) || !ThreadPool.SetMaxThreads(newMax, curIoMax))
    {
        Console.Error.WriteLine($"[threadpool] failed to set min={newMin} max={newMax}");
    }
    ThreadPool.GetMinThreads(out var appliedMin, out _);
    ThreadPool.GetMaxThreads(out var appliedMax, out _);
    Console.WriteLine($"[threadpool] cap applied: min={appliedMin} max={appliedMax} worker threads (was min={curMin} max={curMax})");
}
```

Extend `/stats` to include the pool state:

```csharp
ThreadPool.GetMinThreads(out var tMin, out _);
ThreadPool.GetMaxThreads(out var tMax, out _);
ThreadPool.GetAvailableThreads(out var tAvail, out _);
int tActive = tMax - tAvail; // approx; GetAvailableThreads counts idle workers only
string body += $"threadpool_min = {tMin}\nthreadpool_max = {tMax}\nthreadpool_active = {tActive}\n";
```

Files affected:

- `src/MiniWebServer.Host/Program.cs` — parse flags, apply cap, log, extend `/stats`. The cap must be applied *before* `AsyncServer.RunAsync` or the WorkerPool `Start`, depending on the mode.

Keep the slice small:

- No new types — the cap is configured inline at startup.
- The `/stats` route gains three lines, no new route.
- Cap is worker-only; `ioCompletionThreads` left at runtime default (Windows-specific).

## Experiment

Terminal 1 — start the server in async mode with a tight cap:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj -- --async --max-threads 4 --min-threads 4
```

Terminal 2 — fire 150 parked slow clients and observe:

```powershell
Add-Type @'
using System;
using System.Net.Sockets;
using System.Threading;
using System.Text;

public class Cap
{
    public static void Park(int id, int seconds)
    {
        var t = new Thread(() =>
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    s.Connect("127.0.0.1", 8080);
                    s.Send(Encoding.ASCII.GetBytes("GET /slow?seconds=" + seconds + " HTTP/1.1\r\nHost: localhost\r\n\r\n"));
                    s.ReceiveTimeout = (seconds + 5) * 1000;
                    var buf = new byte[2048];
                    int n = s.Receive(buf);
                    Console.WriteLine("[park " + id + "] done, " + n + " bytes");
                }
            }
            catch (Exception ex) { Console.WriteLine("[park " + id + "] " + ex.Message); }
        });
        t.IsBackground = true;
        t.Start();
    }
}
'@

[Cap]::Park(1, 30)
[Cap]::Park(2, 30)
# ... or fire 150 of them
1..150 | %{ [Cap]::Park($_, 30) }

# While they're parked, sample /stats to see thread count
Start-Sleep -Seconds 2
$stats = (New-Object System.Net.Sockets.TcpClient).Connect("127.0.0.1", 8080)
# (simpler: a new HttpClient)
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(2)
$client.GetStringAsync("http://127.0.0.1:8080/stats").Result
```

Expected with cap=4:

- 150 parked slow clients queue continuations on the ThreadPool.
- `/stats` reports `threadpool_max=4` and `threadpool_active <= 4` even with 150 Tasks in flight (some are parked on the timer queue, not consuming a worker slot; only the actively-running handlers hold a worker slot).
- Total process threads stays bounded; the M7 observation of "20 threads for 150 connections" shrinks further to "≤ 4 worker threads for 150 connections."

Run a second time without the cap (drop the flag) and confirm `/stats` shows the runtime default (`threadpool_max = 32767` on .NET 10 by default).

A third run with `--max-threads 32` shows the intermediate case: thread count grows up to 32, then queueing begins.

## Observation

Answer after running the experiment:

What changes when the ThreadPool is capped?

Expected answer:

The async server still handles all 150 connections (the Tasks are scheduled regardless of worker availability), but the worker count is now hard-bounded. Continuations that need a worker slot queue when the cap is reached; only N (the cap) handlers can be actively running at any time. Memory and total OS thread count go down compared to no cap. Latency for "instant" requests (e.g., `/stats`) stays low as long as at least one worker slot is free.

More precise answer:

The .NET ThreadPool is a hill-climbing pool that grows one thread at a time under starvation. Capping it with `SetMaxThreads(N, _)` turns the growth strategy into a hard wall. When the cap is hit, the runtime stops growing; new work items queue until a slot frees. In the smoke, this means at most N handlers can be executing their `Task.Delay`-resumed continuations simultaneously. The remaining N-K handlers are queued waiting for a slot. Connection parking (the `Task.Delay(30000)` in `/slow`) does **not** hold a worker slot — the Task is on the timer queue, not consuming a worker. So `threadpool_active` will be far less than 150 even with the cap at 4, but it can never exceed 4.

## Three-Question Test

1. What is the OS doing?
   - The OS schedules the N worker threads (and the accept loop thread, and the GC threads, etc.). When the cap is hit and Tasks queue, the runtime's work-stealing deque grows but no new OS threads are created. From the OS perspective, the process has fewer threads than the uncapped case, so context-switch overhead is lower.
2. Which .NET API exposes it?
   - `System.Threading.ThreadPool.SetMaxThreads(int, int)` and `SetMinThreads(int, int)`. The runtime exposes `GetAvailableThreads(out int, out int)` for sampling.
3. Where does it break at scale?
   - With a cap too low, work backs up: a `/stats` request that arrives during a slow burst may wait behind 1000s of queued continuations. Production code uses `SemaphoreSlim` or per-route quotas to bound work at the application layer, not at the pool layer.

## Learning Note

### What changed

- `src/MiniWebServer.Host/Program.cs` — added `--max-threads N` and `--min-threads N` CLI flag parsing and `ThreadPool.SetMaxThreads` / `SetMinThreads` application at startup, applied to **both** server modes. The cap is logged at startup with the old/new min/max. `/stats` route gains three lines: `threadpool_min`, `threadpool_max`, `threadpool_active`.
- `src/MiniWebServer.Host/AsyncServer.cs` — `/stats` route in async mode also gains the three `threadpool_*` lines so the smoke is comparable across modes.

Files affected:

- `src/MiniWebServer.Host/Program.cs` (flag parsing + cap apply + stats extension)
- `src/MiniWebServer.Host/AsyncServer.cs` (stats extension only)

### What I observed

Two smoke runs against the async server with 150 parked `/slow` connections (each `Task.Delay(30000)`):

#### Run 1: with `--max-threads 4 --min-threads 4`

| Sample | threads | threadpool_min | threadpool_max | threadpool_active | total_requests |
|---|---|---|---|---|---|
| baseline | 15 | 4 | 4 | 1 | 151 |
| +3s stress | 16 | 4 | 4 | 1 | 302 |
| +8s stress | 15 | 4 | 4 | 1 | 303 |
| +18s stress | 15 | 4 | 4 | 1 | 304 |

#### Run 2: no cap (default)

| Sample | threads | threadpool_min | threadpool_max | threadpool_active | total_requests |
|---|---|---|---|---|---|
| baseline | 11 | 8 | 32767 | 1 | 1 |
| +3s stress | 15 | 8 | 32767 | 1 | 152 |
| +8s stress | 14 | 8 | 32767 | 1 | 153 |
| +18s stress | 14 | 8 | 32767 | 1 | 154 |

#### Reading the table

The cap is binding: `threadpool_max = 4` is held across all four samples in Run 1. In Run 2 the runtime default `threadpool_max = 32767` is in force and the pool is free to grow.

OS thread count (`threads`) stays in the 11-16 range in **both** runs. The cap does not visibly reduce the OS thread count here because the parked `/slow` Tasks are all on the timer queue — they do not consume a worker slot. Only the `await clientSocket.SendAsync(...)` and the `/stats` query's parsing path hold a worker. With 150 Tasks parked on `Task.Delay`, `threadpool_active` stays at 1 (the in-flight `/stats` query).

This is the key insight the smoke surfaces: **a ThreadPool cap is most visible when CPU-bound work piles up against a worker count, not when Tasks are all parked on async I/O**. The cap is a hard wall on the hill-climbing growth strategy. If we had 150 concurrent handlers all doing CPU work (e.g., a `/race` route), the cap would force at most 4 of them to run in parallel; the remaining 146 would queue for a worker slot, and `/stats` would observe the queue delay.

#### Why the thread count is the same

In Run 1, the runtime cannot grow the ThreadPool beyond 4, but the rest of the process still has GC threads, finalizer threads, the async accept-loop I/O completion threads, and the dedicated thread for the `Server socket listening` print. The ~15 baseline threads come from .NET internals, not from the user code. Capping the user-visible worker pool to 4 does not affect the internals; it only caps how many `Task` continuations can run concurrently.

In Run 2, the ThreadPool is free to grow, but the parked Tasks don't request workers, so it doesn't grow either. The two runs converge on the same OS-thread count.

The cap matters most when:

1. **CPU-bound work piles up**: 1000 concurrent `/race` requests would need 1000 worker slots without a cap; with cap=4, only 4 run in parallel and 996 queue. This is the OSEP §33.5 "no blocking calls" lesson in disguise: CPU-bound work in an event-based server must be brief, or the application must bound it.
2. **Heterogeneous loads**: a few slow handlers can starve many fast ones. The cap bounds total CPU parallelism, so fast handlers get a fair share.

### OSEP concept

OSEP §33.5 ("no blocking calls") is the rule that makes the event-based model work. The runtime equivalent is "no long-running work without yielding to the scheduler". Capping the ThreadPool with `SetMaxThreads` is a runtime-level enforcement of the same idea: even if the application code accidentally does long-running work, the pool will not grow unboundedly.

In OSEP §33.6 the chapter discusses asynchronous I/O at the OS level. The .NET equivalent is `IOCP` on Windows / `epoll` on Linux. The cap does not affect I/O completion thread count (which is the second arg of `SetMaxThreads`); it only caps CPU worker threads.

The lesson also connects to OSEP §26 ("concurrency and threads"): the OS schedules threads; the runtime schedules Tasks onto those threads. The cap is the boundary between the two schedulers — it tells the runtime scheduler "do not ask the OS for more than N threads, no matter how much work queues."

### .NET mechanism

- `System.Threading.ThreadPool.SetMaxThreads(int worker, int io)` sets the upper bound on worker threads. The second arg is Windows-specific (`ioCompletionThreads`); on Linux the I/O completion thread count is managed by the IOCP-equivalent layer separately.
- `SetMinThreads(int worker, int io)` sets the lower bound the runtime will keep around idle. The default is `processor count`; setting it lower makes the pool shrink faster when idle.
- `GetAvailableThreads(out int worker, out int io)` returns the count of worker slots that are free right now. `max - available` is the active worker count.
- The .NET ThreadPool is a "hill-climbing" pool: it grows one thread at a time under starvation (Hungarian algorithm) and shrinks after idle time. The cap overrides the upper bound of this growth.
- The cap is per-process, not per-appdomain. `SetMaxThreads` in a test runner affects the test process's pool.

### Next question

The cap bounds CPU parallelism. What about **memory**? `ArrayPool<byte>` would lower the per-connection buffer cost in async mode, taking the M7 numbers from 172 MB toward M6's 21 MB. That's a separate slice in the `Useful directions` list.

Or, persistence: replace `File.ReadAllBytes` (used in `StaticFileResponder`) with raw `FileStream.Read` calls. The current code wraps the file in a single read; a streaming approach uses many small reads, which exposes the OSEP Ch. 36-38 I/O device narrative (interrupts, DMA, sector reads). That's M11.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted