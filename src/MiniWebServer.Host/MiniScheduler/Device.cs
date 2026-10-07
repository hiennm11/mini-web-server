using System.Text;

namespace MiniWebServer.Host.MiniScheduler;

/// <summary>What the status register reports (OSEP §36.2, §36.3).</summary>
public enum DeviceStatus
{
    /// <summary>Not working: the OS may write data and a command.</summary>
    Idle,

    /// <summary>Executing the last command. §36.3's polling loop spins on this.</summary>
    Busy,

    /// <summary>The last command finished; an error code may be in the status.</summary>
    Complete,

    /// <summary>The last command failed.</summary>
    Error,
}

/// <summary>What the command register accepts (OSEP §36.2).</summary>
public enum DeviceCommand
{
    None = 0,
    Read = 1,
    Write = 2,
}

/// <summary>
/// A canonical device: three registers and an internal backing store
/// (OSEP §36.2 "A Canonical Device").
/// </summary>
/// <remarks>
/// §36.2, verbatim: "the (simplified) device interface is comprised of three
/// registers: a status register, which can be read to see the current status of
/// the device; a command register, to tell the device to perform a certain
/// task; and a data register to pass data to the device, or get data from the
/// device."
///
/// The device is clocked explicitly through <see cref="Tick"/> rather than
/// running on its own thread. Every protocol in this file - §36.3's polling
/// handshake, §36.4's interrupt, §36.5's DMA - is a statement about *when* the
/// CPU looks, and a background thread would make the ordering depend on
/// scheduling luck. Ticking by hand makes each one deterministic and lets a
/// test count the CPU cycles the OS would have burned.
/// </remarks>
public sealed class Device
{
    public readonly byte[] BackingStore;

    private readonly int _latencyTicks;

    private DeviceStatus _status = DeviceStatus.Idle;
    private DeviceCommand _command = DeviceCommand.None;
    private byte _data;
    private int _remainingTicks;
    private int _pendingBlock;
    private int _pendingLength;
    private int _dataCursor;

    // Whether a non-DMA command is outstanding. This is separate from
    // _remainingTicks because a device may have zero latency, and a command with
    // nothing left to wait for still has to complete on its next tick.
    private bool _commandOutstanding;

    // The transfer a DMA channel has been programmed to perform. The DMA engine
    // moves these bytes itself, so no per-byte CPU work is charged anywhere.
    private byte[]? _dmaBuffer;
    private bool _dmaFromDevice;

    /// <summary>CPU cycles charged to the OS so far, in abstract units.</summary>
    public long CpuCycles { get; private set; }

    /// <summary>Status reads issued by the OS - §36.3's polling loop.</summary>
    public long PollsExecuted { get; private set; }

    /// <summary>Interrupts the device has raised (§36.4).</summary>
    public long InterruptsRaised { get; private set; }

    /// <summary>True while the device still owes the OS a completion.</summary>
    public bool IsBusy => _commandOutstanding || _remainingTicks > 0 || _dmaBuffer is not null;

    /// <summary>Fired when the device raises an interrupt (§36.4).</summary>
    public Action? OnInterrupt { get; set; }

    public DeviceStatus Status
    {
        get { PollsExecuted++; return _status; }
    }

    public DeviceCommand Command
    {
        get => _command;
        set
        {
            _command = value;
            if (value == DeviceCommand.None) return;
            _status = DeviceStatus.Busy;
            _remainingTicks = _latencyTicks;
            _dataCursor = 0;
            _commandOutstanding = true;
            // §36.3: "Write command to COMMAND register (starts the device and
            // executes the command)".
            CpuCycles++;
        }
    }

    public byte Data
    {
        get { CpuCycles++; return _data; }
        set { CpuCycles++; _data = value; }
    }

    public Device(int payloadSize, int latencyTicks)
    {
        if (payloadSize < 1) throw new ArgumentOutOfRangeException(nameof(payloadSize), "a device needs at least one byte of storage");
        if (latencyTicks < 0) throw new ArgumentOutOfRangeException(nameof(latencyTicks), "latency cannot be negative");

        BackingStore = new byte[payloadSize];
        _latencyTicks = latencyTicks;
    }

    /// <summary>
    /// Advance the device clock by one tick, finishing whatever it was doing.
    /// </summary>
    public void Tick()
    {
        if (_remainingTicks > 0)
        {
            if (--_remainingTicks > 0) return;
            CompleteWork();
            return;
        }

        // A zero-latency device still has to complete the command it accepted:
        // the countdown above never fires for it, so without this the request
        // would spin in the polling loop forever.
        if (_commandOutstanding)
        {
            CompleteWork();
            return;
        }

        if (_dmaBuffer is null) return;
        FinishDma();
    }

    /// <summary>
    /// The DMA engine moves the bytes itself. Nothing here is charged to the
    /// CPU - §36.5's claim is that "the copying of data is now handled by the
    /// DMA controller" - and the transfer raises exactly one interrupt when it
    /// completes.
    /// </summary>
    private void FinishDma()
    {
        // The transfer size was validated when the channel was programmed, so
        // this copy is exact rather than clamped.
        var buffer = _dmaBuffer!;
        if (_dmaFromDevice)
        {
            for (int i = 0; i < buffer.Length; i++) buffer[i] = BackingStore[i];
        }
        else
        {
            for (int i = 0; i < buffer.Length; i++) BackingStore[i] = buffer[i];
        }
        _dmaBuffer = null;
        _status = DeviceStatus.Complete;
        InterruptsRaised++;
        // §36.5: "When the DMA is complete, the DMA controller raises an
        // interrupt, and the OS thus knows the transfer is complete."
        OnInterrupt?.Invoke();
    }

    /// <summary>Run ticks until the device has nothing outstanding.</summary>
    public void RunUntilIdle()
    {
        int guard = 0;
        while (IsBusy)
        {
            Tick();
            if (++guard > _latencyTicks + 1_000_000)
                throw new InvalidOperationException("device did not settle");
        }
    }

    private void CompleteWork()
    {
        // Clear the outstanding marker *before* invoking the handler: the
        // handler may re-enter the device, and a completion must not be able to
        // fire twice for one command.
        _commandOutstanding = false;
        _status = DeviceStatus.Complete;
        InterruptsRaised++;
        OnInterrupt?.Invoke();
    }

    /// <summary>
    /// §36.3's four-step polling protocol, returning the bytes read. This is
    /// also the definition of PIO: "When the main CPU is involved with the data
    /// movement (as in this example protocol), we refer to it as programmed I/O".
    /// </summary>
    public byte[] CanonicalRead(int block, int length)
    {
        if (block < 0) throw new ArgumentOutOfRangeException(nameof(block));
        if (length < 1) throw new ArgumentOutOfRangeException(nameof(length));
        // block * length is computed in long because a large block index would
        // wrap to a negative offset in int and then slip past the bounds check.
        long offset64 = (long)block * length;
        if (offset64 + length > BackingStore.Length)
            throw new ArgumentOutOfRangeException(nameof(length), "the read runs past the end of the device");
        int offset = (int)offset64;

        // Step 1: wait until the device is ready to receive a command.
        WaitForReady();
        // Step 2: the read needs no outgoing data, so the data register is left
        // alone; a write would happen here.
        // Step 3: issue the command, which starts the device.
        Command = DeviceCommand.Read;
        _pendingBlock = offset;
        _pendingLength = length;
        // Step 4: wait for it to finish.
        WaitForReady();

        var result = new byte[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = BackingStore[offset + i];
            // PIO: the CPU touches every byte itself.
            CpuCycles++;
        }
        return result;
    }

    /// <summary>
    /// The same protocol issued through explicit port-style instructions
    /// (OSEP §36.6) rather than memory-mapped register accesses.
    /// </summary>
    /// <remarks>
    /// §36.6: "There is not some great advantage to one approach or the
    /// other... but both approaches are still in use today." So this is
    /// deliberately the same code path with a different access spelling - the
    /// point the tests pin is that neither costs more than the other.
    /// </remarks>
    public byte[] CanonicalReadViaPorts(int block, int length)
    {
        // §36.6: "the in and out instructions can be used to communicate with
        // devices", and they "are usually privileged". The protocol they carry
        // is the same four-step one, so the only difference is how a register
        // is reached.
        // The same four steps §36.3 defines, with each register reached through a
        // port instead of a load/store. §36.6 is explicit that neither approach
        // has "some great advantage", so the cost must come out the same - which
        // also means the same bounds have to hold on both paths.
        if (block < 0) throw new ArgumentOutOfRangeException(nameof(block));
        if (length < 1) throw new ArgumentOutOfRangeException(nameof(length));
        // block * length is computed in long because a large block index would
        // wrap to a negative offset in int and then slip past the bounds check.
        long offset64 = (long)block * length;
        if (offset64 + length > BackingStore.Length)
            throw new ArgumentOutOfRangeException(nameof(length), "the read runs past the end of the device");
        int offset = (int)offset64;

        WaitForReady();
        WritePort(2, (byte)DeviceCommand.Read);
        _pendingBlock = offset;
        _pendingLength = length;
        WaitForReady();
        var result = new byte[length];
        for (int i = 0; i < length; i++) result[i] = ReadPort(1);
        return result;
    }

    /// <summary>Read a device register through a port, as an x86 <c>in</c> would.</summary>
    private byte ReadPort(int port)
    {
        CpuCycles++;
        return port switch
        {
            0 => (byte)_status,
            // The data register is what the OS reads results from; a read
            // command leaves the next byte of the read there.
            1 => NextDataByte(),
            _ => throw new ArgumentOutOfRangeException(nameof(port), "a canonical device has three registers"),
        };
    }

    private byte NextDataByte()
    {
        byte v = BackingStore[_pendingBlock + _dataCursor];
        _dataCursor++;
        return v;
    }

    /// <summary>Write a device register through a port, as an x86 <c>out</c> would.</summary>
    private void WritePort(int port, byte value)
    {
        CpuCycles++;
        switch (port)
        {
            case 1:
                _data = value;
                break;
            case 2:
                _command = (DeviceCommand)value;
                if (value != (byte)DeviceCommand.None)
                {
                    _status = DeviceStatus.Busy;
                    _remainingTicks = _latencyTicks;
                    _dataCursor = 0;
                    _commandOutstanding = true;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(port), "a canonical device has three registers");
        }
    }

    /// <summary>Poll the status register until the device is not BUSY (§36.3).</summary>
    private void WaitForReady()
    {
        while (Status == DeviceStatus.Busy)
        {
            CpuCycles++;   // the spin itself
            Tick();
        }
    }

    /// <summary>
    /// Program the DMA engine and let it move the bytes (§36.5).
    /// </summary>
    /// <param name="buffer">
    /// Host memory. A <c>byte[]</c> rather than an <c>IntPtr</c>: the chapter
    /// describes telling the engine "where the data lives in memory", and in a
    /// simulator the array *is* that memory. A pointer would carry no meaning
    /// here and would be unsafe.
    /// </param>
    /// <param name="fromDevice">
    /// True to DMA from the device into the buffer (§36.5's read), false to
    /// DMA the buffer out to the device (its write).
    /// </param>
    public void DmaTransferToDevice(byte[] buffer, bool fromDevice)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Length < 1) throw new ArgumentOutOfRangeException(nameof(buffer), "an empty transfer is not a transfer");
        // Truncating here would report a successful completion while silently
        // dropping write data or leaving stale bytes in a read buffer, and
        // nothing in the return path would say so.
        if (buffer.Length > BackingStore.Length)
            throw new ArgumentOutOfRangeException(nameof(buffer),
                $"a {buffer.Length}-byte transfer does not fit on a {BackingStore.Length}-byte device");

        WaitForReady();
        _dmaBuffer = buffer;
        _dmaFromDevice = fromDevice;
        // The OS tells the engine where the data is, how much, and which way;
        // after that it is done and the copy runs on the engine's own clock.
        CpuCycles += DeviceCostModel.ProgramChannelCycles;
        _status = DeviceStatus.Busy;
    }
}

/// <summary>
/// The cost of moving bytes by hand versus handing them to a DMA engine
/// (OSEP §36.3 and §36.5).
/// </summary>
/// <remarks>
/// The chapter gives no numbers - it argues that PIO leaves the CPU "overburdened
/// with a rather trivial task" and that DMA lets "the OS proceed with other work".
/// The units here are therefore abstract work items rather than cycles, and the
/// constants are declared as named properties so a reader can see exactly what
/// the comparison rests on and change any of them.
/// <para>
/// The one thing that is not arbitrary is the *shape*: PIO's cost grows with the
/// byte count because §36.5 says the CPU copies "one word at a time", while DMA's
/// does not, because §36.5 says the OS is "done with the transfer" after
/// programming the channel. Everything the model concludes follows from those two
/// statements.
/// </para>
/// </remarks>
public sealed class DeviceCostModel
{
    /// <summary>One status poll.</summary>
    public const int PollCycle = 1;

    /// <summary>One byte copied by the CPU itself (§36.5's "one word at a time").</summary>
    public const int PerByteCycle = 1;

    /// <summary>
    /// Writing source address, destination address and length into the channel -
    /// §36.5's "telling it where the data lives in memory, how much data to
    /// copy, and which device to send it to".
    /// </summary>
    public const int ProgramChannelCycles = 100;

    /// <summary>The interrupt handler that runs when the transfer completes (§36.4).</summary>
    public const int DefaultInterruptCost = 40;

    /// <summary>
    /// How long the hybrid strategy polls before giving up on polling
    /// (§36.4's "polls for a little while and then... uses interrupts").
    /// </summary>
    public const int DefaultPollThresholdTicks = 8;

    /// <summary>CPU cost of the §36.3 polling protocol for an n-byte transfer.</summary>
    /// <remarks>
    /// The arithmetic is done in double from the start. Evaluating
    /// <c>PollCycle + bytes * PerByteCycle</c> in int overflows for a large
    /// <c>bytes</c> and yields a negative cost, which is worse than useless in a
    /// report about which method is cheaper.
    /// </remarks>
    public double PioCycles(int bytes) => PollCycle + (double)bytes * PerByteCycle;

    /// <summary>CPU cost of a DMA transfer of any size: program, then handle the interrupt.</summary>
    public double DmaCycles(int bytes) => ProgramChannelCycles + DefaultInterruptCost;

    /// <summary>
    /// The smallest transfer where DMA costs strictly less than PIO: the point
    /// where the bytes PIO would copy by hand first exceed DMA's fixed setup.
    /// </summary>
    /// <remarks>
    /// <c>PioCycles(n) = PollCycle + n * PerByteCycle</c> and
    /// <c>DmaCycles = ProgramChannelCycles + DefaultInterruptCost</c>, so DMA
    /// wins when <c>n * PerByteCycle &gt; DmaCycles - PollCycle</c>. The one-byte
    /// adjustment is the status poll, which PIO pays before any data moves.
    /// </remarks>
    public int DmaCrossoverBytes => (ProgramChannelCycles + DefaultInterruptCost - PollCycle) / PerByteCycle + 1;

    /// <summary>§36.3's protocol against a device that finishes after N ticks.</summary>
    public double PollDriveCycles(int deviceLatencyTicks) => PollCycle + (double)deviceLatencyTicks * PollCycle;

    /// <summary>§36.4: issue, sleep, take the interrupt, run the handler.</summary>
    public double InterruptDriveCycles(int deviceLatencyTicks, int interruptCost) => 1 + interruptCost;

    /// <summary>
    /// §36.4's two-phased approach: poll for a bounded number of ticks, and only
    /// fall back to an interrupt if the device has not finished by then.
    /// </summary>
    public double HybridDriveCycles(int deviceLatencyTicks)
    {
        int polled = Math.Min(deviceLatencyTicks, DefaultPollThresholdTicks);
        // Each poll costs a read, plus a tick of latency if the device was busy.
        double cycles = 1 + polled * PollCycle;
        if (deviceLatencyTicks > DefaultPollThresholdTicks) cycles += DefaultInterruptCost;
        return cycles;
    }
}

/// <summary>
/// Formats the M34 comparisons for the <c>/device/run</c> route.
/// </summary>
public static class DeviceDemos
{
    public static string FormatCanonicalProtocol(int payloadSize, int latencyTicks)
    {
        var dev = new Device(payloadSize, latencyTicks);
        for (int i = 0; i < payloadSize; i++) dev.BackingStore[i] = (byte)('A' + (i % 26));

        var before = (Polls: dev.PollsExecuted, Cycles: dev.CpuCycles);
        var read = dev.CanonicalRead(block: 0, length: 16);
        var polls = dev.PollsExecuted - before.Polls;
        var cycles = dev.CpuCycles - before.Cycles;

        var sb = new StringBuilder();
        sb.AppendLine("=== Canonical device protocol (M34 / OSEP §36.2-§36.3) ===");
        sb.AppendLine($"device: {payloadSize} bytes, {latencyTicks} tick(s) of latency");
        sb.AppendLine();
        sb.AppendLine("step | what the OS does                        | status after");
        sb.AppendLine("-----|------------------------------------------|-------------");
        sb.AppendLine("  1  | poll STATUS until not BUSY               | Idle");
        sb.AppendLine("  2  | (no outgoing data on a read)             | Idle");
        sb.AppendLine("  3  | write the command to the COMMAND register | Busy");
        sb.AppendLine($"  4  | poll STATUS until done                   | {dev.Status}");
        sb.AppendLine();
        sb.AppendLine("A read needs no outgoing data, so step 2 is a no-op here; §36.3 puts it there for the");
        sb.AppendLine("write case. Once the device reports Complete the OS copies the results, and it is");
        sb.AppendLine("still doing that copying - which is what makes this programmed I/O.");
        sb.AppendLine();
        sb.AppendLine($"read 16 bytes: {string.Join(" ", read.Select(b => (char)b))}");
        sb.AppendLine($"status polls issued: {polls}   cpu cycles: {cycles}");
        sb.AppendLine();
        sb.AppendLine("§36.3, verbatim: \"While (STATUS == BUSY) ; ... Write data to DATA register /");
        sb.AppendLine("Write command to COMMAND register ... While (STATUS == BUSY) ;\". Four steps, and");
        sb.AppendLine("because the main CPU did the data movement, this is programmed I/O.");
        return sb.ToString();
    }

    public static string FormatPioVsDma(int transferBytes)
    {
        var cost = new DeviceCostModel();
        var sb = new StringBuilder();
        sb.AppendLine("=== PIO vs DMA (M34 / OSEP §36.3 + §36.5) ===");
        sb.AppendLine($"transfer: {transferBytes} bytes");
        sb.AppendLine();
        sb.AppendLine("method | cpu cycles | scales with size? | interrupts");
        sb.AppendLine("--------|------------|--------------------|-----------");
        sb.AppendLine($"PIO     | {cost.PioCycles(transferBytes),10} | yes ({DeviceCostModel.PerByteCycle}/byte) | 0");
        sb.AppendLine($"DMA     | {cost.DmaCycles(transferBytes),10} | no                 | 1");
        sb.AppendLine();
        sb.AppendLine($"DMA crossover: {cost.DmaCrossoverBytes} bytes. Below it, PIO is cheaper because the");
        sb.AppendLine("channel setup costs more than the bytes it would have saved.");
        sb.AppendLine();
        sb.AppendLine("Across sizes:");
        sb.AppendLine("bytes   | PIO     | DMA     | cheaper");
        foreach (int n in new[] { 1, 32, 128, cost.DmaCrossoverBytes - 1, cost.DmaCrossoverBytes, 1024, 4096, 1 << 20 })
        {
            double p = cost.PioCycles(n), d = cost.DmaCycles(n);
            sb.AppendLine($"{n,7} | {p,7} | {d,7} | {(d < p ? "DMA" : "PIO")}");
        }
        sb.AppendLine();
        sb.AppendLine("§36.5, verbatim: with PIO the CPU copies \"explicitly, one word at a time\"; with");
        sb.AppendLine("DMA \"the copying of data is now handled by the DMA controller\", and the OS is free");
        sb.AppendLine("to run another process meanwhile.");
        return sb.ToString();
    }

    public static string FormatInterruptVsPoll(int deviceLatencyTicks)
    {
        var cost = new DeviceCostModel();
        double poll = cost.PollDriveCycles(deviceLatencyTicks);
        double interrupt = cost.InterruptDriveCycles(deviceLatencyTicks, DeviceCostModel.DefaultInterruptCost);
        double hybrid = cost.HybridDriveCycles(deviceLatencyTicks);

        var sb = new StringBuilder();
        sb.AppendLine("=== Interrupt vs polling (M34 / OSEP §36.4) ===");
        sb.AppendLine($"device finishes after {deviceLatencyTicks} tick(s)");
        sb.AppendLine();
        sb.AppendLine("strategy | cpu cycles | interrupts | note");
        sb.AppendLine("----------|------------|------------|------");
        sb.AppendLine($"poll      | {poll,10:F0} | {0,10} | CPU spins in the polling loop");
        sb.AppendLine($"interrupt | {interrupt,10:F0} | {1,10} | CPU sleeps, then runs the handler");
        sb.AppendLine($"hybrid    | {hybrid,10:F0} | {(deviceLatencyTicks > DeviceCostModel.DefaultPollThresholdTicks ? 1 : 0),10} | polls {DeviceCostModel.DefaultPollThresholdTicks} ticks, then falls back");
        sb.AppendLine();
        sb.AppendLine($"cheapest here: {(poll <= interrupt && poll <= hybrid ? "poll" : interrupt <= hybrid ? "interrupt" : "hybrid")}");
        sb.AppendLine();
        sb.AppendLine("§36.4, verbatim: \"if a device is fast, it may be best to poll; if it is slow,");
        sb.AppendLine("interrupts, which allow overlap, are best.\" The crossover sits where polling");
        sb.AppendLine($"costs more than one interrupt, i.e. around {DeviceCostModel.DefaultInterruptCost} ticks of latency.");
        sb.AppendLine();
        sb.AppendLine("The TIP at the end of §36.4 makes the same point: \"Although interrupts allow for");
        sb.AppendLine("overlap of computation and I/O, they only really make sense for slow devices.\"");
        return sb.ToString();
    }
}