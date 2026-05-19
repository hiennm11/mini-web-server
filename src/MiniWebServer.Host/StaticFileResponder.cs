using System.Text;

public static class StaticFileResponder
{
    public static HttpResponse CreateResponse(HttpRequest request, string webRoot)
    {
        string root = Path.GetFullPath(webRoot);
        string path = request.Path.Split('?', 2)[0];
        string relativePath = path == "/" ? "index.html" : path.TrimStart('/');
        relativePath = Uri.UnescapeDataString(relativePath).Replace('/', Path.DirectorySeparatorChar);

        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
        {
            return Text(404, "Not Found", "Not Found");
        }

        byte[] body = File.ReadAllBytes(fullPath);
        return new HttpResponse(200, "OK", GetContentType(fullPath), body);
    }

    private static HttpResponse Text(int statusCode, string reasonPhrase, string body)
    {
        return new HttpResponse(
            statusCode,
            reasonPhrase,
            "text/plain; charset=UTF-8",
            Encoding.UTF8.GetBytes(body));
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=UTF-8",
            ".htm" => "text/html; charset=UTF-8",
            ".txt" => "text/plain; charset=UTF-8",
            ".css" => "text/css; charset=UTF-8",
            ".js" => "application/javascript; charset=UTF-8",
            _ => "application/octet-stream"
        };
    }
}
