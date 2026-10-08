# Slice 34.1: A Canonical Device, Interrupts, DMA, and How Registers Are Reached (Ch. 36 §36.2-§36.6)

> **What it does** — adds a device simulator with the three canonical registers, implements §36.3's four-step protocol, and makes the three claims the chapter argues for measurable: that polling wastes CPU, that DMA removes the copying from the CPU, and that neither polling nor interrupts wins on every device.

## Surface

- `Device` — three registers over a backing store.
  - `Status` (read), `Command` (write), `Data` (read/write) — §36.2's figure 36.3 interface.
  - `Tick()` advances the device clock by one tick. **The device is clocked by hand, not by a thread.** Every protocol here is a claim about *when* the CPU looks, and a background thread would make the ordering depend on scheduling luck.
  - `CanonicalRead(block, length)` — §36.3's four steps, and therefore the definition of PIO.
  - `CanonicalReadViaPorts(block, length)` — the same four steps reaching each register through a port, for §36.6.
  - `DmaTransferToDevice(byte[] buffer, bool fromDevice)` — programs the channel, then the engine copies and raises one interrupt.
  - `OnInterrupt`, `InterruptsRaised`, `PollsExecuted`, `CpuCycles`, `IsBusy`.
- `DeviceCostModel` — `PioCycles(n)`, `DmaCycles(n)`, `DmaCrossoverBytes`, `PollDriveCycles`, `InterruptDriveCycles`, `HybridDriveCycles`.
- `DeviceDemos` — the route's three report formats.

## Three corrections to the original spec

1. **`IntPtr hostAddr` is meaningless in a simulator, and unsafe.** §36.5 describes telling the engine "where the data lives in memory". In a simulator the `byte[]` *is* that memory; a raw pointer carries no meaning here and only invites a segfault. `DmaTransferToDevice(byte[], bool)`.

2. **"PIO means zero interrupts" is not what the chapter says.** §36.3 defines PIO by *who moves the data*: "When the main CPU is involved with the data movement (as in this example protocol), we refer to it as programmed I/O (PIO)." That is a statement about the byte copying, not about how completion is signalled. The route's interrupt column describes what these particular implementations do.

3. **The spec omitted §36.4's most important claim.** The chapter says plainly: "if a device is fast, it may be best to poll; if it is slow, interrupts, which allow overlap, are best", and its TIP adds "they only really make sense for slow devices". A milestone that taught "interrupts replace polling" would teach half the section. `interrupt-vs-poll` and `HybridDriveCycles` cover the missing half.

## The cost model

OSEP Ch. 36 states **no numbers at all**. The model here is derived from the chapter's qualitative claims, with every constant named so a reader can see what rests on it:

| Quantity | Value | Where it comes from |
|---|---|---|
| `PerByteCycle` | 1 | §36.5: PIO copies "explicitly, one word at a time" |
| `ProgramChannelCycles` | 100 | §36.5: "telling it where the data lives in memory, how much data to copy, and which device to send it to" |
| `DefaultInterruptCost` | 40 | §36.4: "handling the interrupt, and switching back to the issuing process is expensive" |
| `DefaultPollThresholdTicks` | 8 | §36.4's "hybrid that polls for a little while and then... uses interrupts" |

The *shape* is what the chapter fixes, not the values: PIO's cost grows with the byte count because the CPU copies each byte, and DMA's does not because the CPU is "done with the transfer" once the channel is programmed.

## Measured results

`scenario=pio-vs-dma`:

| bytes | PIO cycles | DMA cycles | cheaper |
|---|---|---|---|
| 1 | 2 | 140 | PIO |
| 32 | 33 | 140 | PIO |
| 128 | 129 | 140 | PIO |
| 139 | 140 | 140 | tie |
| 140 | 141 | 140 | **DMA** |
| 1024 | 1025 | 140 | DMA |
| 4096 | 4097 | 140 | DMA |

**DMA crossover: 140 bytes.** The chapter frames DMA as the answer to transferring "a large chunk of data" but never gives a number; the threshold falls out of the model. Below it the channel setup costs more than the bytes it saves.

`scenario=interrupt-vs-poll`:

| device latency | poll | interrupt | hybrid | cheapest |
|---|---|---|---|---|
| 1 tick | 2 | 41 | 2 | **poll** |
| 500 ticks | 501 | 41 | 49 | **interrupt** |

This is §36.4's claim made visible: the same driver is wrong on half the devices.

`scenario=mmio`: memory-mapped and port access return identical data at identical cost — 21 cycles and 6 register reads each — which is §36.6's "There is not some great advantage to one approach or the other."

## Bugs found by review, after the slice's own tests were green

1. **A zero-latency device hung every caller.** The countdown that completes a command only fired when it started positive, so `latency=0` left the status BUSY forever and §36.3's polling loop spun without end. `latency=0` is an accepted route parameter, so this occupied a worker permanently. Completion is now tracked by an explicit outstanding-command flag.
2. **The cost model overflowed to a negative cost.** `PollCycle + bytes * PerByteCycle` was evaluated in `int` before widening to `double`, so `transfer_bytes=2147483647` reported PIO as *cheaper than* DMA by a negative margin. The arithmetic is now done in `double` from the start.
3. **DMA silently truncated an oversized transfer.** A buffer larger than the device copied only what fit and still reported a successful completion with an interrupt — dropping write data, or leaving stale bytes in a read buffer with nothing to say so. Oversized transfers are now rejected.
4. **The port path read past the end of the device.** `CanonicalRead` bounds-checked its offset and `CanonicalReadViaPorts` did not, so the same out-of-range read that threw on one path indexed off the array on the other — exactly the "no great advantage" §36.6 argues for, inverted. Both paths now check, in `long` so a large block index cannot wrap past the check.
5. **The canonical trace described accesses that never happened.** It claimed the OS wrote the DATA register on step 2 and read it on step 4; a read does neither, and the results are copied from the device once it reports Complete. The table now says so.
6. **One assertion could not fail.** The BUSY test assigned `readyObservations = 1` and then asserted it was 1. It now derives the count from `PollsExecuted`.

## Tests

- `device canonical protocol reads a block through the three registers` — §36.3's handshake returns the right bytes from the right block and ends Complete.
- `device reports BUSY between the command and its completion` — BUSY is observable for exactly the latency in ticks, completion fires one interrupt, and extra ticks do not fire another.
- `a zero-latency device still completes its command` — `latency=0` completes rather than hanging.
- `DMA burns fewer CPU cycles than PIO for a large transfer` — and PIO's marginal cost is linear while DMA's is flat.
- `PIO beats DMA below the crossover and DMA wins above it` — including the exact tie one byte below.
- `DMA raises exactly one interrupt per transfer` — one interrupt per transfer in both directions, and the bytes land.
- `DMA refuses a transfer larger than the device` — rejection leaves nothing started.
- `cost model stays finite for very large transfers` — `int.MaxValue` bytes still produces a finite, positive cost.
- `interrupt loses to polling on a fast device and wins on a slow one` — §36.4's claim as an inequality in both directions.
- `hybrid polls a fast device and falls back to an interrupt on a slow one` — the two-phased approach. It does **not** beat both strategies: at 500 ticks it costs 49 cycles against the interrupt's 41. The test asserts the comparisons that hold and pins the ordering that breaks, rather than re-asserting §36.4's "best of both worlds" as a property of this model.
- `MMIO and explicit I/O reach the same registers at the same cost` — §36.6's "no great advantage", pinned.
- `both register-access paths refuse a read past the end of the device` — including a block index that would wrap.

The cost-model, DMA-truncation and duplicate-interrupt assertions were mutation-checked. Two limits are worth stating plainly. The exact per-byte and per-poll constants are pinned only through the crossover and the orderings, so a consistent re-tuning would pass. And the zero-latency regression does not fail cleanly - removing the fix hangs the suite, because §36.3's polling loop has no exit; that test proves the defect was real, but it is a weaker guarantee than a failing assertion.

## What this slice does NOT do

- **Bus arbitration** (§36.7-§36.9) — many devices contending for the bus.
- **Device drivers proper** (§36.7) — the OS abstraction layer above a device. This slice is the device side; M11 was the syscall side.
- **Interrupt priorities, MSI/MSI-X, multi-queue DMA** — all flat, single-queue, single-priority here.
- **Real hardware.** .NET cannot map device registers, so MMIO is modelled by an access spelling, not by a memory mapping.

## Source documents

- `docs/learning/m11-raw-syscall-demo/overview.md` — predecessor: the syscall side of I/O.
- `docs/adr/0024-m34-device-drivers.md` — decision record.
- OSEP Ch. 36 §36.2 — the three-register interface and the device's internal structure.
- OSEP Ch. 36 §36.3 — the four-step protocol, and the definition of PIO.
- OSEP Ch. 36 §36.4 — interrupts, the handler, and when polling is better.
- OSEP Ch. 36 §36.5 — DMA, and the CPU being "done with the transfer" once programmed.
- OSEP Ch. 36 §36.6 — explicit I/O instructions vs memory-mapped I/O.