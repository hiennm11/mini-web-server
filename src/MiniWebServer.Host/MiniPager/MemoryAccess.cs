namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// One memory access in a workload. Slice 14.1: just a virtual
/// address and a kind (read or write). Later slices add process
/// id (for multi-process traces) and access time.
/// </summary>
public readonly record struct MemoryAccess(int Pid, int VirtualAddressValue, AccessKind Kind)
{
    public VirtualAddress Va => new(VirtualAddressValue);
}

public enum AccessKind { Read, Write }

/// <summary>
/// Outcome of a single translation attempt. OSEP §18.7.
///   Hit: VA -> PA succeeded (page was in the page table).
///   PageFault: VA's VPN had no valid PTE (page is not in memory).
///     In slice 14.1 there's no swap, so this is a fatal fault.
///   OutOfRange: VA's VPN is outside the configured address space.
/// </summary>
public enum TranslateOutcome { Hit, PageFault, OutOfRange }

/// <summary>
/// One event in a translation trace.
/// </summary>
public sealed record TraceEvent(
    int Step,
    int Pid,
    string VaStr,
    string? PaStr,
    TranslateOutcome Outcome,
    string? Detail)
{
    public string Format()
    {
        string head = $"step={Step,3} pid={Pid} va={VaStr,-13}";
        if (PaStr is not null) head += $" -> pa={PaStr,-13}";
        else head += $"                ";
        head += $" {Outcome,-10}";
        if (!string.IsNullOrEmpty(Detail)) head += $"  {Detail}";
        return head;
    }
}
