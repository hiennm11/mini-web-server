using System.Text;

/// <summary>
/// Pure helpers for deciding when an accumulating byte buffer contains a complete
/// HTTP request. The socket loop in <c>Program.ReceiveRequest</c> drives the
/// buffer; this class only answers "do we have enough yet?" so the loop and the
/// decision can be tested independently.
/// </summary>
public static class HttpRequestReceiver
{
    public static readonly byte[] HeaderDelimiter = { 0x0D, 0x0A, 0x0D, 0x0A };

    /// <summary>
    /// Returns the byte index where the header terminator "\r\n\r\n" starts,
    /// or -1 if the buffer does not yet contain it.
    /// </summary>
    public static int FindHeaderEnd(ReadOnlySpan<byte> buffer)
    {
        return IndexOf(buffer, HeaderDelimiter);
    }

    /// <summary>
    /// Parses the integer value of the Content-Length header from the bytes
    /// before the header terminator. Returns 0 when the header is missing or
    /// malformed (so the caller treats the request as headers-only).
    /// </summary>
    public static int ParseContentLength(ReadOnlySpan<byte> headerBlock)
    {
        ReadOnlySpan<byte> needle = "Content-Length:"u8;
        int idx = IndexOf(headerBlock, needle);
        if (idx < 0)
        {
            return 0;
        }

        int valueStart = idx + needle.Length;
        while (valueStart < headerBlock.Length && (headerBlock[valueStart] == ' ' || headerBlock[valueStart] == '\t'))
        {
            valueStart++;
        }

        int valueEnd = valueStart;
        while (valueEnd < headerBlock.Length && headerBlock[valueEnd] >= '0' && headerBlock[valueEnd] <= '9')
        {
            valueEnd++;
        }

        if (valueEnd == valueStart)
        {
            return 0;
        }

        var digits = headerBlock.Slice(valueStart, valueEnd - valueStart);
        int result = 0;
        for (int i = 0; i < digits.Length; i++)
        {
            result = (result * 10) + (digits[i] - '0');
        }
        return result;
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty)
        {
            return 0;
        }

        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return i;
            }
        }
        return -1;
    }
}