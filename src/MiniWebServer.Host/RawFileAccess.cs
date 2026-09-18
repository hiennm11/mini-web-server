using System.Text;

/// <summary>
/// Demonstrates the raw open/read/close syscall path that FileStream
/// uses internally. Each FileStream operation is logged so the kernel
/// boundary is observable. OSEP Ch.39 file descriptor / open-read-close
/// pattern.
/// </summary>
public static class RawFileAccess
{
    /// <summary>
    /// Reads the file at <paramref name="path"/> using a single FileStream
    /// open + a loop of 4 KB blocking reads + close on Dispose. Each
    /// syscall is logged to the console.
    /// </summary>
    public static byte[] ReadAllBytesRaw(string path)
    {
        Console.WriteLine($"[syscall] open(path={path})");
        using var fs = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.None,
            });
        Console.WriteLine($"[syscall] open returned handle={fs.SafeFileHandle}");

        var buffer = new byte[4096];
        var ms = new MemoryStream();
        int totalReads = 0;
        long totalBytes = 0;
        int n;
        while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            long offsetBefore = fs.Position - n;
            Console.WriteLine(
                $"[syscall] read(handle={fs.SafeFileHandle}, requested={buffer.Length}, returned={n}, offset={offsetBefore})");
            ms.Write(buffer, 0, n);
            totalReads++;
            totalBytes += n;
        }
        Console.WriteLine(
            $"[syscall] close(handle={fs.SafeFileHandle}) [total_reads={totalReads}, total_bytes={totalBytes}]");
        return ms.ToArray();
    }
}