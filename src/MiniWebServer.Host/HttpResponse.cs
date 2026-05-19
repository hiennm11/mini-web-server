using System.Text;

public sealed record HttpResponse(
    int StatusCode,
    string ReasonPhrase,
    string ContentType,
    byte[] Body)
{
    public byte[] ToBytes()
    {
        string headers =
            $"HTTP/1.1 {StatusCode} {ReasonPhrase}\r\n" +
            $"Content-Type: {ContentType}\r\n" +
            $"Content-Length: {Body.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
        byte[] responseBytes = new byte[headerBytes.Length + Body.Length];

        Buffer.BlockCopy(headerBytes, 0, responseBytes, 0, headerBytes.Length);
        Buffer.BlockCopy(Body, 0, responseBytes, headerBytes.Length, Body.Length);

        return responseBytes;
    }
}
