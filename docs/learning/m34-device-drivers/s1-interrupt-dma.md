# Slice 34.1: Interrupt-Driven I/O + DMA (Ch. 36 §36.2-§36.6)
> **What it does** — adds a `Device` simulator with three registers (status, command, data) accessible via PIO or MMIO + an interrupt callback. A `/device/run?scenario=pio-vs-dma` driver transfers N bytes both ways and reports CPU cycles burned + interrupt count, so the PIO-vs-DMA trade-off is observable.
## Surface
- `Device` (new class in `MiniScheduler`):
  - `Register Status { get; }`, `Register Command { set; }`, `Register Data { get; set; }` — the three canonical registers from OSEP §36.2 figure 36.3.
  - `Action OnInterrupt` — callback fired when the device raises an interrupt (e.g., DMA complete, per §36.4).
  - `DmaChannel ProgramDma(IntPtr hostAddr, int byteCount, Direction dir)` — programs the DMA channel per §36.5; on completion, raises `OnInterrupt`.
- `PioChannel.TransferBytes(int n)` — CPU-driven byte-by-byte loop; CPU is busy the whole time (§36.3 PIO).
- `DmaChannel.TransferBytes(int n)` — CPU programs the channel + returns (§36.5 DMA); gets an interrupt on completion.
- The comparison route reports cycles + interrupt count for both.
## Files
- `src/MiniWebServer.Host/MiniScheduler/Device.cs` — new file (~150 LOC).
- `src/MiniWebServer.Host/Program.cs` — new route scenario.
## Smoke evidence
- `/device/run?scenario=pio-vs-dma&transfer_bytes=1000` reports:
  - PIO: `cpuCycles ≈ 1000 × per-byte-cost + 1 sync wait` (§36.3 polling cost).
  - DMA: `cpuCycles ≈ channel-program-cost + interrupt-handler-cost << PIO` (§36.5 DMA savings).
  - Interrupt count: 1 (DMA) vs 0 (PIO — but more cycles spent).
- `/device/run?scenario=canonical-protocol` shows the §36.3 four-step handshake: poll `status` until `READY` → write `command = READ` → read `data` register → `status = COMPLETE`.
## Tests
- `tests/MiniWebServer.Host.Tests/Program.cs` adds:
  - `Run("device canonical protocol completes", ...)` — execute the §36.3 four-step read, assert `Data` register has expected value.
  - `Run("DMA burns fewer CPU cycles than PIO", ...)` — assert DMA's `cpuCycles` < PIO's for the same transfer (§36.5).
  - `Run("DMA raises interrupt on completion", ...)` — assert `OnInterrupt` fires exactly once per `TransferBytes` call (§36.4).
## Source documents
- `docs/learning/m34-device-drivers/overview.md` — milestone scope.
- `docs/learning/m11-raw-syscall-demo/overview.md` — predecessor M11 syscall-side of I/O.
- OSEP Ch. 36 §36.2 — canonical device (3-register interface).
- OSEP Ch. 36 §36.3 — canonical protocol (4-step polling, the PIO baseline).
- OSEP Ch. 36 §36.4 — interrupts as the alternative to polling.
- OSEP Ch. 36 §36.5 — DMA for large transfers.
- OSEP Ch. 36 §36.6 — PIO vs MMIO.
