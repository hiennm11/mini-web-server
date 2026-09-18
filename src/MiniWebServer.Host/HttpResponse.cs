using System.Text;

public sealed record HttpResponse(
    int StatusCode,
    string ReasonPhrase,
    string ContentType,
    byte[] Body)
{
    /// <summary>Calculate the total byte length the response will occupy when serialized.</summary>
    public int TotalLength
    {
        get
        {
            int headerLen = StatusCode.ToString().Length + ReasonPhrase.Length
                + ContentType.Length + Body.Length.ToString().Length + 64;
            return headerLen + Body.Length;
        }
    }

    public byte[] ToBytes()
    {
        byte[] responseBytes = new byte[TotalLength];
        int written = WriteTo(responseBytes);
        return responseBytes.AsSpan(0, written).ToArray();
    }

    /// <summary>
    /// Serialize the response into <paramref name="dest"/> starting at
    /// offset 0. Returns the number of bytes written. Caller is
    /// responsible for ensuring <paramref name="dest"/> has at least
    /// <see cref="TotalLength"/> bytes of capacity.
    /// </summary>
    public int WriteTo(byte[] dest)
    {
        string headers =
            $"HTTP/1.1 {StatusCode} {ReasonPhrase}\r\n" +
            $"Content-Type: {ContentType}\r\n" +
            $"Content-Length: {Body.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
        Buffer.BlockCopy(headerBytes, 0, dest, 0, headerBytes.Length);
        Buffer.BlockCopy(Body, 0, dest, headerBytes.Length, Body.Length);
        return headerBytes.Length + Body.Length;
    }
}

