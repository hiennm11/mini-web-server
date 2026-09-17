namespace MiniWebServer.Host;

/// <summary>
/// Constants shared between the worker-pool server (Program.cs) and
/// the async / event-based server (AsyncServer.cs). Lives in its own
/// type because top-level statements cannot declare public members.
/// </summary>
public static class ServerConfig
{
    public const int MaxRequestBytes = 1_024 * 1_024;
    public const int RaceIterations = 1_000_000;
}