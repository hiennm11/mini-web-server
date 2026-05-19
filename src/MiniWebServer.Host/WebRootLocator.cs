public static class WebRootLocator
{
    public static string GetWebRoot(string appBaseDirectory)
    {
        return Path.GetFullPath(Path.Combine(appBaseDirectory, "wwwroot"));
    }
}
