# Milestone 15: ArrayPool in async mode

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How much memory does async mode allocate per connection, and can we reduce it via `ArrayPool<byte>`?

## Scope

In the async server (`AsyncServer`), rent the receive buffer (16 KB) and response buffer (4 KB) from `ArrayPool<byte>.Shared` instead of allocating fresh per connection. Measure working-set under 150 parked /slow clients.

## Slice

- **[s1-arraypool.md](./s1-arraypool.md)** — `AsyncServer.HandleClientAsync` uses `ArrayPool<byte>.Shared.Rent()` for receive + response buffers. `HttpResponse.WriteTo(byte[] dest)` enables in-place serialization. Measure under stress.

## Results (honest measurement)

| | Baseline | Under 150 parked /slow | Delta | Per-conn |
|---|---|---|---|---|
| M7 (pre-pool) | ~27 MB | **~172 MB** | +145 MB | ~970 KB |
| M15 (pooled) | ~27 MB | **~157 MB** | +130 MB | ~890 KB |

**Reduction: ~15 MB (~9%)** — not the predicted 172 → 21 MB win. Most remaining per-connection cost is from:
1. Async state machines parked at `Task.Delay(30000)` — each parked Task holds its continuation + captured locals on the GC heap.
2. `HttpRequest` + headers dictionary — allocated fresh per request.
3. Native socket buffers + GC heap fragmentation.

The `ArrayPool<byte>` change saves the predicted ~2.4 MB across 150 conns. To win more would require an `HttpRequest` object pool, replacing `Task.Delay` with a custom timer, or refactoring for `ValueTask` — out of scope for this slice.

## .NET mechanism

- `ArrayPool<byte>.Shared.Rent(int minSize)` — returns a buffer of length ≥ `minSize`. Often rounds up to the next power of 2.
- `ArrayPool<byte>.Shared.Return(buffer)` — returns it. Default `clearArray=false` (don't zero on return — fine since we always overwrite before sending).
- `HttpResponse.WriteTo(byte[] dest)` lets the response serialize in-place without an intermediate `ToBytes()` allocation.

## Files

- `src/MiniWebServer.Host/AsyncServer.cs` — `Rent` + `Return` for both buffers, wrapped in try/finally.
- `src/MiniWebServer.Host/HttpResponse.cs` — adds `TotalLength` getter + `WriteTo(byte[] dest)` returning int (bytes written).
