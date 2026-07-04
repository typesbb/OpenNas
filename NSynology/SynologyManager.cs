namespace NSynology;

public static class SynologyManager
{
    private static int _sessionRedirectActive;

    public static SynologyClient Client { get; private set; } = null!;

    /// <summary>由应用层注册：DSM 返回 106/107 时统一清会话并跳转登录。</summary>
    public static Func<SynologyApiException, Task>? SessionExpiredHandler { get; set; }

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

    public static bool ShouldSuppressApiErrorUi(Exception? ex) =>
        Volatile.Read(ref _sessionRedirectActive) == 1 || SynologyApiException.IsSessionExpired(ex);

    internal static void NotifySessionExpired(SynologyApiException ex)
    {
        Volatile.Write(ref _sessionRedirectActive, 1);
        var handler = SessionExpiredHandler;
        if (handler == null)
        {
            Volatile.Write(ref _sessionRedirectActive, 0);
            return;
        }

        _ = RunSessionExpiredHandlerAsync(handler, ex);
    }

    private static async Task RunSessionExpiredHandlerAsync(
        Func<SynologyApiException, Task> handler,
        SynologyApiException ex)
    {
        try
        {
            await handler(ex).ConfigureAwait(false);
        }
        catch
        {
            // 应用层 handler 负责日志
        }
        finally
        {
            Volatile.Write(ref _sessionRedirectActive, 0);
        }
    }
}
