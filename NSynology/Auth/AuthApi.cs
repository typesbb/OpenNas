using System.Text.Json;

namespace NSynology.Auth;

/// <summary>官方 Android App 启动时 compound 批量请求（抓包 SYNO.Entry.Request）。</summary>
internal static class AppCompoundRequests
{
    public const string BootstrapCompoundJson =
        "[{\"api\":\"SYNO.Foto.Setting.User\",\"method\":\"get\",\"version\":1}," +
        "{\"api\":\"SYNO.Foto.Setting.TeamSpace\",\"method\":\"get\",\"version\":1}," +
        "{\"api\":\"SYNO.Foto.UserInfo\",\"method\":\"me\",\"version\":1}," +
        "{\"api\":\"SYNO.Foto.Setting.Admin\",\"method\":\"get\",\"version\":1}]";
}

public class AuthApi(SynologyClient synologyClient) : ApiBase
{
    private readonly SynologyClient _client = synologyClient;

    public async Task<bool> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        _client.ClearHttpCookies();
        _client.SessionUsername = username;
        _client.SessionPassword = password;
        _client.CookieSessionSynoToken = null;

        if (!await TryLoginDsmAsync(username, password, cancellationToken))
            return false;

        await RefreshMainSynoTokenAsync(cancellationToken);
        _client.ApplyPhotosWebCookies();
        await _client.RefreshCookieSessionSynoTokenAsync(cancellationToken);

        return true;
    }

    private async Task RefreshMainSynoTokenAsync(CancellationToken cancellationToken)
    {
        var token = await _client.FetchSynoTokenAsync(
            SynologyClient.DsmWebApiEntry,
            _client.GetSessionIdCookieValue() ?? _client.Sid!,
            _client.SynoToken,
            cancellationToken);
        if (!string.IsNullOrEmpty(token))
            _client.SynoToken = token;
    }

    /// <summary>
    /// 与官方 Synology Photos Android App 一致：保留 <c>did</c>、加密登录，获取 <c>id</c>+<c>did</c> Cookie。
    /// </summary>
    public async Task<bool> LoginAppStyleAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        _client.SessionUsername = username;
        _client.SessionPassword = password;
        _client.CookieSessionSynoToken = null;

        var explicitDid = _client.PhotosDeviceId;
        var preservedDid = explicitDid ?? _client.LoadPersistedDeviceId();
        _client.ClearHttpCookiesPreservingDid(preservedDid);

        await TryAppApiInfoAsync(cancellationToken);

        // 官方 App 抓包：优先 POST entry.cgi + __cIpHeRtExT 加密登录，响应 data.did + data.sid。
        if (!await TryLoginAppEncryptedAsync(username, password, cancellationToken)
            && !await TryLoginAppPlainAsync(username, password, cancellationToken))
            return false;

        // 官方抓包：id/did 仅来自本次登录响应，不再额外刷新 SynoToken。
        await EnsureAppBootstrapAsync(cancellationToken);

        return true;
    }

    private async Task TryAppApiInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("api", "SYNO.API.Info"),
                new KeyValuePair<string, string>("method", "query"),
                new KeyValuePair<string, string>("version", "1"),
                new KeyValuePair<string, string>("query", "all")
            ]);
            using var request = new HttpRequestMessage(HttpMethod.Post, _client.BuildApiUri("webapi/query.cgi"))
            {
                Content = content
            };
            _client.ApplyAppApiHeaders(request);
            await _client.HttpClient.SendAsync(request, cancellationToken);
        }
        catch
        {
            // ignore
        }
    }

    private async Task<SynologyApiEncryption.EncryptionInfo?> FetchEncryptionInfoAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("api", "SYNO.API.Encryption"),
                new KeyValuePair<string, string>("method", "getinfo"),
                new KeyValuePair<string, string>("version", "1")
            ]);
            using var request = new HttpRequestMessage(HttpMethod.Post, _client.BuildApiUri(SynologyClient.DsmWebApiEntry))
            {
                Content = content
            };
            _client.ApplyAppApiHeaders(request);
            var response = await _client.HttpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("success", out var ok) || !ok.GetBoolean())
                return null;
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return null;

            return SynologyApiEncryption.ParseInfo(data);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> TryLoginAppEncryptedAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var encInfo = await FetchEncryptionInfoAsync(cancellationToken);
        if (encInfo == null)
            return false;

        // SAZ 1406：加密登录无 enable_syno_token；带上会导致后续 Cookie POST 全部 119。
        foreach (var version in new[] { 6, 7, 3 })
        {
            var fields = new Dictionary<string, string>
            {
                ["api"] = "SYNO.API.Auth",
                ["version"] = version.ToString(),
                ["method"] = "login",
                ["account"] = username,
                ["passwd"] = password,
                ["session"] = "SynologyPhotos",
                ["format"] = "cookie",
                ["stay_login"] = "yes"
            };

            var (cipherField, cipherPayload, clientTime) =
                SynologyApiEncryption.EncryptLoginFields(encInfo, fields);

            var auth = await _client.LoginRawPostAsync(
                SynologyClient.DsmWebApiEntry,
                [
                    new KeyValuePair<string, string>(cipherField, cipherPayload),
                    new KeyValuePair<string, string>("client_time", clientTime.ToString())
                ],
                useAppUserAgent: true,
                cancellationToken: cancellationToken);

            if (auth == null || string.IsNullOrEmpty(auth.Sid))
                continue;

            _client.ApplyAppAuthResult(auth);
            MarkAppEncryptedLogin();
            return true;
        }

        return false;
    }

    private async Task<bool> TryLoginAppPlainAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var preservedDid = _client.PhotosDeviceId ?? _client.GetDidCookieValue();

        foreach (var version in new[] { 6, 3 })
        {
            var url =
                $"{SynologyClient.DsmWebApiEntry}?api=SYNO.API.Auth&version={version}&method=login" +
                $"&session=SynologyPhotos" +
                $"&account={Uri.EscapeDataString(username)}" +
                $"&passwd={Uri.EscapeDataString(password)}" +
                "&format=cookie&stay_login=yes";

            var auth = await _client.LoginRawGetAsync(url, cancellationToken);
            if (auth == null || string.IsNullOrEmpty(auth.Sid))
                continue;

            _client.ApplyAppAuthResult(auth);
            if (string.IsNullOrEmpty(auth.Did) && !string.IsNullOrEmpty(preservedDid))
                _client.ApplyAppDeviceId(preservedDid);
            MarkAppPlainLogin();
            return true;
        }

        return false;
    }

    /// <summary>官方 App 登录后初始化（SAZ 1407+）。</summary>
    /// <summary>上次登录是否走加密 POST（SAZ 1406）。</summary>
    public bool LastLoginUsedEncryptedPost { get; private set; }

    public async Task EnsureAppBootstrapAsync(CancellationToken cancellationToken = default) =>
        await _client.RunAppPostLoginSequenceAsync(cancellationToken);

    internal void MarkAppEncryptedLogin() => LastLoginUsedEncryptedPost = true;

    internal void MarkAppPlainLogin() => LastLoginUsedEncryptedPost = false;

    /// <summary>
    /// 官方 Android App 上传前用 <c>session=SynologyPhotos</c> + <c>format=cookie</c> 刷新 <c>id</c>/<c>did</c> Cookie。
    /// </summary>
    public async Task EnsureAppCookieSessionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_client.SessionUsername) || string.IsNullOrEmpty(_client.SessionPassword))
            return;

        foreach (var version in new[] { 6, 3 })
        {
            var url =
                $"{SynologyClient.DsmWebApiEntry}?api=SYNO.API.Auth&version={version}&method=login" +
                $"&session=SynologyPhotos" +
                $"&account={Uri.EscapeDataString(_client.SessionUsername)}" +
                $"&passwd={Uri.EscapeDataString(_client.SessionPassword)}" +
                "&format=cookie&stay_login=yes";

            var auth = await _client.LoginRawGetAsync(url, cancellationToken);
            if (auth == null || string.IsNullOrEmpty(auth.Sid))
                continue;

            _client.ApplyAppAuthResult(auth);
            return;
        }
    }

    public async Task<bool> ValidateAsync(string sid, CancellationToken cancellationToken = default)
    {
        _client.RestoreAppSessionCookies(sid);
        var validity = await TryValidateAppSessionAsync(cancellationToken);
        return validity == true;
    }

    /// <summary>
    /// 用官方 Cookie 会话向 NAS 探测是否仍有效。
    /// <c>true</c> 有效，<c>false</c> 已失效，<c>null</c> 网络等原因暂无法判断。
    /// </summary>
    public async Task<bool?> TryValidateAppSessionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_client.Sid))
            return false;

        _client.RestorePersistedPhotosDeviceId();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(20));

            await _client.PostAppFormAsync(
                "SYNO.Foto.UserInfo", 1, "me", cancellationToken: cts.Token);
            return true;
        }
        catch (Exception ex) when (IsSessionRejected(ex))
        {
            return false;
        }
        catch (Exception ex) when (IsTransientFailure(ex))
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsSessionRejected(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("Session timeout", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Session interrupted", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Synology Photos API", StringComparison.OrdinalIgnoreCase)
               || message.Contains("会话", StringComparison.OrdinalIgnoreCase)
               || message.Contains("permission", StringComparison.OrdinalIgnoreCase)
               || message.Contains("权限", StringComparison.OrdinalIgnoreCase)
               || message.Contains("privilege", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or TimeoutException or IOException
        || (ex.InnerException != null && IsTransientFailure(ex.InnerException));

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_client.Sid))
            return;

        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.API.Auth&version=1&method=logout&{{0}}";
        await _client.GetAsync<AuthResponse>(url, cancellationToken);

        _client.ClearHttpCookies();
        _client.Sid = null;
        _client.SynoToken = null;
        _client.CookieSessionSynoToken = null;
        _client.FileStationSid = null;
        _client.FileStationSynoToken = null;
        _client.SessionUsername = null;
        _client.SessionPassword = null;
    }

    private async Task<bool> TryLoginDsmAsync(string username, string password, CancellationToken cancellationToken)
    {
        foreach (var entry in new[] { SynologyClient.DsmWebApiEntry, SynologyClient.PhotoWebApiEntry })
        {
            foreach (var session in new string?[] { null, "SynologyPhotos" })
            {
                foreach (var version in new[] { 6, 3 })
                {
                    var sessionPart = string.IsNullOrEmpty(session)
                        ? ""
                        : $"&session={Uri.EscapeDataString(session)}";
                    var url =
                        $"{entry}?api=SYNO.API.Auth&version={version}&method=login" +
                        $"{sessionPart}&account={Uri.EscapeDataString(username)}&passwd={Uri.EscapeDataString(password)}&enable_syno_token=yes&format=cookie&stay_login=yes";

                    var auth = await _client.LoginRawGetAsync(url, cancellationToken);
                    if (auth == null || string.IsNullOrEmpty(auth.Sid))
                        continue;

                    _client.Sid = auth.Sid;
                    _client.SynoToken = auth.SynoToken;
                    return true;
                }
            }
        }

        return false;
    }

    private async Task TryEstablishFileStationSessionAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        _client.FileStationSid = null;
        foreach (var version in new[] { 6, 3 })
        {
            var url =
                $"{SynologyClient.DsmWebApiEntry}?api=SYNO.API.Auth&version={version}&method=login" +
                $"&session=FileStation&account={Uri.EscapeDataString(username)}&passwd={Uri.EscapeDataString(password)}" +
                "&enable_syno_token=yes&format=sid";

            var auth = await _client.LoginRawGetAsync(url, cancellationToken);
            if (auth == null || string.IsNullOrEmpty(auth.Sid))
                continue;

            _client.FileStationSid = auth.Sid;
            var token = await _client.FetchSynoTokenAsync(
                SynologyClient.DsmWebApiEntry,
                auth.Sid,
                auth.SynoToken ?? _client.SynoToken,
                cancellationToken);
            if (!string.IsNullOrEmpty(token))
                _client.FileStationSynoToken = token;
            return;
        }
    }
}
