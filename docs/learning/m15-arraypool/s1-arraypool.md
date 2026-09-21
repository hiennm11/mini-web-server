# Milestone 15: ArrayPool<byte> in async mode

## What changed

`src/MiniWebServer.Host/AsyncServer.cs` now rents the receive buffer (16 KB) and response buffer (4 KB) from `ArrayPool<byte>.Shared` instead of allocating fresh `byte[]` per connection.

`src/MiniWebServer.Host/HttpResponse.cs` adds `WriteTo(byte[] dest)` so the response can be serialized directly into a pooled buffer.

## Why

In async mode each accepted connection runs as a `Task` that drives `ReceiveAsync` / `SendAsync`. The previous code allocated two fresh arrays per connection:
- `byte[] buffer = new byte[ServerConfig.MaxRequestBytes]` — 16 KB receive buffer
- `byte[] responseBytes = response.ToBytes()` — small but per-conn allocation

The CONTEXT "Useful directions" line flagged this as the dominant per-connection memory cost: under 150 parked `/slow` clients the M7 async mode held ~172 MB of working set, mostly from those buffers being retained while the parked `Task.Delay` held the connection alive.

## Measured impact

Same stress as before (150 backgrounded curl.exe clients hitting `/slow`, which parks each server-side Task for 30s):

| Mode | Baseline | Under 150 parked | Delta | Per-conn |
|---|---|---|---|---|
| M7 (pre-pool) | ~27 MB | ~172 MB | +145 MB | ~970 KB |
| M15 (pooled)  | ~27 MB | ~157 MB | +130 MB | ~890 KB |

The ArrayPool change saves the **16 KB receive buffer + a few-KB response buffer per connection** (≈ 2.4 MB total across 150 conns). The remaining ~127 MB of overhead is from sources the pool can't reclaim:

1. **Async state machines parked at `Task.Delay(30000)`** — each parked Task holds its continuation + captured locals on the GC heap.
2. **`HttpRequest` + headers dictionary** — allocated fresh per request by `HttpRequestParser.Parse`. Could be object-pooled but is out of scope here.
3. **Native socket buffers** — each client socket has kernel-side buffer reservations.
4. **GC heap fragmentation** — many short-lived strings (response bodies, header lines) accumulate before collection.

## Honest assessment

The expected "172 MB → 21 MB" win predicted by the CONTEXT line **did not materialize** with a simple `ArrayPool<byte>.Shared.Rent`. The receive buffer alone wasn't the dominant cost — the async state machine and per-request objects are. To get a bigger win would require:

- A pooled `HttpRequest` object pool (replacing per-request allocations).
- Replacing `Task.Delay(30000)` with a custom timer to avoid the Task allocation per parked request.
- Reducing the async state machine footprint (e.g., using `ValueTask` where appropriate).

None of those are in this slice. This slice does the bounded, testable thing: pool the buffers. Document the real numbers.

## Smoke evidence (.gitnexus/smoke-m15.ps1)

```
=== M15 ArrayPool smoke ===
1. baseline working_set = 26.8 MB
2. spawning 150 /slow clients...
  spawned 150 processes
3. waiting 4 s for clients to park...
4. under load: working_set = 157.3 MB, total_requests = 153

=== summary ===
baseline: 26.8 MB
under 150 parked /slow clients: 157.3 MB
delta: +130.5 MB (~890.9 KB per parked client)
M7 pre-pool baseline: ~172 MB
Post-pool: ~157.3 MB (~890.9 KB/conn, dominated by Task overhead)
```

## OSEP concept

This slice is about **resource pooling** — a common OS-level pattern (slab allocator, free list, object pool). The .NET `ArrayPool<byte>` is essentially a thread-safe slab allocator for byte arrays. It's the same pattern the OS kernel uses for its own buffer caches (e.g., Linux's `kmem_cache`).

## .NET mechanism

- `ArrayPool<byte>.Shared` is the .NET-wide singleton pool. `Rent(size)` returns a buffer of length >= size (often rounded up to the next power of 2 or bucket size).
- `Return(buffer)` puts it back. The default `clearArray=false` means the buffer isn't zeroed on return — fine for our use since we always overwrite before sending.
- `HttpResponse.WriteTo(byte[] dest)` lets the response serialize in-place without an intermediate `ToBytes()` allocation.

## Files changed (slice 15)

- `src/MiniWebServer.Host/AsyncServer.cs` — rent receive buffer (16 KB) + response buffer (4 KB), wrap in try/finally for Return.
- `src/MiniWebServer.Host/HttpResponse.cs` — add `WriteTo(byte[] dest)` + `TotalLength` getter.

## Deferred

- Object pool for `HttpRequest` (bigger win but more refactoring).
- `ValueTask` for short paths (e.g., when content-length is small and we can write directly).
- Reduce the per-Task overhead of `Task.Delay(30000)` in the `/slow` handler.
