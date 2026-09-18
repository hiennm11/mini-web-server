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

    /// <summary>Read a block of bytes from physical memory.</summary>
    public void ReadBytes(int pa, byte[] dest, int count)
    {
        if (pa < 0 || pa + count > _bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(pa));
        Array.Copy(_bytes, pa, dest, 0, count);
    }

    /// <summary>Write a block of bytes to physical memory.</summary>
    public void WriteBytes(int pa, byte[] src)
    {
        if (pa < 0 || pa + src.Length > _bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(pa));
        Array.Copy(src, 0, _bytes, pa, src.Length);
    }

    /// <summary>Total physical memory in bytes.</summary>
    public int SizeBytes => _bytes.Length;
}
