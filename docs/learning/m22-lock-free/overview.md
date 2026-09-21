# Milestone 22: Lock-free Data Structures (CAS)

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

Can you build concurrent data structures without locks at all? What does Compare-And-Swap (CAS) buy you, and what does it cost?

## Scope

Two lock-free primitives built with the CAS retry pattern (OSEP §29.1 "Concurrent Counters" + §29.2 "Concurrent Linked Lists"):
- **`AtomicCounter`** — `Increment()` via CAS retry on `Interlocked.CompareExchange`. Matches OSEP §29.1 `AtomicIncrement` pseudocode exactly.
- **`LockFreeStack<T>`** — Treiber stack. Push/pop via CAS on the head pointer. Matches OSEP §29.2 "lock-free list insert" pseudocode.

Plus lock-based comparison primitives (`LockedCounter`, `LockedStack`) so the benchmark shows the difference.

Exposed via `/lockfree/bench?impl=atomic|locked|stack-atomic|stack-locked&threads=N&ops=M` HTTP route. Reports elapsed time, ops/sec, and final value.

## Slice

- **[s1-cas-primitives.md](./s1-cas-primitives.md)** — `AtomicCounter`, `LockFreeStack<T>`, `LockedCounter`, `LockedStack<T>`, `LockFreeBenchmark`. HTTP route.

## OSTEP coverage

- **Ch. 29 §29.1** "Concurrent Counters" [OS+AD14]: introduces the basic concurrent counter (with single lock) + the lock-free `AtomicIncrement` via CAS as the more concurrent alternative. OSEP §29.1 gives the canonical pseudocode:

  > "void AtomicIncrement(int *value, int amount) {
  >     do {
  >         int old = *value;
  >     } while (CompareAndSwap(value, old, old + amount) == 0);
  > }"

  > "Instead of acquiring a lock, doing the update, and then releasing it, we have instead built an approach that repeatedly tries to update the value to the new amount and uses the compare-and-swap to do so. In this manner, no lock is acquired, and no deadlock can arise (though livelock is still a possibility, and thus a robust solution will be more complex than the simple code snippet above)."

- **Ch. 29 §29.2** "Concurrent Linked Lists" [OS+AD14]: gives the canonical lock-free list insert:

  > "void insert(int value) {
  >     node_t *n = malloc(sizeof(node_t));
  >     n->value = value;
  >     do {
  >         n->next = head;
  >     } while (CompareAndSwap(&head, n->next, n) == 0);
  > }"

  > "Of course, building a useful list requires more than just a list insert, and not surprisingly building a list that you can insert into, delete from, and perform lookups on in a lock-free manner is non-trivial. Read the rich literature on lock-free and wait-free synchronization to learn more [H01, H91, H93]."

- **Cross-reference** (NOT in OSEP Ch. 32 §32.3 as the previous version of this overview claimed — that section is "Deadlock Bugs" and the lock-free idea only appears there as a one-line aside): the canonical coverage of CAS-based lock-free primitives is in **Ch. 29 §29.1-§29.2** ("Lock-based Concurrent Data Structures"). The §32.3 mention of CAS in the Mutual-Exclusion prevention subsection is a one-paragraph aside pointing readers to Herlihy for the full story; M22 takes its primary citations from §29.

## OSEP §-specific deviations

| OSEP § | We do | We defer |
|---|---|---|
| §29.1 `CompareAndSwap` | `Interlocked.CompareExchange` (x86 `CMPXCHG` wrapper). | Hand-coded assembly. |
| §29.1 `AtomicIncrement` retry loop | Exact match (SpinWait backoff). | Exponential backoff with random jitter. |
| §29.2 lock-free list insert | Exact match — CAS on `head` pointer (Treiber stack). | Michael-Scott hazard-pointer safe reclamation. |
| §29.2 ABA problem | Mitigated by **never freeing popped nodes** (nodes leak). | Hazard pointers [Michael 2004], epoch-based reclamation. |
| §29.2 Livelock | Mitigated by `SpinWait.SpinOnce()` (which yields after a few spins). | Randomized backoff. |

## Key OSEP quotes

> "Imagine we now wanted to atomically increment a value by a certain amount, using compare-and-swap. We could do so with the following simple function: `void AtomicIncrement(int *value, int amount) { do { int old = *value; } while (CompareAndSwap(value, old, old + amount) == 0); }`" (OSEP §29.1)

> "Instead of acquiring a lock, doing the update, and then releasing it, we have instead built an approach that repeatedly tries to update the value to the new amount and uses the compare-and-swap to do so. In this manner, no lock is acquired, and no deadlock can arise (though livelock is still a possibility, and thus a robust solution will be more complex than the simple code snippet above)." (OSEP §29.1)

> "Of course, we could solve [the race in `insert`] by surrounding this code with a lock acquire and release ... Instead, let us try to perform this insertion in a lock-free manner simply using the compare-and-swap instruction." (OSEP §29.2)

> "Of course, building a useful list requires more than just a list insert, and not surprisingly building a list that you can insert into, delete from, and perform lookups on in a lock-free manner is non-trivial. Read the rich literature on lock-free and wait-free synchronization to learn more [H01, H91, H93]." (OSEP §29.2)

## .NET mechanism

- `System.Threading.Interlocked.CompareExchange(ref int, int, int)` — atomic CAS on a 32-bit int. Returns the original value; if equal to `expected`, the memory was set to `new`.
- `System.Threading.SpinWait` — backoff primitive that spins a few times then yields to the OS thread.
- `System.Threading.Volatile.Read/Write` — acquire/release fences on reads/writes.

## Files

- `src/MiniWebServer.Host/MiniScheduler/LockFreePrimitives.cs` (new, ~280 lines):
  - `AtomicCounter` (CAS-based counter, matches OSEP §29.1 pseudocode).
  - `LockFreeStack<T>` (Treiber stack, matches OSEP §29.2 list insert).
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
