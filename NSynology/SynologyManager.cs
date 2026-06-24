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

    /// <summary>当前 Client 已初始化且使用相同的 BaseUrl。</summary>
    public static bool IsInitializedFor(string baseUrl)
    {
        return Client != null
            && string.Equals(Client.BaseUrl, baseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }
}
