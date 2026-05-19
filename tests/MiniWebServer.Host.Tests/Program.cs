using System;

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

Console.WriteLine("All parser tests passed.");

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
