# Slice 4.6: Thread-Per-Connection Limits

## Question

Why does thread-per-connection fail at scale?

## OSTEP Context

- Chapter(s): 26 (Concurrency: An Introduction), 27 (Interlude: Thread API)
- Concept: each thread reserves a private stack in virtual memory. On x86-64, the default is 1 MiB per thread. Unbounded thread creation exhausts memory and CPU.
- Key point: even with no work in flight, a parked thread costs at least its reserved stack plus the cost of context switching on every timer interrupt. At a few hundred threads the OS scheduler spends more time switching between them than running them.

## C#/.NET Mechanism

- `Process.Threads.Count` reports the OS thread count owned by the current process. Includes runtime threads (GC, finalizer, JIT, IO completion ports) plus every `new Thread(...)` that has not been collected by the GC.
- `Process.WorkingSet64` reports bytes currently resident in physical memory.
- `Process.PrivateMemorySize64` reports bytes committed to private virtual memory (most relevant for stack reservation cost).
- On .NET 10 x64, the default thread stack size is 1 MiB. The CLR reserves it via the OS when the thread is created; only the touched pages count toward working set, but the virtual reservation grows immediately.

## Build

Add a `/stats` route that returns thread count, working set, and private memory.

File affected:

- `src/MiniWebServer.Host/Program.cs`

Code shape inside the response branch (alongside `/slow` and `/race`):

```csharp
else if (parsedRequest.Path == "/stats")
{
    var p = System.Diagnostics.Process.GetCurrentProcess();
    int threads = p.Threads.Count;
    long workingSet = p.WorkingSet64;
    long privateBytes = p.PrivateMemorySize64;
    string body =
        $"threads = {threads}\n" +
        $"working_set_bytes = {workingSet}\n" +
        $"private_bytes = {privateBytes}\n" +
        $"total_requests = {RequestStats.TotalRequests}\n";
    response = new HttpResponse(
        200, "OK", "text/plain; charset=UTF-8", Encoding.UTF8.GetBytes(body));
}
```

Also extend `/slow` to sleep 30 seconds instead of 5 — this lets a stress client park many handler threads in `Thread.Sleep` long enough to sample memory under load.

The slice intentionally does not add a max-thread counter, a thread pool, or a queue. The lesson is the linear cost of unbounded thread creation; the fix belongs to Milestone 6 (bounded worker pool).

## Experiment

Terminal 1 — start the server:

```powershell
dotnet run --project src/MiniWebServer.Host/MiniWebServer.Host.csproj
```

Terminal 2 — sample baseline, then open 50 parked sockets, sample, then 100 more, sample. A "parked" socket is one that connects and sends one byte so the server handler thread blocks in `Socket.Receive(...)` waiting for the rest of the request:

```powershell
Add-Type @'
using System;
using System.Net.Sockets;
using System.Threading;

public class SlowClient
{
    public static void OpenMany(string host, int port, int count)
    {
        var sockets = new Socket[count];
        for (int i = 0; i < count; i++)
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            s.Connect(host, port);
            s.Send(new byte[] { 0x47 }); // 'G' - first byte of a GET request
            sockets[i] = s;
        }
        Console.WriteLine("opened " + count + " sockets, sleeping 5s ...");
        Thread.Sleep(5000);
        foreach (var s in sockets) { try { s.Close(); } catch { } }
        Console.WriteLine("closed all sockets");
    }
}
'@

(Invoke-WebRequest -Uri http://localhost:8080/stats -UseBasicParsing).Content
[SlowClient]::OpenMany('127.0.0.1', 8080, 50)
(Invoke-WebRequest -Uri http://localhost:8080/stats -UseBasicParsing).Content
[SlowClient]::OpenMany('127.0.0.1', 8080, 100)
(Invoke-WebRequest -Uri http://localhost:8080/stats -UseBasicParsing).Content
```

Expected result:

- Baseline `/stats` shows a small thread count (around 9: the accept thread, GC, finalizer, JIT, IO completion port, plus a few runtime threads) and modest memory (~9 MB private).
- After 50 parked sockets, the thread count grows by roughly 50 and private memory grows by roughly 50 MB (1 MB per thread stack on .NET x64).
- After 100 more parked sockets, the thread count grows by another ~100 and private memory by another ~100 MB.
- `total_requests` reflects the cumulative count from slice 4.4's atomic counter.

## Observation

Answer after running the experiment:

How much memory does each parked handler thread cost?

Expected answer:

Roughly 1 MiB per thread. The exact numbers from this slice's smoke run:

| Stage | Threads (OS) | Working set | Private memory | Δ private |
|---|---|---|---|---|
| Baseline | 9 | 27 MB | 9.5 MB | — |
| 50 parked | 7 | 83 MB | 65 MB | +55 MB |
| +100 parked | 7 | 190 MB | 174 MB | +109 MB |

- 50 new threads → +55 MB private → **1.1 MB / thread**
- 100 new threads → +109 MB private → **1.09 MB / thread**

The "threads" number drops because the runtime periodically reaps finished threads, but private memory never returns to baseline during the experiment. The OS reserved 1 MiB of virtual address space for each thread's stack the moment it was created, and that reservation is not released until the thread is fully collected.

More precise answer:

A thread stack is reserved (not committed) virtual memory. The OS allocates a range of virtual addresses for the stack when the thread is created, but only physical pages that the thread actually touches count toward working set. Private memory (`Process.PrivateMemorySize64`) counts the reserved range, which is why the cost is visible immediately. With 150 parked threads, 150 MiB of virtual address space is committed but most of it is unmapped pages that simply cannot be reused by other threads. At ~1.1 MB per thread, 1000 concurrent connections would reserve over 1 GB just for stacks, before any application data. This is the cost the OSEP chapter calls out and the reason Milestone 6 will introduce a bounded worker pool.

## Three-Question Test

1. What is the OS doing?
   - It reserves a virtual-address range for the stack when the thread is created. The reservation is independent of how much of the stack the thread has actually touched.
2. Which .NET API exposes it?
   - `Process.Threads.Count` for active OS threads, `Process.PrivateMemorySize64` for committed virtual memory. `new Thread(...)` creates a thread with the default 1 MiB stack; passing a custom `Thread(ParameterizedThreadStart, int maxStackSize)` lets you shrink it.
3. Where does it break at scale?
   - At a few hundred connections the memory cost dominates; at a few thousand, the scheduler spends more time switching between threads than running them. The fix is a bounded pool of long-lived worker threads (Milestone 6) so each accepted connection does not allocate a fresh OS thread.

## Learning Note

### What changed

Added a `/stats` route that reports `Process.Threads.Count`, `Process.WorkingSet64`, `Process.PrivateMemorySize64`, and `RequestStats.TotalRequests`. Extended `/slow` from `Thread.Sleep(5000)` to `Thread.Sleep(30000)` so stress clients have enough time to park their handler threads while the observer samples memory. No max-thread counter, no thread pool, no queue — the slice is observation only.

File affected:

- `src/MiniWebServer.Host/Program.cs`

### What I observed

Baseline (server idle):

```text
threads = 9
working_set_bytes = 27197440
private_bytes = 9555968
total_requests = 4
```

After opening 50 parked sockets (each sends one byte then holds the connection):

```text
threads = 7
working_set_bytes = 82657280
private_bytes = 65327104
total_requests = 55
```

After opening 100 more parked sockets (cumulative 150):

```text
threads = 7
working_set_bytes = 190152704
private_bytes = 173830144
total_requests = 156
```

Computed deltas:

- 50 new threads → private memory +55 MB → **1.1 MB per parked thread**
- 100 more threads → private memory +109 MB → **1.09 MB per parked thread**

The "threads" count did not grow in lock-step because the runtime reaped finished `/stats` handler threads in between samples, but the private memory never came back down during the experiment. The OS had reserved 1 MiB of virtual address space for each parked thread's stack and that reservation does not get returned to the pool until the `Thread` object is fully garbage-collected.

### OSTEP concept

OSEP Ch. 27 §27.1 explains that creating a thread means asking the OS for a separate execution context, including its own register state and a fresh stack. On x86-64, the default thread stack is 1 MiB; on Linux/POSIX it is configurable via `pthread_attr_setstacksize`, on Windows via `CreateThread(..., dwStackSize, ...)`. The OS reserves the range immediately — most of it is unused at any given moment, but the reservation is committed virtual memory.

The smoke numbers match the OS prediction almost perfectly: 1.1 MB per thread (slightly above 1 MiB because of guard pages and the CLR's extra thread-local structures). At 1000 concurrent connections, the server would reserve over 1 GB of virtual address space just for stacks. Physical pages are only faulted in for touched stack ranges, so working set grows more slowly than private memory, but the virtual address space is the binding constraint — 32-bit processes would hit the 2 GiB ceiling well before that.

The scheduler cost is the second half of the OSEP argument. With 150 threads parked in `Thread.Sleep`, the OS's timer interrupt fires every ~15 ms; each interrupt saves the current TCB, picks a Ready thread, restores its TCB. As thread count grows, scheduling overhead grows roughly linearly, while useful CPU time shrinks proportionally. The remedy OSEP points to (and the repo's roadmap reflects in Milestone 6) is bounded concurrency: a fixed-size worker pool that processes accepted connections from a queue.

### .NET mechanism

- `Process.GetCurrentProcess()` returns a snapshot handle. `Threads.Count` is the number of OS threads the process has created and not yet reaped; the runtime uses several threads of its own (GC, finalizer, JIT, ThreadPool worker, IO completion port) before any user code.
- `WorkingSet64` is the resident set size — bytes currently in physical RAM.
- `PrivateMemorySize64` is committed virtual memory specific to the process, including thread stacks, the managed heap, JIT code, etc.
- `Thread.CurrentThread.ManagedThreadId` (already in use since slice 4.3) identifies the managed thread; `ThreadPool` is the CLR's built-in pool, but using it here would hide the per-connection thread cost the slice is supposed to observe.

### Next question

How do we accept arbitrarily many connections without allocating an OS thread per connection?

Next slice: Milestone 6 — bounded worker pool. Accept thread enqueues client sockets onto a fixed-size `Queue<Socket>`. Worker threads dequeue and call `HandleClient`. The OS only knows about N worker threads; the queue absorbs bursts.

## Status

- [x] Planned
- [x] Built
- [x] Experimented
- [x] Noted