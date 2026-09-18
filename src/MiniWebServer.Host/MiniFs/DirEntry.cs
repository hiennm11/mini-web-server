using static MiniWebServer.Host.MiniFs.Constants;

namespace MiniWebServer.Host.MiniFs;

/// <summary>
/// Directory entry. Fixed-size 32 bytes: ushort ino (2) +
/// 30 bytes name. OSEP §40.7 directory as a file of dir entries.
/// ino == 0 means the slot is free.
/// </summary>
public sealed class DirEntry
{
    public ushort Ino;
    public string Name = "";  // up to 30 chars

    public const int RECORD_SIZE = 32;
    public const int MAX_NAME = 30;

    public byte[] ToBytes()
    {
        var bytes = new byte[RECORD_SIZE];
        BitConverter.GetBytes(Ino).CopyTo(bytes, 0);
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(Name);
        if (nameBytes.Length > MAX_NAME)
            throw new ArgumentException($"name too long: {Name} ({nameBytes.Length} > {MAX_NAME})");
        Array.Copy(nameBytes, 0, bytes, 2, nameBytes.Length);
        return bytes;
    }

    public static DirEntry FromBytes(byte[] bytes, int off)
    {
        var de = new DirEntry
        {
            Ino = BitConverter.ToUInt16(bytes, off),
            Name = System.Text.Encoding.UTF8.GetString(bytes, off + 2, MAX_NAME).TrimEnd('\0'),
        };
        return de;
    }
}