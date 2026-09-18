using System.Text;

public sealed record HttpResponse(
    int StatusCode,
    string ReasonPhrase,
    string ContentType,
    byte[] Body)
{
    /// <summary>Calculate the exact byte length the response will occupy when serialized.</summary>
    public int TotalLength
    {
        get
        {
            // Fixed header overhead (literal strings):
            //   "HTTP/1.1 " + StatusCode + " " + ReasonPhrase + "\r\n"   -> 9 + 3 + 1 + RP + 2
            //   "Content-Type: " + ContentType + "\r\n"                 -> 14 + CT + 2
            //   "Content-Length: " + Body.Length + "\r\n"               -> 17 + BL + 2
            //   "Connection: close\r\n"                                  -> 19
            //   "\r\n"                                                   -> 2
            //   total constants: 9 + 3 + 1 + 2 + 14 + 2 + 17 + 2 + 19 + 2 = 71
            const int headerConstants = 71;
            int headerLen = headerConstants
                + ReasonPhrase.Length
                + ContentType.Length
                + Body.Length.ToString().Length;
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

