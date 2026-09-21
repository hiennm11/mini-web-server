# Slice 34.1: Interrupt-Driven I/O + DMA (§36.3-§36.6)
> **What it does** — adds a `Device` simulator with three registers (status, command, data) accessible via PIO or MMIO + an interrupt callback. A `/device/run?scenario=pio-vs-dma` driver transfers N bytes both ways and reports CPU cycles burned + interrupt count, so the PIO-vs-DMA trade-off is observable.
## Surface
- `Device` (new class in `MiniScheduler`):
  - `Register Status { get; }`, `Register Command { set; }`, `Register Data { get; set; }` — the three canonical registers.
  - `Action OnInterrupt` — callback fired when the device raises an interrupt (e.g., DMA complete).
  - `DmaChannel ProgramDma(IntPtr hostAddr, int byteCount, Direction dir)` — programs the DMA channel; on completion, raises `OnInterrupt`.
- `PioChannel.TransferBytes(int n)` — CPU-driven byte-by-byte loop; CPU is busy the whole time.
- `DmaChannel.TransferBytes(int n)` — CPU programs the channel + returns; gets an interrupt on completion.
- The comparison route reports cycles + interrupt count for both.
## Files
- `src/MiniWebServer.Host/MiniScheduler/Device.cs` — new file (~150 LOC).
- `src/MiniWebServer.Host/Program.cs` — new route scenario.
## Smoke evidence
- `/device/run?scenario=pio-vs-dma&transfer_bytes=1000` reports:
  - PIO: `cpuCycles ≈ 1000 × per-byte-cost + 1 sync wait`
  - DMA: `cpuCycles ≈ channel-program-cost + interrupt-handler-cost << PIO`
  - Interrupt count: 1 (DMA) vs 0 (PIO — but more cycles spent).
- `/device/run?scenario=canonical-protocol` shows the four-step handshake: poll `status` until `READY` → write `command = READ` → read `data` register → `status = COMPLETE`.
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds:
  - `Run("device canonical protocol completes", ...)` — execute the four-step read, assert `Data` register has expected value.
  - `Run("DMA burns fewer CPU cycles than PIO", ...)` — assert DMA's `cpuCycles` < PIO's for the same transfer.
  - `Run("DMA raises interrupt on completion", ...)` — assert `OnInterrupt` fires exactly once per `TransferBytes` call.
## Source documents
- `docs/learning/m34-device-drivers/overview.md` — milestone scope.
- `docs/learning/m11-raw-syscall-demo/overview.md` — predecessor M11 syscall-side of I/O.
- OSTEP Ch. 36 §36.3-§36.6 — canonical protocol, interrupts, DMA, MMIO.
