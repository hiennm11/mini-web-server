using System;
using System.Text;

Run("parses request line", () =>
{
    HttpRequest request = HttpRequestParser.Parse(
        "GET /ostep HTTP/1.1\r\n" +
        "Host: localhost:8080\r\n" +
        "\r\n");

    AssertEqual("GET", request.Method);
    AssertEqual("/ostep", request.Path);
    AssertEqual("HTTP/1.1", request.Version);
});

Run("parses headers", () =>
{
    HttpRequest request = HttpRequestParser.Parse(
        "GET / HTTP/1.1\r\n" +
        "Host: localhost:8080\r\n" +
        "User-Agent: curl/8.0\r\n" +
        "\r\n");

    AssertEqual("localhost:8080", request.Headers["Host"]);
    AssertEqual("curl/8.0", request.Headers["User-Agent"]);
});

Run("invalid request becomes unknown request", () =>
{
    HttpRequest request = HttpRequestParser.Parse("");

    AssertEqual("UNKNOWN", request.Method);
    AssertEqual("/", request.Path);
    AssertEqual("HTTP/1.1", request.Version);
    AssertEqual(0, request.Headers.Count);
});

Run("serves index html for root path", () =>
{
    string webRoot = CreateTempWebRoot();
    File.WriteAllText(Path.Combine(webRoot, "index.html"), "<h1>Home</h1>");

    HttpResponse response = StaticFileResponder.CreateResponse(
        new HttpRequest("GET", "/", "HTTP/1.1", new Dictionary<string, string>()),
        webRoot);

    AssertEqual(200, response.StatusCode);
    AssertEqual("text/html; charset=UTF-8", response.ContentType);
    AssertEqual("<h1>Home</h1>", Encoding.UTF8.GetString(response.Body));
});

Run("missing file returns 404", () =>
{
    string webRoot = CreateTempWebRoot();

    HttpResponse response = StaticFileResponder.CreateResponse(
        new HttpRequest("GET", "/missing.txt", "HTTP/1.1", new Dictionary<string, string>()),
        webRoot);

    AssertEqual(404, response.StatusCode);
    AssertEqual("Not Found", response.ReasonPhrase);
});

Run("path traversal returns 404", () =>
{
    string webRoot = CreateTempWebRoot();
    string outsideFile = Path.Combine(Directory.GetParent(webRoot)!.FullName, "secret.txt");
    File.WriteAllText(outsideFile, "secret");

    HttpResponse response = StaticFileResponder.CreateResponse(
        new HttpRequest("GET", "/../secret.txt", "HTTP/1.1", new Dictionary<string, string>()),
        webRoot);

    AssertEqual(404, response.StatusCode);
});

Run("web root resolves from app base directory", () =>
{
    string appBase = Path.Combine(Path.GetTempPath(), "mini-web-server-tests", Guid.NewGuid().ToString("N"));

    string webRoot = WebRootLocator.GetWebRoot(appBase);

    AssertEqual(Path.GetFullPath(Path.Combine(appBase, "wwwroot")), webRoot);
});

Run("parses /slow path", () =>
{
    HttpRequest request = HttpRequestParser.Parse(
        "GET /slow HTTP/1.1\r\n" +
        "Host: localhost:8080\r\n" +
        "\r\n");

    AssertEqual("GET", request.Method);
    AssertEqual("/slow", request.Path);
    AssertEqual("HTTP/1.1", request.Version);
});

Console.WriteLine("All tests passed.");

static string CreateTempWebRoot()
{
    string path = Path.Combine(Path.GetTempPath(), "mini-web-server-tests", Guid.NewGuid().ToString("N"), "wwwroot");
    Directory.CreateDirectory(path);
    return path;
}

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.Exit(1);
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}");
    }
}
