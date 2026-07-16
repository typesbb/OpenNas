namespace NSynology;

public static class SynologyManager
{
    private static int _sessionRedirectActive;
    private static int _addressSwitchActive;
    private static long _addressSwitchSuppressUntilTicks;

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

    /// <summary>地址切换开始：抑制进行中请求因 HttpClient 重建产生的错误弹窗。</summary>
    public static void BeginAddressSwitch() =>
        Volatile.Write(ref _addressSwitchActive, 1);

    /// <summary>地址切换结束，并在 <paramref name="graceAfter"/> 内继续抑制误报弹窗。</summary>
    public static void EndAddressSwitch(TimeSpan graceAfter)
    {
        Volatile.Write(ref _addressSwitchActive, 0);
        Interlocked.Exchange(ref _addressSwitchSuppressUntilTicks, DateTime.UtcNow.Add(graceAfter).Ticks);
    }

    /// <summary>是否处于地址切换或切换宽限期内（业务层可用此避免弹超时/网络错）。</summary>
    public static bool IsAddressSwitchErrorSuppressed =>
        Volatile.Read(ref _addressSwitchActive) == 1
        || DateTime.UtcNow.Ticks < Interlocked.Read(ref _addressSwitchSuppressUntilTicks);

    public static bool ShouldSuppressApiErrorUi(Exception? ex) =>
        Volatile.Read(ref _sessionRedirectActive) == 1
        || IsAddressSwitchErrorSuppressed
        || ex is ObjectDisposedException
        || SynologyApiException.IsSessionExpired(ex);

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
