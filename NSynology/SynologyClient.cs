using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSynology.Auth;
using NSynology.Diagnostics;
using NSynology.FileStation;
using NSynology.Foto;

namespace NSynology;

public class SynologyClient
{
    public const string DsmWebApiEntry = "webapi/entry.cgi";
    public const string PhotoWebApiEntry = "photo/webapi/entry.cgi";

    public string BaseUrl { get; }
    private readonly CookieContainer _cookieContainer;
    internal HttpClient HttpClient { get; private set; }
    public string? Sid { get; set; }
    public string? SynoToken { get; set; }

    /// <summary>File Station 专用 sid（<c>session=FileStation</c>）。</summary>
    public string? FileStationSid { get; set; }
    public string? FileStationSynoToken { get; set; }

    internal string? SessionUsername { get; set; }
    internal string? SessionPassword { get; set; }

    /// <summary>官方 App 设备 <c>did</c>（可在外部设置后持久化）。</summary>
    public string? PhotosDeviceId { get; set; }

    /// <summary>与 Cookie <c>id</c> 匹配的 CSRF token（Browse API 用）。</summary>
    internal string? CookieSessionSynoToken { get; set; }

    private readonly HashSet<int> _warmedAppAlbumIds = new();
    private readonly object _warmupLock = new();

    public AuthApi Auth { get; set; }
    public FotoApi Foto { get; set; }
    public FileStationApi FileStation { get; set; }

    public SynologyClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _cookieContainer = new CookieContainer();
        HttpClient = CreateHttpClient();
        ServicePointManager.FindServicePoint(new Uri(BaseUrl)).ConnectionLimit = 512;
        Auth = new AuthApi(this);
        Foto = new FotoApi(this);
        FileStation = new FileStationApi(this);
    }

    public SynologyClient(string baseUrl, string sid) : this(baseUrl) => Sid = sid;

    /// <summary>供测试注入 <see cref="HttpMessageHandler"/>（不经过 CookieContainer）。</summary>
    internal SynologyClient(string baseUrl, HttpMessageHandler handler)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _cookieContainer = new CookieContainer();
        HttpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri(BaseUrl + "/"),
            Timeout = TimeSpan.FromMinutes(20)
        };
        ServicePointManager.FindServicePoint(new Uri(BaseUrl)).ConnectionLimit = 512;
        Auth = new AuthApi(this);
        Foto = new FotoApi(this);
        FileStation = new FileStationApi(this);
    }

    internal void ClearHttpCookies()
    {
        var baseUri = new Uri(BaseUrl);
        foreach (Cookie cookie in _cookieContainer.GetCookies(baseUri))
            cookie.Expired = true;
    }

    /// <summary>官方 App 设备 <c>did</c> 应跨登录保留（max-age=31536000）。</summary>
    internal void ClearHttpCookiesPreservingDid(string? preservedDid = null)
    {
        preservedDid ??= PhotosDeviceId ?? GetDidCookieValue();
        ClearHttpCookies();
        if (!string.IsNullOrEmpty(preservedDid))
            ApplyAppDeviceId(preservedDid);
    }

    internal void ApplyAppDeviceId(string deviceId)
    {
        PhotosDeviceId = deviceId;
        AddCookie(new Uri(BaseUrl), "did", deviceId);
    }

    internal string GetDeviceIdPersistencePath()
    {
        var host = new Uri(BaseUrl).Host.Replace(':', '_');
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenNas");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"photos-did-{host}.txt");
    }

    internal string? LoadPersistedDeviceId()
    {
        try
        {
            var path = GetDeviceIdPersistencePath();
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    internal void SavePersistedDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return;
        try
        {
            File.WriteAllText(GetDeviceIdPersistencePath(), deviceId.Trim());
            PhotosDeviceId = deviceId.Trim();
        }
        catch
        {
            // ignore
        }
    }

    public void ConfigureHttpTrace(bool enabled, Action<string>? logger = null)
    {
        if (enabled)
            SynologyHttpTrace.Enable(logger);
        else
            SynologyHttpTrace.Disable();
        RecreateHttpClient();
    }

    public void RecreateHttpClient()
    {
        HttpClient.Dispose();
        HttpClient = CreateHttpClient();
    }

    internal Task<HttpResponseMessage> SendAppRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        if (request.RequestUri == null || !request.RequestUri.IsAbsoluteUri)
            request.RequestUri = BuildApiUri(request.RequestUri?.ToString() ?? DsmWebApiEntry);
        return HttpClient.SendAsync(request, cancellationToken);
    }

    private HttpClient CreateHttpClient()
    {
        // Android 默认 HttpClientHandler 会把整块 multipart 读入内存（>256MB 堆上限必 OOM）。
        // SocketsHttpHandler 才会按 HttpContent.SerializeToStreamAsync 逐块流式发送。
        // Cookie 仍仅通过显式请求头 did+id 发送（SAZ 无 _sid）；UseCookies=false 保留 Set-Cookie 供解析。
        HttpMessageHandler pipeline = new SocketsHttpHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 1,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = static (_, _, _, _) => true
            }
        };

        if (SynologyHttpTrace.IsEnabled)
            pipeline = new SynologyHttpTraceHandler(pipeline);

        return new HttpClient(pipeline, disposeHandler: true)
        {
            BaseAddress = new Uri(BaseUrl + "/"),
            Timeout = TimeSpan.FromMinutes(20)
        };
    }

    internal Uri BuildApiUri(string entryQuery) =>
        new Uri($"{BaseUrl}/{entryQuery.TrimStart('/')}");

    internal async Task<AuthResponse?> LoginRawGetAsync(string url, CancellationToken cancellationToken = default)
    {
        var response = await HttpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        RefreshAppCookiesFromResponse(response);
        return ParseAuthResponse(body, response);
    }

    internal async Task<AuthResponse?> LoginRawPostAsync(
        string entryPath,
        IEnumerable<KeyValuePair<string, string>> formFields,
        bool useAppUserAgent = false,
        CancellationToken cancellationToken = default)
    {
        using var content = CreateAppFormContent(formFields);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUri(entryPath)) { Content = content };
        if (useAppUserAgent)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", AppPhotosAndroidUserAgent);
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
            var didCookie = BuildAppCookieHeader();
            if (!string.IsNullOrEmpty(didCookie))
                request.Headers.TryAddWithoutValidation("Cookie", didCookie);
        }

        var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        RefreshAppCookiesFromResponse(response);
        return ParseAuthResponse(body, response);
    }

    internal static AuthResponse? ParseAuthResponse(string body, HttpResponseMessage? response = null)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("success", out var ok) || !ok.GetBoolean())
            return null;
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return null;

        var sid = data.TryGetProperty("sid", out var sidEl) ? sidEl.GetString() : null;
        if (string.IsNullOrEmpty(sid))
            return null;

        var did = data.TryGetProperty("did", out var didEl) ? didEl.GetString() : null;

        string? token = null;
        if (data.TryGetProperty("synotoken", out var tokenEl))
            token = tokenEl.GetString();
        else if (data.TryGetProperty("SynoToken", out var tokenEl2))
            token = tokenEl2.GetString();

        if (string.IsNullOrEmpty(token) && response != null
            && response.Headers.TryGetValues("X-SYNO-TOKEN", out var headerValues))
            token = headerValues.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        TryParseSetCookieAuth(response, ref sid, ref did);

        return new AuthResponse { Sid = sid, Did = did ?? "", SynoToken = token };
    }

    /// <summary>从登录响应 <c>Set-Cookie</c> 补充 <c>id</c>/<c>did</c>（与 JSON <c>data.sid</c>/<c>data.did</c> 一致）。</summary>
    private static void TryParseSetCookieAuth(
        HttpResponseMessage? response,
        ref string? sid,
        ref string? did)
    {
        if (response == null)
            return;

        IEnumerable<string> setCookies;
        if (response.Headers.TryGetValues("Set-Cookie", out var headerValues))
            setCookies = headerValues;
        else if (response.Headers.NonValidated.TryGetValues("Set-Cookie", out var nv))
            setCookies = nv;
        else
            return;

        foreach (var header in setCookies)
        {
            var part = header.Split(';', 2)[0];
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            var name = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (name.Equals("id", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                sid = value;
            else if (name.Equals("did", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                did = value;
        }
    }

    /// <summary>
    /// 官方 App 加密登录响应：<c>data.sid</c> 即 Cookie <c>id</c>，<c>data.did</c> 即 Cookie <c>did</c>。
    /// </summary>
    internal void ApplyAppAuthResult(AuthResponse auth)
    {
        if (!string.IsNullOrEmpty(auth.Sid))
            Sid = auth.Sid;
        // 官方 App Cookie 会话不依赖 SynoToken（SAZ 1406 登录响应无 synotoken）。
        SynoToken = null;
        CookieSessionSynoToken = null;

        var uri = new Uri(BaseUrl);
        if (!string.IsNullOrEmpty(auth.Sid))
            SetCookie(uri, "id", auth.Sid);

        if (!string.IsNullOrEmpty(auth.Did))
        {
            ApplyAppDeviceId(auth.Did);
            SavePersistedDeviceId(auth.Did);
        }
        else
        {
            var cookieDid = GetDidCookieValue();
            if (!string.IsNullOrEmpty(cookieDid))
                SavePersistedDeviceId(cookieDid);
        }
    }

    /// <summary>
    /// 恢复已保存会话：本地 sid 即官方 App 的 Cookie <c>id</c>，并恢复持久化的 <c>did</c>。
    /// </summary>
    public void RestoreAppSessionCookies(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        Sid = sessionId;
        SetCookie(new Uri(BaseUrl), "id", sessionId);
        RestorePersistedPhotosDeviceId();
    }

    /// <summary>恢复上次 NAS 登录下发的 <c>did</c>（非 AndroidId）。</summary>
    public void RestorePersistedPhotosDeviceId()
    {
        var did = LoadPersistedDeviceId();
        if (!string.IsNullOrWhiteSpace(did))
            ApplyAppDeviceId(did);
    }

    private void SetCookie(Uri uri, string name, string value)
    {
        var existing = _cookieContainer.GetCookies(uri)[name];
        if (existing != null)
            existing.Expired = true;
        AddCookie(uri, name, value);
    }

    internal async Task<string?> FetchSynoTokenAsync(
        string entryPath,
        string sid,
        string? existingToken,
        CancellationToken cancellationToken = default)
    {
        var url = $"{entryPath}?api=SYNO.API.Auth&version=6&method=token&_sid={sid}";
        if (!string.IsNullOrEmpty(existingToken))
            url += $"&SynoToken={Uri.EscapeDataString(existingToken)}";

        var response = await HttpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("success", out var ok) || !ok.GetBoolean())
            return existingToken;
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return existingToken;

        if (data.TryGetProperty("synotoken", out var t) && t.ValueKind == JsonValueKind.String)
        {
            var v = t.GetString();
            if (!string.IsNullOrEmpty(v))
                return v;
        }

        if (data.TryGetProperty("SynoToken", out var t2) && t2.ValueKind == JsonValueKind.String)
        {
            var v = t2.GetString();
            if (!string.IsNullOrEmpty(v))
                return v;
        }

        return existingToken;
    }

    public async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        var str = await GetStringAsync(url, cancellationToken);
        return ParseResponse<T>(str);
    }

    public async Task<SynologyResponse<object>> GetAsyncRawAsync(string url, CancellationToken cancellationToken = default)
    {
        var str = await GetStringAsync(url, cancellationToken);
        return JsonSerializer.Deserialize<SynologyResponse<object>>(str,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SynologyResponse<object>();
    }

    internal async Task<JsonDocument> GetJsonDocumentAsync(string url, CancellationToken cancellationToken = default)
    {
        var str = await GetStringAsync(url, cancellationToken);
        return JsonDocument.Parse(str);
    }

    public async Task<Stream> GetStreamAsync(string url, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, FormatSid(url));
        ApplySynoTokenHeader(request);
        var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, FormatSid(url));
        ApplySynoTokenHeader(request);
        var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return body;
    }

    protected string FormatSid(string url)
    {
        if (!url.Contains("{0}"))
            return AppendSession(url);
        return string.Format(url, BuildSessionQuery());
    }

    internal string BuildAuthenticatedApiUrl(string urlWithSessionPlaceholder) => FormatSid(urlWithSessionPlaceholder);

    internal string BuildSessionQuery(string? sid = null, string? synoToken = null)
    {
        sid ??= Sid;
        synoToken ??= SynoToken;
        var sb = new StringBuilder($"_sid={sid}");
        if (!string.IsNullOrEmpty(synoToken))
            sb.Append($"&SynoToken={Uri.EscapeDataString(synoToken)}");
        return sb.ToString();
    }

    /// <summary>与浏览器 Photos 个人空间一致的 Cookie（Browse API 用）。</summary>
    internal void ApplyPhotosWebCookies()
    {
        var uri = new Uri(BaseUrl);
        AddCookie(uri, "ViewLibrary", "personal_space");
        AddCookie(uri, "AutoSmartAlbumLibrary", "personal_space");
        AddCookie(uri, "ViewType", "folder");
    }

    internal string? GetSessionIdCookieValue()
    {
        var uri = new Uri(BaseUrl);
        return _cookieContainer.GetCookies(uri)["id"]?.Value;
    }

    internal string? GetDidCookieValue()
    {
        var uri = new Uri(BaseUrl);
        return _cookieContainer.GetCookies(uri)["did"]?.Value;
    }

    /// <summary>Photos 子会话登录会覆盖 <c>id</c> Cookie；恢复主 DSM 会话的 <c>id</c>。</summary>
    internal void RestoreSessionIdCookie(string? mainIdCookieValue)
    {
        if (string.IsNullOrEmpty(mainIdCookieValue))
            return;

        var uri = new Uri(BaseUrl);
        AddCookie(uri, "id", mainIdCookieValue);
        Sid = mainIdCookieValue;
    }

    internal string DescribeCookieNames()
    {
        var uri = new Uri(BaseUrl);
        return string.Join(", ", _cookieContainer.GetCookies(uri).Cast<Cookie>().Select(c => c.Name));
    }

    /// <summary>导出当前 CookieContainer 为请求头字符串（调试用）。</summary>
    public string BuildCookieHeader()
    {
        var uri = new Uri(BaseUrl);
        return string.Join("; ",
            _cookieContainer.GetCookies(uri).Cast<Cookie>()
                .Where(c => !c.Expired)
                .Select(c => $"{c.Name}={c.Value}"));
    }

    /// <summary>将 HTTPS DSM 基址转为 HTTP 明文端口（常见 5001→5000），供旧 WebView 引导 Cookie。</summary>
    public static string ToHttpCleartextBaseUrl(string httpsBaseUrl)
    {
        var uri = new Uri(httpsBaseUrl.TrimEnd('/'));
        var port = uri.Port switch
        {
            5001 => 5000,
            443 => 80,
            _ when uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) => uri.Port,
            _ => uri.Port
        };
        return $"http://{uri.Host}:{port}";
    }

    /// <summary>从浏览器 DevTools 复制的 Cookie 请求头注入（调试用，勿提交仓库）。</summary>
    public void ApplyCookieHeader(string cookieHeader)
    {
        var uri = new Uri(BaseUrl);
        foreach (var part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            var name = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (name.Length > 0)
                AddCookie(uri, name, value);
        }

        var id = GetSessionIdCookieValue();
        if (!string.IsNullOrEmpty(id))
            Sid = id;
    }

    private void AddCookie(Uri uri, string name, string value)
    {
        try
        {
            _cookieContainer.Add(uri, new Cookie(name, value, "/"));
        }
        catch (CookieException)
        {
            // 忽略非法 Cookie 名/值
        }
    }

    internal string AppendSession(string url) => AppendSession(url, null);

    internal string AppendSession(string url, string? sidOverride)
    {
        var sep = url.Contains('?') ? "&" : "?";
        return $"{url}{sep}{BuildSessionQuery(sidOverride)}";
    }

    internal string AppendFileStationSession(string url) =>
        AppendSession(url, !string.IsNullOrEmpty(FileStationSid) ? FileStationSid : Sid);

    protected T ParseResponse<T>(string content)
    {
        var result = JsonSerializer.Deserialize<SynologyResponse<T>>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (result == null)
            throw new Exception("Empty response from NAS.");
        result.CheckErrorCode();
        return result.Data;
    }

    public static async Task<string> ComputeMd5HexAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var hash = await MD5.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Photos 上传 check：32 位小写 MD5 十六进制（非 JSON 数组）。</summary>
    public static string FormatPhotosCheckValue(string md5Hex) => NormalizeMd5Hex(md5Hex);

    internal static string NormalizeMd5Hex(string md5Hex)
    {
        var hex = md5Hex.Trim().ToLowerInvariant();
        if (hex.Length != 32 || !hex.All(Uri.IsHexDigit))
            throw new ArgumentException($"check 须为 32 位 MD5 十六进制，当前: {md5Hex}", nameof(md5Hex));
        return hex;
    }

    internal string GetPhotosDeviceId()
    {
        if (!string.IsNullOrEmpty(PhotosDeviceId))
            return PhotosDeviceId;

        var did = GetDidCookieValue();
        if (!string.IsNullOrEmpty(did))
            return PhotosDeviceId = did;

        return PhotosDeviceId ??= Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// 与官方 Synology Android App 抓包一致：备份到 NAS 相册（非个人空间 folder）。
    /// POST <c>webapi/entry.cgi</c>（无 URL 查询参数），Cookie <c>did</c>+<c>id</c> 认证；
    /// multipart（SAZ 3017）：文本字段无 Content-Type；thumb 文件名为 xl/sm；含 raw_data。
    /// </summary>
    public const string AppPhotosAndroidUserAgent =
        "Synology-Synology_Photos_2.2.0_rv:556_23127PN0CC_Android_36_(Dalvik/2.1.0 (Linux; U; Android 16; 23127PN0CC Build/BP2A.250605.031.A3))";

    public async Task<UploadResult> UploadItemAppAlbumAsync(
        UploadStreamFactory openStream,
        string fileName,
        string mimeType,
        int albumId,
        long fileSize = 0,
        long mtimeUnix = 0,
        IProgress<double>? uploadProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (albumId <= 0)
            throw new ArgumentOutOfRangeException(nameof(albumId));

        await EnsureSidAsync();
        return await PostAppAlbumUploadFromStreamAsync(
            openStream,
            fileName,
            mimeType,
            albumId,
            fileSize,
            mtimeUnix,
            uploadProgress,
            cancellationToken);
    }

    public async Task<UploadResult> UploadItemAppAlbumFromBytesAsync(
        byte[] fileBytes,
        string fileName,
        string mimeType,
        int albumId,
        long fileSize = 0,
        long mtimeUnix = 0,
        IProgress<double>? uploadProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (albumId <= 0)
            throw new ArgumentOutOfRangeException(nameof(albumId));

        await EnsureSidAsync();
        return await PostAppAlbumUploadAsync(
            fileBytes,
            fileName,
            mimeType,
            albumId,
            fileSize,
            mtimeUnix,
            uploadProgress,
            cancellationToken);
    }

    private Uri BuildAppAlbumUploadUri() =>
        BuildApiUri(DsmWebApiEntry);

    /// <summary>上传前恢复官方 App Cookie 会话（<c>did</c> + <c>id</c>）。</summary>
    public void PrepareAppUploadSession()
    {
        RestorePersistedPhotosDeviceId();
        EnsureAppDeviceCookie();
        lock (_warmupLock)
            _warmedAppAlbumIds.Clear();
    }

    /// <summary>官方 App 用 Cookie <c>did</c> 标识设备；登录后由 NAS 下发，勿覆盖。</summary>
    internal void EnsureAppDeviceCookie()
    {
        var uri = new Uri(BaseUrl);
        var existing = _cookieContainer.GetCookies(uri)["did"]?.Value;
        if (!string.IsNullOrEmpty(existing))
            PhotosDeviceId = existing;
    }

    /// <summary>官方 App 抓包仅带 <c>id</c>+<c>did</c>，移除网页浏览用的 View* Cookie。</summary>
    internal void StripPhotosViewCookiesForAppUpload()
    {
        var uri = new Uri(BaseUrl);
        foreach (var name in new[] { "ViewLibrary", "AutoSmartAlbumLibrary", "ViewType" })
        {
            var cookie = _cookieContainer.GetCookies(uri)[name];
            if (cookie != null)
                cookie.Expired = true;
        }
    }

    /// <summary>date 字段：官方 App 在 mtime（UTC 秒）基础上加本地时区偏移。</summary>
    internal static long ToAppDateSeconds(long mtimeSec) =>
        mtimeSec + (long)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.FromUnixTimeSeconds(mtimeSec)).TotalSeconds;

    private static async Task<byte[]> BufferUploadFileAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream mem)
        {
            if (mem.CanSeek)
                mem.Position = 0;
            return mem.ToArray();
        }

        using var buffer = new MemoryStream();
        if (stream.CanSeek)
            stream.Position = 0;
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private string BuildAppCookieHeader()
    {
        var uri = new Uri(BaseUrl);
        var id = _cookieContainer.GetCookies(uri)["id"]?.Value;
        var did = _cookieContainer.GetCookies(uri)["did"]?.Value;
        if (string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(Sid))
            id = Sid;
        if (string.IsNullOrEmpty(did) && !string.IsNullOrEmpty(PhotosDeviceId))
            did = PhotosDeviceId;

        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(did))
            parts.Add($"did={did}");
        if (!string.IsNullOrEmpty(id))
            parts.Add($"id={id}");
        return parts.Count == 0 ? string.Empty : string.Join("; ", parts);
    }

    private void ApplyAppAlbumUploadHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", AppPhotosAndroidUserAgent);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");

        // 实机：CookieContainer 对 POST entry.cgi 常不附带 id+did；须与官方抓包一样显式写入。
        var cookieHeader = BuildAppCookieHeader();
        if (!string.IsNullOrEmpty(cookieHeader))
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
    }

    internal void ApplyAppApiHeaders(HttpRequestMessage request) =>
        ApplyAppAlbumUploadHeaders(request);

    private Uri BuildAppApiUri() =>
        BuildApiUri(DsmWebApiEntry);

    internal async Task<string> PostAppFormRawAsync(
        string api,
        int version,
        string method,
        IEnumerable<KeyValuePair<string, string>>? extraFields = null,
        CancellationToken cancellationToken = default)
    {
        using var content = CreateAppFormContent(BuildAppFormFields(api, method, version, extraFields));
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildAppApiUri()) { Content = content };
        ApplyAppApiHeaders(request);

        var response = await SendAppRequestAsync(request, cancellationToken: cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        RefreshAppCookiesFromResponse(response);
        return body;
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildAppFormFields(
        string api,
        string method,
        int version,
        IEnumerable<KeyValuePair<string, string>>? extraFields = null)
    {
        // SAZ 字段顺序：api → method → version → …
        var fields = new List<KeyValuePair<string, string>>
        {
            new("api", api),
            new("method", method),
            new("version", version.ToString())
        };
        if (extraFields != null)
            fields.AddRange(extraFields);
        return fields;
    }

    /// <summary>SAZ：Content-Type 为 <c>application/x-www-form-urlencoded</c>（无 charset）。</summary>
    private static ByteArrayContent CreateAppFormContent(
        IEnumerable<KeyValuePair<string, string>> fields)
    {
        var body = string.Join("&", fields.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");
        return content;
    }

    internal async Task<bool> TryPostAppFormAsync(
        string api,
        int version,
        string method,
        IEnumerable<KeyValuePair<string, string>>? extraFields = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await PostAppFormAsync(api, version, method, extraFields, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal async Task<SynologyResponse<object>> PostAppFormAsync(
        string api,
        int version,
        string method,
        IEnumerable<KeyValuePair<string, string>>? extraFields = null,
        CancellationToken cancellationToken = default)
    {
        using var content = CreateAppFormContent(BuildAppFormFields(api, method, version, extraFields));
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildAppApiUri()) { Content = content };
        ApplyAppApiHeaders(request);

        var response = await SendAppRequestAsync(request, cancellationToken: cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        RefreshAppCookiesFromResponse(response);
        response.EnsureSuccessStatusCode();

        var result = JsonSerializer.Deserialize<SynologyResponse<object>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SynologyResponse<object>();
        result.CheckErrorCode();
        return result;
    }

    internal async Task<T?> PostAppFormAsync<T>(
        string api,
        int version,
        string method,
        IEnumerable<KeyValuePair<string, string>>? extraFields = null,
        CancellationToken cancellationToken = default)
    {
        var result = await PostAppFormAsync(api, version, method, extraFields, cancellationToken);
        if (result.Data is JsonElement el)
            return JsonSerializer.Deserialize<T>(el.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return default;
    }

    /// <summary>各响应 <c>Set-Cookie: id=</c> 与登录 <c>data.sid</c> 一致，须持续同步。</summary>
    internal void RefreshAppCookiesFromResponse(HttpResponseMessage response)
    {
        IEnumerable<string> setCookies;
        if (response.Headers.TryGetValues("Set-Cookie", out var headers))
            setCookies = headers;
        else if (response.Headers.NonValidated.TryGetValues("Set-Cookie", out var nv))
            setCookies = nv;
        else
            return;

        var uri = new Uri(BaseUrl);
        foreach (var header in setCookies)
        {
            var part = header.Split(';', 2)[0];
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            var name = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (name.Equals("id", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                SetCookie(uri, "id", value);
                Sid = value;
            }
            else if (name.Equals("did", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                ApplyAppDeviceId(value);
                SavePersistedDeviceId(value);
            }
        }
    }

    /// <summary>SAZ 1407–1421：登录后官方 App 初始化（全程 Cookie + POST form，无 <c>_sid</c>）。</summary>
    internal async Task RunAppPostLoginSequenceAsync(CancellationToken cancellationToken = default)
    {
        await TryPostAppFormAsync(
            "SYNO.Foto.Setting.MobileCompatibility", 1, "get", cancellationToken: cancellationToken);

        await TryPostAppFormAsync(
            "SYNO.Entry.Request",
            1,
            "request",
            [
                new KeyValuePair<string, string>("compound", AppCompoundRequests.BootstrapCompoundJson),
                new KeyValuePair<string, string>("stop_when_error", "false")
            ],
            cancellationToken);

        await TryPostAppFormAsync("SYNO.Foto.Setting.Wizard", 1, "get", cancellationToken: cancellationToken);
        await TryPostAppFormAsync("SYNO.Foto.Browse.Diff", 5, "get_version", cancellationToken: cancellationToken);
        await TryPostAppFormAsync("SYNO.Foto.Browse.Category", 1, "get", cancellationToken: cancellationToken);

        await TryPostAppFormAsync(
            "SYNO.Foto.Browse.Album",
            4,
            "list",
            [
                new KeyValuePair<string, string>("offset", "0"),
                new KeyValuePair<string, string>("limit", "1000"),
                new KeyValuePair<string, string>("category", "\"all\""),
                new KeyValuePair<string, string>("additional", AppCapture.BrowseAlbumListAdditional),
                new KeyValuePair<string, string>("accept_language", "chs")
            ],
            cancellationToken);
    }

    private static KeyValuePair<string, string>[] AppAlbumGetFields(int albumId) =>
    [
        new KeyValuePair<string, string>("id", $"[{albumId}]"),
        new KeyValuePair<string, string>("additional", AppCapture.BrowseAlbumGetAdditional),
        new KeyValuePair<string, string>("accept_language", "chs")
    ];

    private static KeyValuePair<string, string>[] AppAlbumItemListFields(int albumId, int offset = 0) =>
    [
        new KeyValuePair<string, string>("offset", offset.ToString()),
        new KeyValuePair<string, string>("limit", "1000"),
        new KeyValuePair<string, string>("album_id", albumId.ToString()),
        new KeyValuePair<string, string>("sort_by", "\"takentime\""),
        new KeyValuePair<string, string>("sort_direction", "\"asc\""),
        new KeyValuePair<string, string>("additional", AppCapture.BrowseItemListAdditional),
        new KeyValuePair<string, string>("geocoding_accept_language", "chs")
    ];

    /// <summary>SAZ 1450–1457：上传前预热（Item list → Album get × N → compound → Album get）。每相册每轮备份仅一次。</summary>
    internal async Task WarmupAppAlbumBeforeUploadAsync(int albumId, CancellationToken cancellationToken = default)
    {
        lock (_warmupLock)
        {
            if (_warmedAppAlbumIds.Contains(albumId))
                return;
        }

        await TryPostAppFormAsync(
            "SYNO.Foto.Browse.Item", 5, "list", AppAlbumItemListFields(albumId), cancellationToken);

        await TryPostAppFormAsync(
            "SYNO.Foto.Browse.Album", 4, "get", AppAlbumGetFields(albumId), cancellationToken);
        await TryPostAppFormAsync(
            "SYNO.Foto.Browse.Album", 4, "get", AppAlbumGetFields(albumId), cancellationToken);

        await TryPostAppFormAsync(
            "SYNO.Entry.Request",
            1,
            "request",
            [
                new KeyValuePair<string, string>("compound", AppCompoundRequests.BootstrapCompoundJson),
                new KeyValuePair<string, string>("stop_when_error", "false")
            ],
            cancellationToken);

        await TryPostAppFormAsync(
            "SYNO.Foto.Browse.Album", 4, "get", AppAlbumGetFields(albumId), cancellationToken);
        await TryPostAppFormAsync(
            "SYNO.Foto.Browse.Album", 4, "get", AppAlbumGetFields(albumId), cancellationToken);

        lock (_warmupLock)
            _warmedAppAlbumIds.Add(albumId);
    }

    /// <summary>小于此值的文件可在内存中缓冲后上传；更大文件应走流式 multipart。</summary>
    internal const int InMemoryUploadMaxBytes = 16 * 1024 * 1024;

    private static async Task<byte[]> ReadAppUploadFileBytesAsync(
        UploadStreamFactory openStream,
        long hintedSize,
        CancellationToken cancellationToken)
    {
        await using var stream = await openStream(cancellationToken);
        using var ms = new MemoryStream(
            hintedSize is > 0 and <= InMemoryUploadMaxBytes ? (int)hintedSize : 256 * 1024);
        await stream.CopyToAsync(ms, cancellationToken);
        if (ms.Length > InMemoryUploadMaxBytes)
            throw new InvalidOperationException(
                $"文件过大（{ms.Length} 字节），请使用流式上传（上限 {InMemoryUploadMaxBytes}）。");
        return ms.ToArray();
    }

    private async Task<UploadResult> PostAppAlbumUploadFromStreamAsync(
        UploadStreamFactory openStream,
        string fileName,
        string mimeType,
        int albumId,
        long fileSize,
        long mtimeUnix,
        IProgress<double>? uploadProgress,
        CancellationToken cancellationToken)
    {
        EnsureAppDeviceCookie();
        StripPhotosViewCookiesForAppUpload();

        var mtimeSec = ToMtimeSeconds(mtimeUnix);
        await WarmupAppAlbumBeforeUploadAsync(albumId, cancellationToken);

        var idCookie = GetSessionIdCookieValue();
        if (!string.IsNullOrEmpty(idCookie))
            Sid = idCookie;

        var dateSec = ToAppDateSeconds(mtimeSec);
        var (thumbXl, thumbSm) = await AppThumbnailGenerator.CreateForUploadAsync(
            mimeType, openStream, cancellationToken);

        var fileBytesLength = await ResolveUploadFileBytesLengthAsync(openStream, fileSize, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildAppAlbumUploadUri());
        request.Content = new AppMultipartUploadContent(
            fileName,
            albumId,
            mtimeSec,
            dateSec,
            thumbXl,
            thumbSm,
            AppCapture.UploadRawDataJson,
            openStream,
            fileBytesLength,
            uploadProgress);
        ApplyAppAlbumUploadHeaders(request);

        var response = await SendAppRequestAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        RefreshAppCookiesFromResponse(response);
        if (!response.IsSuccessStatusCode)
        {
            throw new SynologyUploadException(
                (int)response.StatusCode,
                DescribeUploadHttpError((int)response.StatusCode),
                string.IsNullOrEmpty(responseBody) ? response.ReasonPhrase ?? "" : responseBody);
        }

        var parsed = ParseUploadResponse(responseBody);
        return await FinalizeAppAlbumUploadAsync(
            parsed,
            albumId,
            fileName,
            fileBytesLength,
            cancellationToken);
    }

    private static async Task<long> ResolveUploadFileBytesLengthAsync(
        UploadStreamFactory openStream,
        long hintedSize,
        CancellationToken cancellationToken)
    {
        if (hintedSize > 0)
            return hintedSize;

        await using var stream = await openStream(cancellationToken);
        if (stream.CanSeek && stream.Length > 0)
            return stream.Length;

        throw new InvalidOperationException("无法确定文件大小，无法计算 multipart Content-Length。");
    }

    private async Task<UploadResult> PostAppAlbumUploadAsync(
        byte[] fileBytes,
        string fileName,
        string mimeType,
        int albumId,
        long fileSize,
        long mtimeUnix,
        IProgress<double>? uploadProgress,
        CancellationToken cancellationToken)
    {
        EnsureAppDeviceCookie();
        StripPhotosViewCookiesForAppUpload();

        var mtimeSec = ToMtimeSeconds(mtimeUnix);
        await WarmupAppAlbumBeforeUploadAsync(albumId, cancellationToken);

        var idCookie = GetSessionIdCookieValue();
        if (!string.IsNullOrEmpty(idCookie))
            Sid = idCookie;

        var dateSec = ToAppDateSeconds(mtimeSec);
        var (thumbXl, thumbSm) = await AppThumbnailGenerator.CreateForUploadFromBytesAsync(
            mimeType, fileBytes, cancellationToken);

        var (body, boundary) = AppMultipartBuilder.BuildAlbumUpload(
            fileBytes,
            fileName,
            albumId,
            mtimeSec,
            dateSec,
            thumbXl,
            thumbSm,
            AppCapture.UploadRawDataJson);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildAppAlbumUploadUri());
        request.Content = new ProgressByteArrayContent(body, uploadProgress);
        request.Content.Headers.TryAddWithoutValidation(
            "Content-Type", $"multipart/form-data; boundary={boundary}");
        ApplyAppAlbumUploadHeaders(request);

        var response = await SendAppRequestAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        RefreshAppCookiesFromResponse(response);
        if (!response.IsSuccessStatusCode)
        {
            throw new SynologyUploadException(
                (int)response.StatusCode,
                DescribeUploadHttpError((int)response.StatusCode),
                string.IsNullOrEmpty(responseBody) ? response.ReasonPhrase ?? "" : responseBody);
        }

        var parsed = ParseUploadResponse(responseBody);
        return await FinalizeAppAlbumUploadAsync(
            parsed,
            albumId,
            fileName,
            fileSize > 0 ? fileSize : fileBytes.LongLength,
            cancellationToken);
    }

    internal async Task<bool> TryAddPhotoToNormalAlbumAsync(
        int albumId,
        int photoId,
        CancellationToken cancellationToken = default)
    {
        var itemJson = JsonSerializer.Serialize(new[] { new { id = photoId, type = "photo" } });
        return await TryPostAppFormAsync(
            "SYNO.Foto.Browse.NormalAlbum",
            1,
            "add_item",
            [
                new KeyValuePair<string, string>("id", albumId.ToString()),
                new KeyValuePair<string, string>("item", itemJson)
            ],
            cancellationToken);
    }

    private async Task<UploadResult> FinalizeAppAlbumUploadAsync(
        UploadResult result,
        int albumId,
        string fileName,
        long fileSize,
        CancellationToken cancellationToken)
    {
        if (!result.Success || result.PhotoId <= 0)
        {
            result.VerifiedOnServer = false;
            return result;
        }

        // Upload response already tells us if it's a duplicate
        if (!string.Equals(result.Action, "new", StringComparison.OrdinalIgnoreCase))
        {
            result.VerifiedOnServer = true;
            result.SkippedAsDuplicate = true;
            return result;
        }

        // New upload: add to album and verify
        result.VerifiedOnServer = await TryAddPhotoToNormalAlbumAsync(
            albumId, result.PhotoId, cancellationToken);
        return result;
    }

    /// <summary>Photos 上传 mtime：10 位 Unix 秒（非毫秒）。</summary>
    internal static long ToMtimeSeconds(long mtimeUnix)
    {
        if (mtimeUnix <= 0)
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return mtimeUnix < 10_000_000_000L ? mtimeUnix : mtimeUnix / 1000;
    }

    internal async Task RefreshCookieSessionSynoTokenAsync(CancellationToken cancellationToken = default)
    {
        var id = GetSessionIdCookieValue();
        if (string.IsNullOrEmpty(id))
            return;

        var token = await FetchSynoTokenAsync(
            DsmWebApiEntry, id, CookieSessionSynoToken ?? SynoToken, cancellationToken);
        if (!string.IsNullOrEmpty(token))
            CookieSessionSynoToken = token;
    }

    private static string DescribeUploadHttpError(int statusCode) => statusCode switch
    {
        403 => "NAS 拒绝上传（HTTP 403，请确认 Cookie id/did 与登录账号一致且未过期）",
        _ => $"Photos upload HTTP {statusCode}"
    };

    private static UploadResult ParseUploadResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("success", out var successEl) && !successEl.GetBoolean())
        {
            var code = root.TryGetProperty("error", out var err) && err.TryGetProperty("code", out var c)
                ? c.GetInt32()
                : -1;
            throw new SynologyUploadException(code, DescribeUploadErrorCode(code, body), body);
        }

        var photoId = 0;
        var action = "";
        if (root.TryGetProperty("data", out var data))
        {
            photoId = ExtractPhotoId(data);
            if (data.TryGetProperty("action", out var actionEl))
                action = actionEl.GetString() ?? "";
        }

        return new UploadResult
        {
            Success = true,
            PhotoId = photoId,
            Action = action,
            RawResponse = body,
            VerifiedOnServer = false
        };
    }

    private static string DescribeUploadErrorCode(int code, string? body = null)
    {
        if (code == 101)
            return "NAS upload error 101（multipart 缺少 api/method/version）";

        if (code == 108)
            return "NAS Photos 上传被拒绝（108，多为移动备份设备未授权；请在官方 App 用相同 did 完成备份授权）";

        if (code == 119)
            return "NAS Photos 上传会话无效（119，请确认 Cookie id+did 与官方 App 登录一致）";

        return $"NAS upload error {code}";
    }

    internal static int ExtractPhotoId(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Number && data.TryGetInt32(out var n))
            return n;
        if (data.TryGetProperty("id", out var id) && id.TryGetInt32(out var idVal))
            return idVal;
        if (data.TryGetProperty("unit_id", out var unitId) && unitId.TryGetInt32(out var unitVal))
            return unitVal;
        if (data.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array && ids.GetArrayLength() > 0 &&
            ids[0].TryGetInt32(out var firstId))
            return firstId;
        if (data.TryGetProperty("item", out var item))
            return ExtractPhotoId(item);
        return 0;
    }

    internal void ApplySynoTokenHeader(HttpRequestMessage request) =>
        ApplySynoTokenHeader(request, SynoToken);

    internal void ApplySynoTokenHeader(HttpRequestMessage request, string? token) =>
        ApplySynoTokenHeaderInternal(request, token);

    internal void ApplyFileStationSynoTokenHeader(HttpRequestMessage request) =>
        ApplySynoTokenHeader(request, FileStationSynoToken ?? SynoToken);

    private static void ApplySynoTokenHeaderInternal(HttpRequestMessage request, string? token)
    {
        if (!string.IsNullOrEmpty(token))
            request.Headers.TryAddWithoutValidation("X-SYNO-TOKEN", token);
    }

    private async Task EnsureSidAsync()
    {
        var id = GetSessionIdCookieValue();
        if (!string.IsNullOrEmpty(id))
            Sid = id;
        if (string.IsNullOrEmpty(Sid))
            throw new InvalidOperationException("Not logged in.");
        await Task.CompletedTask;
    }
}

