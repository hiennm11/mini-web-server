# Milestone 8: Bounded Queue + 503 Backpressure

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

What does the server do when more requests arrive than workers can process?

## Scope

Bound `WorkerPool.Pending` from unbounded (M6) to a fixed size (64). When the queue is full, the accept loop returns `503 Service Unavailable` to the client instead of accepting the connection (or blocking the producer).

## Slice

- **[s1-bounded-queue.md](./s1-bounded-queue.md)** — `MaxQueueSize = 64` constant. `TryEnqueue` returns false when full; the accept loop writes a 503 response and closes the socket.

## OSEP concept

- **Ch. 30** (Condition Variables), §30.2 figure 30.12 — the two-CVs bounded-buffer pattern: `while (count == MAX) wait`. OSEP shows the blocking form.
- **Ch. 31** (Semaphores), §31.4 — bounded-buffer variant with `sem_wait`/`sem_post`.

The lesson is that an unbounded queue + unbounded work rate = unbounded memory. The M6 worker pool accepts bursts of any size — `Queue<Socket>` has no cap. Under sustained overload, memory grows without limit (each `Socket` + its kernel buffer is a few KB; multiplied by millions of unprocessed connections, the process runs out of address space before workers ever wake up). Bounded queue + backpressure prevents that and gives the application a real choice about what "too many" means.

## .NET mechanism

- `Monitor.Wait(obj)` / `Monitor.Pulse(obj)` is the .NET equivalent of `pthread_cond_wait` / `pthread_cond_signal`. The wait atomically releases the lock and parks the calling thread on the monitor's wait queue.
- Our implementation uses a non-blocking `TryEnqueue` (returns false if full) + immediate 503. The blocking form is documented in the slice as an alternative.

## Files

- `src/MiniWebServer.Host/WorkerPool.cs` — adds `MaxQueueSize = 64`, `TryEnqueue`, `Capacity`, `IsAtCapacity`.
- `src/MiniWebServer.Host/Program.cs` — accept loop branches: try `TryEnqueue`; if false, write a 503 response on the client socket directly and close it.

## What this slice does NOT do

- Doesn't add the blocking-producer variant (M8 could be extended with `while (count >= MAX) wait` if blocking was preferred over 503).
- Doesn't add a `Retry-After` header (deferred per CONTEXT.md useful-directions).
