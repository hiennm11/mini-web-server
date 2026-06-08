# ADR 0002: Prove Single-Thread Blocking Before Adding Threads

## Status

Accepted

## Date

2026-06-05

## Context

Milestone 4 will teach thread-per-connection concurrency. The current server is intentionally single-threaded: one host process, one synchronous accept loop, and one client handled at a time.

Before adding threads, the project needs one observable baseline that proves the pain: a slow request blocks unrelated clients because the server cannot return to `Accept()` while it is blocked inside `HandleClient()`.

OSTEP Chapter 4 frames this through process states:

- **Running**: the process is executing instructions.
- **Ready**: the process can run, but the scheduler has not selected it.
- **Blocked**: the process cannot run until an event completes, such as I/O completion or a timer wakeup.

In a single-threaded blocking server, there is only one point of execution. If that thread blocks, the server cannot accept another client, parse another request, or send another response.

## Decision

Slice 4.1 will prove the baseline before any threading is introduced.

We will add a deliberate `/slow` path that blocks the current server thread for five seconds before responding. This slow path exists only as a learning instrument.

We will not add concurrency in this slice.

We will not refactor the server loop in this slice.

The experiment will show:

1. Client A requests `/slow`.
2. The server blocks inside `HandleClient()`.
3. Client B requests `/` during that blocked window.
4. Client B waits until Client A finishes.
5. The console log shows the second client is accepted only after the slow handler returns.

## Consequences

Good:

- The limitation of the current architecture becomes visible before it is fixed.
- The next slice has a clear motivation: keep accepting clients while one client is blocked.
- The OSTEP process-state model maps directly to server behavior.

Tradeoffs:

- `/slow` is artificial and not a production feature.
- The route deliberately makes the server worse so the learning problem is visible.

## Verification

Run the server, then use two client terminals:

```powershell
curl http://localhost:8080/slow
curl http://localhost:8080/
```

The `/` request should wait if it is started while `/slow` is sleeping.

The slice plan and observation note live in `docs/learning/slice-4.1-single-thread-blocking.md`.
