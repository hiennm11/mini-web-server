# Milestone 8: Bounded Queue + 503 Backpressure

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

What does the server do when more requests arrive than workers can process?

## Scope

Bound `WorkerPool.Pending` from unbounded (M6) to a fixed size (64). When the queue is full, the accept loop returns `503 Service Unavailable` to the client instead of accepting the connection (or blocking the producer).

## Slice

- **[s1-bounded-queue.md](./s1-bounded-queue.md)** — `MaxQueueSize = 64` constant. `TryEnqueue` returns false when full; the accept loop writes a 503 response and closes the socket.

## OSEP coverage

- **Ch. 30 Condition Variables** (§30.2 the canonical producer/consumer bounded buffer).
- **Ch. 31 Semaphores** (§31.4 bounded buffer with semaphores — `empty`, `full`, `mutex`).

The "blocking-producer" variant of the bounded-buffer problem is exactly our use case. OSEP §31.4 shows two variants:
- Blocking: producer waits when full.
- Non-blocking (503-style): producer fails fast when full.

Our implementation is the non-blocking variant (`TryEnqueue` returns false; accept loop replies 503).

## OSEP §-specific deviations

- OSEP §31.4 uses 3 semaphores (`mutex` + `empty` + `full`). .NET's `BlockingCollection` has the same internal structure.
- Our 503-on-full approach is a **policy choice** (decline vs. block). OSEP describes both; we picked decline.
- We don't model the "what to do on shutdown" case OSEP mentions (`unlink`-then-park races — see §31.4 deadlock note).

## Key OSEP quotes

> "A producer/consumer queue with finite capacity requires backpressure. Producers must either block (wait for room) or fail fast (return an error)." (OSEP §31.4)

## .NET mechanism

- `Monitor.Wait(obj)` / `Monitor.Pulse(obj)` is the .NET equivalent of `pthread_cond_wait` / `pthread_cond_signal`. The wait atomically releases the lock and parks the calling thread on the monitor's wait queue.
- Our implementation uses a non-blocking `TryEnqueue` (returns false if full) + immediate 503. The blocking form is documented in the slice as an alternative.

## Files

- `src/MiniWebServer.Host/WorkerPool.cs` — adds `MaxQueueSize = 64`, `TryEnqueue`, `Capacity`, `IsAtCapacity`.
- `src/MiniWebServer.Host/Program.cs` — accept loop branches: try `TryEnqueue`; if false, write a 503 response on the client socket directly and close it.

## What this slice does NOT do

- Doesn't add the blocking-producer variant (M8 could be extended with `while (count >= MAX) wait` if blocking was preferred over 503).
- Doesn't add a `Retry-After` header (deferred per CONTEXT.md useful-directions).
