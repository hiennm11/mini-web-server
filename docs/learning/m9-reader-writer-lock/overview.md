# Milestone 9: Reader-Writer Lock

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How do you let multiple readers access a shared resource concurrently while writers get exclusive access?

## Scope

Add a `/stats-fast` route that returns cached process stats under a reader-writer lock, plus a `/stats-refresh` route that updates the cache under the write lock. The lock lets many concurrent `/stats-fast` calls run in parallel while serializing against `/stats-refresh`.

## Slice

- **[s1-reader-writer-lock.md](./s1-reader-writer-lock.md)** — `RequestStatsCache` class using `ReaderWriterLockSlim`. `Program.cs` adds `/stats-fast` (reader) and `/stats-refresh` (writer) routes.

## OSEP coverage

- **Ch. 31 Semaphores** (§31.5 Reader-Writer Locks — Figure 31.13). The classic implementation uses 2 semaphores (`lock` + `writelock`) and an integer `readers` counter.

OSEP §31.5 introduces the reader-writer lock:
- Many readers can hold the read lock concurrently.
- Writers need exclusive access (no readers, no other writers).
- First reader acquires the write lock; last reader releases it.

.NET's `ReaderWriterLockSlim` implements exactly this pattern (with starvation prevention since .NET 4.5).

## OSEP §-specific deviations

- OSEP §31.5 ends with: "reader-writer locks should be used with some caution. They often add more overhead... and thus do not end up speeding up performance as compared to just using simple and fast locking primitives."

  This matches our experience: for a tiny `Process` stats snapshot, the rwlock is more expensive than just calling `Process.GetCurrentProcess()` directly. The win is conceptual (cache locality + reduced syscalls), not throughput.

- OSEP §31.5 mentions readers-starving-writers risk. Our `ReaderWriterLockSlim` (since .NET 4.5) prevents this with a queue.

## Key OSEP quotes

> "Imagine a number of concurrent list operations, including inserts and simple lookups. While inserts change the state of the list (and thus a traditional critical section makes sense), lookups simply read the data structure; as long as we can guarantee that no insert is on-going, we can allow many lookups to proceed concurrently." (OSEP §31.5)

## .NET mechanism

- `System.Threading.ReaderWriterLockSlim` — `EnterReadLock()` / `ExitReadLock()` for readers, `EnterWriteLock()` / `ExitWriteLock()` for writers.
- Internally tracks reader count; multiple readers can hold the lock simultaneously. Writers wait for all current readers to release.
- `using` blocks ensure `ExitXLock()` is called even on exception.

## Files

- `src/MiniWebServer.Host/RequestStatsCache.cs` — `Read()` returns cached snapshot under read lock, `Refresh()` snapshots + writes under write lock.
- `src/MiniWebServer.Host/Program.cs` — `/stats-fast` (reader) + `/stats-refresh` (writer) routes.

## What this slice does NOT do

- Doesn't add `Interlocked.CompareExchange` for the snapshot read (could be lock-free, but the read-modify-write of the cache struct needs the lock).
- Doesn't measure throughput vs M5's `lock` (documented as a comparison in the slice doc).
