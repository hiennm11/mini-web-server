namespace MiniWebServer.Host.MiniPager;

/// <summary>
/// Physical memory as a flat byte array of <see cref="NumFrames"/>
/// pages of <see cref="VirtualAddress.PAGE_SIZE"/> bytes. OSEP §18.4
/// "Memory Array".
/// </summary>
public sealed class PhysicalMemory
{
    public int NumFrames { get; }
    public int FrameSize => VirtualAddress.PAGE_SIZE;

    private readonly byte[] _bytes;

    public PhysicalMemory(int numFrames)
    {
        if (numFrames < 1) throw new ArgumentException("numFrames >= 1", nameof(numFrames));
        NumFrames = numFrames;
        _bytes = new byte[(long)numFrames * FrameSize];
    }

    /// <summary>Zero out a frame (used when a new page is loaded into it).</summary>
    public void ZeroFrame(int frameNo)
    {
        if (frameNo < 0 || frameNo >= NumFrames)
            throw new ArgumentOutOfRangeException(nameof(frameNo));
        Array.Clear(_bytes, frameNo * FrameSize, FrameSize);
    }

    /// <summary>Read a single byte at a physical address.</summary>
    public byte ReadByte(int pa)
    {
        if (pa < 0 || pa >= _bytes.Length) throw new ArgumentOutOfRangeException(nameof(pa));
        return _bytes[pa];
    }

    /// <summary>Write a single byte at a physical address.</summary>
    public void WriteByte(int pa, byte value)
    {
        if (pa < 0 || pa >= _bytes.Length) throw new ArgumentOutOfRangeException(nameof(pa));
        _bytes[pa] = value;
    }

    /// <summary>Total physical memory in bytes.</summary>
    public int SizeBytes => _bytes.Length;
}
