public sealed record HttpRequest(
    string Method,
    string Path,
    string Version,
    IReadOnlyDictionary<string, string> Headers)
{
    public static HttpRequest Unknown { get; } = new(
        "UNKNOWN",
        "/",
        "HTTP/1.1",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
