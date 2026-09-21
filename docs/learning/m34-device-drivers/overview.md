# Milestone 34: Device Drivers — Interrupts, DMA, MMIO (§36.3-§36.6)
> **Overview** - what this milestone covers and where to start. The slice lives in this folder.
## Question
What does a device look like to the OS? How do the four canonical device-protocol steps (status read, command write, data read/write) compose into an interrupt-driven driver? What's the difference between PIO and DMA, and between PIO and memory-mapped I/O?
## Scope
A standalone **device-simulator** in `src/MiniWebServer.Host/MiniScheduler/Device.cs` that models a simple disk controller as three address-mapped registers + a DMA channel. The slice:
- Models a device with three registers (`status`, `command`, `data`) accessible via either PIO (`in`/`out` instructions on x86) or MMIO (memory-mapped loads/stores). The simulator exposes the MMIO side because .NET can't issue `in`/`out` directly.
- Demonstrates the canonical four-step protocol: read `status` until ready → write `command` → for DMA, also program the DMA channel with source/dest/count → poll or wait for interrupt → read `data` register (PIO) or read memory (DMA).
- Compares PIO (CPU reads/writes one byte at a time, blocking the device until each one completes) vs DMA (CPU programs the channel, returns to other work, gets an interrupt on completion). The simulator reports CPU cycles saved.
## Slice
- **[s1-interrupt-dma.md](./s1-interrupt-dma.md)** — device simulator + PIO/DMA comparison route.
## OSTEP coverage
- **Ch. 36 §36.3** "Canonical Protocol" + §36.4 "Lowering CPU Overhead With Interrupts" + §36.5 "Yet Lowering CPU Overhead With DMA" + §36.6 "How Do Devices Actually Operate?" — the full interrupt/DMA/MMIO story.
- **Cross-reference**: M11 covered the syscall-side of I/O (`open`/`read`/`close`). M34 covers the device-side: how the kernel reaches the device registers.
## Files
- `src/MiniWebServer.Host/MiniScheduler/Device.cs` — new file. `Device`, `PioChannel`, `DmaChannel`, `InterruptController`.
- `src/MiniWebServer.Host/Program.cs` — `/device/run?scenario=pio-vs-dma&transfer_bytes=N` route.
## Implementation deviations from OSEP
- **Software-emulated cycles**: real CPU cycles are nanoseconds; the simulator counts in "abstract work units" so the PIO vs DMA comparison is observable in the smoke trace.
- **No real MMIO**: .NET runs in user-mode; we can't actually map device registers. The simulator models the access pattern.
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
