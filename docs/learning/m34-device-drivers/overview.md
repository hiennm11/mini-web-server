# Milestone 34: Device Drivers — Canonical Protocol + Interrupts + DMA + PIO/MMIO (Ch. 36 §36.2-§36.6)
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
What does a device look like to the OS? How do the four canonical device-protocol steps (poll status, write command, transfer data, poll status) compose into an interrupt-driven driver? What's the difference between PIO and DMA, and between PIO and memory-mapped I/O?
## Scope
A standalone **device-simulator** in `src/MiniWebServer.Host/MiniScheduler/Device.cs` that models a simple disk controller as three address-mapped registers + a DMA channel, following OSEP §36.2-§36.6:
- **§36.2 A Canonical Device**: models a device with three registers — `Status` (read state), `Command` (issue an operation), `Data` (transfer bytes).
- **§36.3 The Canonical Protocol**: the four-step handshake — poll `Status` until not busy → write `Data` → write `Command` → poll `Status` until done.
- **§36.4 Lowering CPU Overhead With Interrupts**: instead of polling, the device raises an interrupt on completion; the OS puts the calling process to sleep and runs another task.
- **§36.5 More Efficient Data Movement With DMA**: the CPU programs a DMA channel with source/dest/count, returns to other work, gets an interrupt when DMA completes. Burns fewer CPU cycles than PIO for large transfers.
- **§36.6 Methods Of Device Interaction**: explicit I/O instructions (x86 `in`/`out`, PIO) vs. memory-mapped I/O (MMIO — device registers appear as memory locations). We expose MMIO because .NET can't issue PIO.

The simulator compares PIO vs DMA via `/device/run?scenario=pio-vs-dma&transfer_bytes=N` and reports CPU cycles burned + interrupt count.

## Slice
- **[s1-interrupt-dma.md](./s1-interrupt-dma.md)** — device simulator + PIO/DMA comparison route.

## OSTEP coverage
- **Ch. 36 §36.2** "A Canonical Device": introduces the canonical 3-register device (status, command, data) + the internal structure (micro-controller, memory, hardware-specific chips).
- **Ch. 36 §36.3** "The Canonical Protocol": the four-step polling protocol. OSEP defines the polling-based protocol as PIO because "When the main CPU is involved with the data movement (as in this example protocol), we refer to it as **programmed I/O (PIO)**."
- **Ch. 36 §36.4** "Lowering CPU Overhead With Interrupts": interrupts as the solution to polling's CPU waste. ISR / interrupt handler. Trade-off: interrupts help slow devices; fast devices may be better off polling because of context-switch cost.
- **Ch. 36 §36.5** "More Efficient Data Movement With DMA": the DMA engine as a "very specific device within a system that can orchestrate transfers between devices and main memory without much CPU intervention."
- **Ch. 36 §36.6** "Methods Of Device Interaction": explicit I/O instructions (`in`/`out` on x86) vs. memory-mapped I/O. OSEP: "There is not some great advantage to one approach or the other."
- **Cross-reference**: M11 covered the syscall-side of I/O (`open`/`read`/`close`). M34 covers the device-side: how the kernel reaches the device registers.

## Files
- `src/MiniWebServer.Host/MiniScheduler/Device.cs` — new file. `Device`, `PioChannel`, `DmaChannel`, `InterruptController`.
- `src/MiniWebServer.Host/Program.cs` — `/device/run?scenario=pio-vs-dma&transfer_bytes=N` route.

## Implementation deviations from OSEP
- **Software-emulated cycles**: real CPU cycles are nanoseconds; the simulator counts in "abstract work units" so the PIO vs DMA comparison is observable in the smoke trace.
- **No real MMIO**: .NET runs in user-mode; we can't actually map device registers. The simulator models the access pattern via a struct field with `volatile`-like semantics.
- **No real interrupts**: the simulator uses a callback function instead of a hardware IRQ. The structural pattern is what matters.
- **Single device**: real systems have many devices on a shared bus with arbitration. The slice models one device.

## What this slice does NOT do
- **Bus arbitration** (Ch. 36 §36.7-§36.9) — multiple devices contending for the bus.
- **Interrupt priorities** — flat priority for all interrupts.
- **MSI/MSI-X** (message-signaled interrupts) — the modern PCI-e interrupt delivery.
- **Real hardware drivers** — this is a simulator, not a driver that talks to real hardware.
- **Multi-queue DMA** — modern NVMe has thousands of DMA queues; the slice models one.

## Where this leads
- This slice is the foundation for understanding what `FileStream.Read` ultimately does at the device level. A future slice could chain M11 (syscall) → M34 (device) → M32 (SSD FTL) to model the full read path top-to-bottom.
- M34 is intentionally the last "operating-system-flavoured" slice; subsequent work shifts to application-level security (M23 extensions) or code-health.
