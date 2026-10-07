# ADR 0024: M34 A Canonical Device, Interrupts, DMA, and Register Access (OSEP Ch. 36 §36.2-§36.6)

## Status

Accepted

## Date

2026-10-07

## Context

M11 covered the syscall side of I/O — `open`, `read`, `close`. M34 covers the device side: how the kernel reaches a device at all.

§36.2 gives the interface every device presents: "the (simplified) device interface is comprised of three registers: a status register, which can be read to see the current status of the device; a command register, to tell the device to perform a certain task; and a data register to pass data to the device, or get data from the device."

§36.3 gives the protocol over them, and names it:

```c
While (STATUS == BUSY)      // wait until device is not busy
    ;
Write data to DATA register
Write command to COMMAND register   // starts the device
While (STATUS == BUSY)      // wait until device is done
    ;
```

> "When the main CPU is involved with the data movement (as in this example protocol), we refer to it as programmed I/O (PIO)."

§36.4 argues interrupts beat that, then immediately qualifies itself: "if a device is fast, it may be best to poll; if it is slow, interrupts, which allow overlap, are best", with a TIP that they "only really make sense for slow devices".

§36.5 introduces DMA: "a DMA engine is essentially a very specific device within a system that can orchestrate transfers between devices and main memory without much CPU intervention."

§36.6 compares explicit I/O instructions with memory-mapped I/O and finds no winner: "There is not some great advantage to one approach or the other."

### Three corrections to the milestone spec

**1. `IntPtr hostAddr` is meaningless and unsafe here.** §36.5 describes telling the engine "where the data lives in memory". In a simulator a `byte[]` *is* that memory; a pointer conveys nothing and only invites a segfault. The API takes the array.

**2. "PIO means zero interrupts" is not the chapter's definition.** §36.3 defines PIO by *who moves the bytes*, not by how completion is signalled. The route reports interrupt counts because that is what these implementations do, and says so.

**3. The spec dropped §36.4's actual conclusion.** Its scope described interrupts purely as a way to stop polling. The chapter's point is the opposite of a blanket recommendation — a fast device is better served by polling, and a hybrid beats both. `interrupt-vs-poll` exists to make that claim measurable; without it the milestone would teach half the section.

## Decision

### `Device` — clocked by hand

Three registers over a backing store, and a `Tick()` the caller advances. **The device runs on no thread.** Each protocol here is a claim about *when* the CPU looks, and a background thread would make the ordering depend on scheduling luck — which would make the costs untestable, not just non-deterministic.

Completion is tracked by an explicit outstanding-command flag rather than by the latency countdown, because a zero-latency device has nothing to count down and would otherwise never report done.

### `DeviceCostModel` — derived, and labelled as such

OSEP Ch. 36 states no numbers anywhere. The model has two properties the chapter does fix and four constants it does not:

- PIO's cost grows with the byte count, because §36.5 says the CPU copies "explicitly, one word at a time".
- DMA's does not, because §36.5 says the OS is "done with the transfer" once the channel is programmed.

The constants (`PerByteCycle`, `ProgramChannelCycles`, `DefaultInterruptCost`, `DefaultPollThresholdTicks`) are named, documented against the sentence they paraphrase, and adjustable. The crossover — the transfer size where DMA starts paying — falls out of the model rather than being asserted.

### Both access paths, deliberately equal

`CanonicalRead` reaches registers as properties; `CanonicalReadViaPorts` reaches the same registers as port reads. They share the same bounds checks, the same protocol and the same cost, because §36.6 says neither is better. The test that pins this is the chapter's "no great advantage" claim turned into an assertion.

## Consequences

### Positive

- **§36.2-§36.6 are runnable**, and the two quantitative claims the chapter only asserts become measurable: DMA's crossover, and the device speed at which polling overtakes interrupts.
- **The chapter's own hedges are preserved.** `interrupt-vs-poll` shows polling winning at 1 tick and the interrupt winning at 500 — which is §36.4's claim, not its marketing.
- **The three claims have separate oracles.** Table size in M32, catch probability in M33, and here the crossover: all derived from the text rather than invented.

### Negative

- **The constants are invented.** A different `ProgramChannelCycles` moves the crossover; nothing in OSEP pins it. The tests assert orderings and relationships, not the literal crossover, so a consistent re-tuning passes.
- **No real MMIO, interrupts, or DMA.** .NET in user mode cannot map device registers or take an interrupt; this models the structure, which is what the chapter is about.
- **Single device, single queue, flat interrupt priority.** §36.7-§36.9 (the driver abstraction, bus arbitration) are out of scope.
- **No write path through the canonical protocol.** `CanonicalRead` implements the read; §36.3's step 2 — writing data to the DATA register — is a no-op in a read and is labelled as such rather than faked.

## Verification

- Build clean (`dotnet build`).
- 12 tests: the §36.3 handshake returns the right bytes and ends Complete; BUSY is observable for exactly the device latency and completion fires one interrupt with no repeats on later ticks; a zero-latency device completes rather than hanging; DMA costs less than PIO at 4096 bytes with linear marginal cost for PIO and flat for DMA; the crossover and the exact tie one byte below it; one interrupt per DMA transfer in both directions; oversized DMA rejected without starting anything; the cost model stays positive and finite at `int.MaxValue`; interrupts lose on a fast device and win on a slow one; the hybrid beats both on a slow device and matches polling on a fast one; MMIO and port access return the same data at the same cost; and both paths reject an out-of-range read including a block index that would wrap.
- Mutation-checked: reverting the cost model to `int` arithmetic, restoring DMA truncation, and making extra ticks fire a duplicate interrupt each change the corresponding test's outcome. Removing the zero-latency completion path does **not** fail cleanly - the suite hangs, because that is the bug reproducing: §36.3's polling loop has no exit. It was confirmed by watching the run reach the timeout with no result, which is evidence the defect was real, but it is a weaker guarantee than a failing assertion.
- Smoke: all four scenarios return 200, unknown scenarios and bad parameters return 400, and 40 requests leave the thread count flat.

### Defects found by review after the slice's own tests were green

Six, all fixed:

1. **A zero-latency device hung every caller.** The completion countdown only fired when it started positive, so `latency=0` — an accepted route parameter — left the status BUSY forever and the §36.3 polling loop spun without end, occupying a worker permanently. A regression test now covers it, but note what it costs: the failure mode is a hang, not an assertion failure.
2. **The cost model overflowed to a negative cost.** `PollCycle + bytes * PerByteCycle` evaluated in `int` before widening, so `transfer_bytes=2147483647` reported PIO as cheaper than DMA by a negative margin.
3. **DMA silently truncated an oversized transfer**, copying only what fit and still reporting a successful completion with an interrupt.
4. **The port path read past the end of the device.** `CanonicalRead` bounds-checked and `CanonicalReadViaPorts` did not — inverting §36.6's "no great advantage" into a security-relevant difference. Both now check, in `long` so a large block index cannot wrap past the check.
5. **The canonical trace described register accesses that never happened**, claiming a DATA-register write on a read.
6. **One assertion could not fail**: a count was assigned `1` and then asserted to be `1`. It is now derived from `PollsExecuted`.

## Source Documents

- `docs/learning/m34-device-drivers/overview.md` — milestone scope.
- `docs/learning/m34-device-drivers/s1-interrupt-dma.md` — slice doc and measured results.
- `docs/learning/m11-raw-syscall-demo/overview.md` — the syscall side of I/O.
- OSEP Ch. 36 §36.2 — the three-register interface; §36.3 — the four-step protocol and the definition of PIO; §36.4 — interrupts, and when polling is better; §36.5 — DMA; §36.6 — explicit instructions versus memory-mapped I/O.