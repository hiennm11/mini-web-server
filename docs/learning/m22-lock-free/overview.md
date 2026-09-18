# Milestone 22: Lock-free Data Structures (CAS)

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

Can you build concurrent data structures without locks at all? What does Compare-And-Swap (CAS) buy you, and what does it cost?

## Scope

Two lock-free primitives built with the CAS retry pattern (OSEP §32.2 "Herlihy's idea"):
- **`AtomicCounter`** — `Increment()` via CAS retry on `Interlocked.CompareExchange`. Matches OSEP §32.2 `AtomicIncrement` pseudocode exactly.
- **`LockFreeStack<T>`** — Treiber stack. Push/pop via CAS on the head pointer. Matches OSEP §32.2 "lock-free list insert" pseudocode.

Plus lock-based comparison primitives (`LockedCounter`, `LockedStack`) so the benchmark shows the difference.

Exposed via `/lockfree/bench?impl=atomic|locked|stack-atomic|stack-locked&threads=N&ops=M` HTTP route. Reports elapsed time, ops/sec, and final value.

## Slice

- **[s1-cas-primitives.md](./s1-cas-primitives.md)** — `AtomicCounter`, `LockFreeStack<T>`, `LockedCounter`, `LockedStack<T>`, `LockFreeBenchmark`. HTTP route.

## OSEP coverage

- **Ch. 32 Common Concurrency Problems** (§32.2 Atomicity-Violation Bugs — but specifically the lock-free fix at the end of §32.3 "Mutual Exclusion").

OSEP §32.3 introduces the lock-free idea:
> "one could design various data structures without locks at all [H91, H93]. The idea behind these lock-free (and related wait-free) approaches here is simple: using powerful hardware instructions, you can build data structures in a manner that does not require explicit locking."

OSEP §32.3 gives the canonical pseudocode:
> "void AtomicIncrement(int *value, int amount) {
>     do {
>         int old = *value;
>     } while (CompareAndSwap(value, old, old + amount) == 0);
> }"

> "Instead of acquiring a lock, doing the update, and then releasing it, we have instead built an approach that repeatedly tries to update the value to the new amount and uses the compare-and-swap to do so. In this manner, no lock is acquired, and no deadlock can arise (though livelock is still a possibility)."

OSEP §32.3 "lock-free list insert":
> "void insert(int value) {
>     node_t *n = malloc(sizeof(node_t));
>     n->value = value;
>     do {
>         n->next = head;
>     } while (CompareAndSwap(&head, n->next, n) == 0);
> }"

## OSEP §-specific deviations

| OSEP §32.3 | We do | We defer |
|---|---|---|
| §32.3 `CompareAndSwap` | `Interlocked.CompareExchange` (x86 `CMPXCHG` wrapper). | Hand-coded assembly. |
| §32.3 `AtomicIncrement` retry loop | Exact match (SpinWait backoff). | Exponential backoff with random jitter. |
| §32.3 lock-free list insert | Exact match — CAS on `head` pointer. | Michael-Scott hazard-pointer safe reclamation. |
| §32.3 ABA problem | Mitigated by **never freeing popped nodes** (nodes leak). | Hazard pointers [Michael 2004], epoch-based reclamation. |
| §32.3 Livelock | Mitigated by `SpinWait.SpinOnce()` (which yields after a few spins). | Randomized backoff. |
| §32.2 atomicity/order bugs | Not the focus — these are CV/lock fixes. | N/A. |
| §32.3 deadlock detection/prevention | Not the focus. | N/A. |

## Key OSEP quotes

> "Imagine we now wanted to atomically increment a value by a certain amount, using compare-and-swap. We could do so with the following simple function: `void AtomicIncrement(int *value, int amount) { do { int old = *value; } while (CompareAndSwap(value, old, old + amount) == 0); }`" (OSEP §32.3)

> "Instead of acquiring a lock, doing the update, and then releasing it, we have instead built an approach that repeatedly tries to update the value to the new amount and uses the compare-and-swap to do so. In this manner, no lock is acquired, and no deadlock can arise (though livelock is still a possibility, and thus a robust solution will be more complex than the simple code snippet above)." (OSEP §32.3)

> "Of course, we could solve [the race in `insert`] by surrounding this code with a lock acquire and release ... Instead, let us try to perform this insertion in a lock-free manner simply using the compare-and-swap instruction." (OSEP §32.3)

> "Of course, building a useful list requires more than just a list insert, and not surprisingly building a list that you can insert into, delete from, and perform lookups on in a lock-free manner is non-trivial. Read the rich literature on lock-free and wait-free synchronization to learn more [H01, H91, H93]." (OSEP §32.3)

## .NET mechanism

- `System.Threading.Interlocked.CompareExchange(ref int, int, int)` — atomic CAS on a 32-bit int. Returns the original value; if equal to `expected`, the memory was set to `new`.
- `System.Threading.SpinWait` — backoff primitive that spins a few times then yields to the OS thread.
- `System.Threading.Volatile.Read/Write` — acquire/release fences on reads/writes.

## Files

- `src/MiniWebServer.Host/MiniScheduler/LockFreePrimitives.cs` (new, ~280 lines):
  - `AtomicCounter` (CAS-based counter, matches OSEP §32.3 pseudocode).
  - `LockFreeStack<T>` (Treiber stack, matches OSEP §32.3 list insert).
  - `LockedCounter` / `LockedStack<T>` (comparison primitives).
  - `LockFreeBenchmark` (multi-thread benchmark with `Barrier` sync).
- `src/MiniWebServer.Host/Program.cs` — `/lockfree/bench?impl=atomic|locked|stack-atomic|stack-locked&threads=N&ops=M` route.

## What this slice does NOT do

- Hazard-pointer safe memory reclamation — we just leak popped nodes.
- Multi-CPU NUMA-aware placement — out of scope.
- Wait-free guarantees (vs lock-free) — our stack retries on CAS failure, so a starved thread could starve forever.
- Compare to .NET's built-in `ConcurrentStack<T>` — out of scope for this educational slice.
- The full `Interlocked` API (add, exchange, read, etc.) — we only demonstrate the pattern.

## Where this leads

The roadmap continues with **M21 FFS** (Ch. 41) — different topic (file system). The lock-free work could be extended later:
- Compare to `System.Collections.Concurrent.ConcurrentStack<T>`.
- Lock-free linked list with hazard pointers (Harris 2001).
- Lock-free queue (Michael-Scott 1996).
- Seqlock / RCU.
