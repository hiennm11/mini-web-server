# Milestone 4: Thread-Per-Connection — Lesson Slice Plan

> Status: planned — not yet implemented. Each slice must be implemented individually, observed, and noted before moving to the next.

Milestone 4 introduces concurrency into the server. The goal is not to build a production-grade threaded server. The goal is to make OS concepts concrete: what happens when a server thread blocks, how threads change that, and what new problems appear.

## Slice Roadmap

| Slice | Name | Concept | Duration |
|-------|------|---------|----------|
| 4.1 | Prove single-thread blocking | Process states | 30 min |
| 4.2 | Spawn one thread per client | Threads, multiple PCs | 45 min |
| 4.3 | Observe scheduling non-determinism | Scheduler, context switch | 30 min |
| 4.4 | Shared address space | Shared heap vs stacks | 45 min |
| 4.5 | Prepare race condition lab | Race condition, critical section | 45 min |
| 4.6 | Thread-per-connection limits | Stack overhead | 30 min |

Dependencies: slices are sequential. 4.2 depends on 4.1. 4.5 depends on 4.4.

---

## Slice 4.1: Prove Single-Thread Blocking

### OSTEP Context

- Chapter(s): 4 (Process), 4.4 (Process States)
- Concept: a process in a `Blocked` state cannot accept new connections.

### C#/.NET Mechanism

- `Thread.Sleep(int milliseconds)` blocks the current managed thread.
- Single `while(true)` accept loop — only one `HandleClient` at a time.

### Build

Add a deliberate slow path. When the request path is `/slow`, sleep 5 seconds before responding. All other paths respond immediately.

### Experiment

1. Start server.
2. Terminal A: `curl http://localhost:8080/slow` (blocks 5s).
3. Terminal B (during those 5s): `curl http://localhost:8080/` — hangs.
4. Wait. Both eventually respond, but B waited for A.

### Observation

Why did the second client wait even though it requested a different path?

The server loop: `Accept() → HandleClient(blocking) → loop back to Accept()`. While `HandleClient` is blocked in `Thread.Sleep`, the loop has not returned to `Accept()`. No new client can be accepted.

### Learning Note

Write in a new file `docs/learning/slice-4.1-single-thread-blocking.md`.

---

## Slice 4.2: Spawn One Thread Per Client

### OSTEP Context

- Chapter(s): 26 (Concurrency: An Introduction), 27 (Thread API)
- Concept: a thread is a separate point of execution. Multiple threads can run while others block.

### C#/.NET Mechanism

- `new Thread(() => HandleClient(clientSocket)).Start()`
- `IsBackground = true` so threads don't keep the process alive.

### Build

Modify the accept loop: after `Accept()`, start a new thread to handle the client. The accept thread returns to `Accept()` immediately.

### Experiment

1. Start server.
2. Terminal A: `curl http://localhost:8080/slow` (5s).
3. Terminal B (immediately): `curl http://localhost:8080/` — responds instantly.
4. Observe server logs: two thread IDs, interleaved output.

### Observation

How can the server accept B while A is still sleeping?

A's thread is blocked. The accept thread returned to `Accept()` immediately after spawning A's handler thread. When B connects, `Accept()` has already looped around and is waiting for the next connection.

### Learning Note

Write in a new file `docs/learning/slice-4.2-spawn-thread-per-client.md`.

---

## Slice 4.3: Observe Scheduling Non-Determinism

### OSTEP Context

- Chapter(s): 26, 4.4
- Concept: the OS scheduler decides which thread runs next. The order is not predictable.

### C#/.NET Mechanism

- `Thread.CurrentThread.ManagedThreadId` identifies each managed thread.
- Console interleaving shows non-deterministic execution order.

### Build

Log `Thread.CurrentThread.ManagedThreadId` at the start and end of each `HandleClient`. Add a small variable-duration sleep so different threads finish at different times.

### Experiment

1. Start server.
2. Send 5 rapid `curl` requests in quick succession (script or multiple terminals).
3. Observe the console log: start and end messages interleave unpredictably.
4. Repeat. The interleaving pattern changes.

### Observation

Do requests finish in the order they arrived?

No. The scheduler chooses which thread runs. A slow request might log its end after a faster request that arrived later.

### Learning Note

Write in a new file `docs/learning/slice-4.3-scheduling-non-determinism.md`.

---

## Slice 4.4: Shared Address Space Appears

### OSTEP Context

- Chapter(s): 13 (Address Spaces), 26
- Concept: threads share heap and static data. Each thread has its own stack (local variables).

### C#/.NET Mechanism

- `static int` lives in shared memory.
- Local variables and method parameters are per-thread stack data.
- Log both to show the difference.

### Build

Add two counters:

```csharp
static int TotalRequests = 0;

static void HandleClient(Socket clientSocket)
{
    int localId = Interlocked.Increment(ref TotalRequests); // safe atomic for this slice
    // ... handle request ...
    Console.WriteLine($"Local request id: {localId}, Shared total seen so far: {TotalRequests}");
}
```

### Experiment

1. Send 5 requests.
2. Observe: `localId` is assigned once per request and never seen by another thread.
3. `TotalRequests` grows as every thread reads/writes the same variable.

### Observation

Why is `localId` safe but `TotalRequests` requires care?

`localId` is on the handler thread's stack. No other thread can see it. `TotalRequests` is a static field visible to every thread. Even the atomic `Interlocked.Increment` is needed because the variable is shared.

### Learning Note

Write in a new file `docs/learning/slice-4.4-shared-address-space.md`.

---

## Slice 4.5: Prepare Race Condition Lab

### OSTEP Context

- Chapter(s): 26 (data race diagram), 28 (Locks)
- Concept: non-atomic shared variable updates produce indeterminate results under concurrency.

### C#/.NET Mechanism

- `counter++` is not atomic. It is three operations: load, increment, store.
- `lock` provides mutual exclusion (not yet applied — leave it broken for observation).

### Build

Add a static `int UnsafeCounter = 0`. Each request increments it 10,000,000 times in a loop.

```csharp
for (int i = 0; i < 10_000_000; i++)
{
    UnsafeCounter++;
}
```

Log the final value after all increments.

### Experiment

1. Start server.
2. Send 2 concurrent requests.
3. Expected: 20,000,000. Actual: random, less than 20,000,000.
4. Repeat. Result changes.

### Observation

Why does 10M + 10M not equal 20M?

Two threads load the same stale value, increment their private register copy, and store back — overwriting each other's work. This is the data race from OSTEP Figure 26.7.

Do not fix it yet. The fix belongs to Milestone 5.

### Learning Note

Write in a new file `docs/learning/slice-4.5-race-condition-prep.md`.

---

## Slice 4.6: Thread-Per-Connection Limits

### OSTEP Context

- Chapter(s): 26 (thread creation cost), 27 (thread API)
- Concept: each thread consumes stack memory. Unbounded thread creation crashes the process.

### C#/.NET Mechanism

- Default thread stack size: ~1 MB in .NET.
- Creating thousands of threads exhausts memory.

### Build

No permanent code change. Add a configurable max-thread counter (optional) or simply document the limit observation.

### Experiment

1. Write a small client script that opens many TCP connections rapidly.
2. Observe server memory in Task Manager.
3. Note the point at which the server becomes unresponsive or throws `OutOfMemoryException`.

```powershell
# Client stress script concept (PowerShell)
1..10000 | ForEach-Object -Parallel {
    $client = New-Object System.Net.Sockets.TcpClient('localhost', 8080)
    Start-Sleep -Seconds 60  # hold connection
} -ThrottleLimit 100
```

### Observation

Why does thread-per-connection fail at scale?

Each thread's stack reserves memory. Thousands of threads = thousands of MB. The OS also spends CPU time context-switching between them. This directly motivates bounded thread pools (Milestone 6).

### Learning Note

Write in a new file `docs/learning/slice-4.6-thread-per-connection-limits.md`.

---

## Milestone 4 Completion Criteria

All six slices are:

- [ ] Built
- [ ] Experimented
- [ ] Noted
- [ ] Committed individually

After completion, update:

- `CONTEXT.md` to reflect concurrency capability.
- ADR 0001 to mark Milestone 4 as implemented.
- Learning notes aggregated or cross-referenced.

Milestone 5 (Race Conditions Lab) naturally follows because slice 4.5 already exposes the problem without fixing it.
