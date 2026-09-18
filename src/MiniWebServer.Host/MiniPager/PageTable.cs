using System.Text;

namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Per-process linear page table. Indexed by VPN, returns the PTE.
/// Slice 14.1: a flat array sized to <see cref="VirtualAddress.Vpn"/>'s
/// range. Sparse (we allocate the whole array up front even for
/// unmapped pages — this is the textbook trade-off that motivates
/// multi-level page tables in 14.3).
///
/// OSEP §18.5.
/// </summary>
public sealed class PageTable
{
    private readonly Pte[] _ptes;

    /// <summary>Process this page table belongs to (for trace labelling).</summary>
    public int Pid { get; }

    public PageTable(int pid, int numPages = 1 << 20)
    {
        Pid = pid;
        _ptes = new Pte[numPages];
    }

    public int Length => _ptes.Length;

    public Pte Get(int vpn)
    {
        if (vpn < 0 || vpn >= _ptes.Length)
            throw new ArgumentOutOfRangeException(nameof(vpn));
        return _ptes[vpn];
    }

    public void Set(int vpn, Pte pte)
    {
        if (vpn < 0 || vpn >= _ptes.Length)
            throw new ArgumentOutOfRangeException(nameof(vpn));
        _ptes[vpn] = pte;
    }

    /// <summary>How many PTEs are currently valid (mapped).</summary>
    public int MappedCount()
    {
        int n = 0;
        for (int i = 0; i < _ptes.Length; i++) if (_ptes[i].Valid) n++;
        return n;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PageTable(pid={Pid}, entries={_ptes.Length}, mapped={MappedCount()})");
        int shown = 0;
        for (int i = 0; i < _ptes.Length && shown < 8; i++)
        {
            if (_ptes[i].Valid)
            {
                sb.AppendLine($"  vpn {i,4} -> frame {_ptes[i].FrameNo}");
                shown++;
            }
        }
        return sb.ToString();
    }
}
