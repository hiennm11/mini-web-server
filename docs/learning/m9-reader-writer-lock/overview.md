# Milestone 9: Reader-Writer Lock

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How do you let multiple readers access a shared resource concurrently while writers get exclusive access?

## Scope

Add a `/stats-fast` route that returns cached process stats under a reader-writer lock, plus a `/stats-refresh` route that updates the cache under the write lock. The lock lets many concurrent `/stats-fast` calls run in parallel while serializing against `/stats-refresh`.

## Slice

- **[s1-reader-writer-lock.md](./s1-reader-writer-lock.md)** — `RequestStatsCache` class using `ReaderWriterLockSlim`. `Program.cs` adds `/stats-fast` (reader) and `/stats-refresh` (writer) routes.

## OSEP concept

- **Ch. 31 Semaphores**, §31.5 (the "Concurrent Linked List" / "Read-Write Lock" problem) — readers don't conflict with each other but writers need exclusive access.
- OSEP uses a pair of semaphores (`wlock` + `rlock`) to implement a readers-writer lock. The .NET `ReaderWriterLockSlim` is the equivalent: `EnterReadLock()` allows multiple readers, `EnterWriteLock()` is exclusive.

The classic RW-lock problem: **reader starvation**. If readers keep arriving, writers can wait forever. Real implementations (including `ReaderWriterLockSlim` since .NET 4.5) use an "any-writer-waits" queue to prevent starvation.

## .NET mechanism

- `System.Threading.ReaderWriterLockSlim` — `EnterReadLock()` / `ExitReadLock()` for readers, `EnterWriteLock()` / `ExitWriteLock()` for writers.
- The class internally tracks reader count; multiple readers can hold the lock simultaneously. Writers wait for all current readers to release.
- `using` blocks ensure `ExitXLock()` is called even on exception.

## Files

- `src/MiniWebServer.Host/RequestStatsCache.cs` — `Read()` returns cached snapshot under read lock, `Refresh()` snapshots + writes under write lock.
- `src/MiniWebServer.Host/Program.cs` — `/stats-fast` (reader) + `/stats-refresh` (writer) routes.

## What this slice does NOT do

- Doesn't add `Interlocked.CompareExchange` for the snapshot read (could be lock-free, but the read-modify-write of the cache struct needs the lock).
- Doesn't measure throughput vs M5's `lock` (documented as a comparison in the slice doc).
