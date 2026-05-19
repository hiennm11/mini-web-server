public static class HttpRequestParser
{
    public static HttpRequest Parse(string rawRequest)
    {
        if (string.IsNullOrWhiteSpace(rawRequest))
        {
            return HttpRequest.Unknown;
        }

        string[] lines = rawRequest.Replace("\r\n", "\n").Split('\n');
        string[] requestLineParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (requestLineParts.Length != 3)
        {
            return HttpRequest.Unknown;
        }

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.Length == 0)
            {
                break;
            }

            int separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string name = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();
            headers[name] = value;
        }

        return new HttpRequest(
            requestLineParts[0],
            requestLineParts[1],
            requestLineParts[2],
            headers);
    }
}
