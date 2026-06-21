namespace NSynology;

public static class SynologyManager
{
    public static SynologyClient Client { get; private set; } = null!;

    public static void Init(string baseUrl)
    {
        Client?.HttpClient?.Dispose();
        Client = new SynologyClient(baseUrl);
    }

    public static void Init(string baseUrl, string sid, string? synoToken = null)
    {
        Client?.HttpClient?.Dispose();
        Client = new SynologyClient(baseUrl, sid) { SynoToken = synoToken };
    }
}
