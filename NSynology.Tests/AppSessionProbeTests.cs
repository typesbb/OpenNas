using System.Text.Json;
using NSynology;
using NSynology.Auth;
using NSynology.Diagnostics;
using NSynology.Foto;
using Xunit;
using Xunit.Abstractions;

namespace NSynology.Tests;

[Collection(NasIntegrationCollection.Name)]
public sealed class AppSessionProbeTests
{
    private readonly ITestOutputHelper _output;

    public AppSessionProbeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Probe_auth_version_matrix_for_cookie_post()
    {
        var cfg = NasIntegrationSettings.Load();
        foreach (var version in new[] { 6, 7, 3 })
        {
            foreach (var enableSynoToken in new[] { false, true })
            {
                var client = new SynologyClient(cfg.BaseUrl);
                if (!string.IsNullOrWhiteSpace(cfg.PhotosDeviceId))
                    client.PhotosDeviceId = cfg.PhotosDeviceId.Trim();
                client.ClearHttpCookiesPreservingDid(client.PhotosDeviceId);

                var encInfo = await FetchEncInfoAsync(client);
                if (encInfo == null)
                {
                    _output.WriteLine($"v{version} token={enableSynoToken}: getinfo failed");
                    continue;
                }

                var fields = new Dictionary<string, string>
                {
                    ["api"] = "SYNO.API.Auth",
                    ["version"] = version.ToString(),
                    ["method"] = "login",
                    ["account"] = cfg.Username,
                    ["passwd"] = cfg.Password,
                    ["session"] = "SynologyPhotos",
                    ["format"] = "cookie",
                    ["stay_login"] = "yes"
                };
                if (enableSynoToken)
                    fields["enable_syno_token"] = "yes";

                var (cipherField, cipherPayload, clientTime) =
                    SynologyApiEncryption.EncryptLoginFields(encInfo, fields);
                var auth = await client.LoginRawPostAsync(
                    SynologyClient.DsmWebApiEntry,
                    [
                        new KeyValuePair<string, string>(cipherField, cipherPayload),
                        new KeyValuePair<string, string>("client_time", clientTime.ToString())
                    ],
                    useAppUserAgent: true);

                if (auth == null)
                {
                    _output.WriteLine($"v{version} token={enableSynoToken}: login failed");
                    continue;
                }

                client.ApplyAppAuthResult(auth);
                client.SynoToken = enableSynoToken ? auth.SynoToken : null;

                var post = await client.PostAppFormRawAsync(
                    "SYNO.Foto.Setting.MobileCompatibility", 1, "get");
                using var doc = JsonDocument.Parse(post);
                var ok = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
                var hasSyno = !string.IsNullOrEmpty(auth.SynoToken);
                _output.WriteLine(
                    $"v{version} token={enableSynoToken} login-ok syno={hasSyno} post-ok={ok} body={post[..Math.Min(post.Length, 120)]}");
            }
        }
    }

    private static async Task<SynologyApiEncryption.EncryptionInfo?> FetchEncInfoAsync(SynologyClient client)
    {
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("api", "SYNO.API.Encryption"),
            new KeyValuePair<string, string>("method", "getinfo"),
            new KeyValuePair<string, string>("version", "1")
        ]);
        using var request = new HttpRequestMessage(HttpMethod.Post, client.BuildApiUri(SynologyClient.DsmWebApiEntry))
        {
            Content = content
        };
        client.ApplyAppApiHeaders(request);
        var response = await client.HttpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return null;
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("success", out var ok) || !ok.GetBoolean())
            return null;
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return null;
        return SynologyApiEncryption.ParseInfo(data);
    }

    [Fact]
    public async Task Probe_upload_saz_fixture_via_client_builder()
    {
        var cfg = NasIntegrationSettings.Load();
        var fixtureDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures"));
        var filePath = Path.Combine(fixtureDir, "official_upload_file.jpg");
        Assert.True(File.Exists(filePath), $"缺少 {filePath}");

        var client = new SynologyClient(cfg.BaseUrl);
        if (!string.IsNullOrWhiteSpace(cfg.PhotosDeviceId))
            client.PhotosDeviceId = cfg.PhotosDeviceId.Trim();

        Assert.True(await client.Auth.LoginAppStyleAsync(cfg.Username, cfg.Password));

        var fileInfo = new FileInfo(filePath);
        var result = await client.UploadItemAppAlbumAsync(
            ct => Task.FromResult<Stream>(File.OpenRead(filePath)),
            "mmexport1780538292421.jpg",
            "image/jpeg",
            albumId: 15,
            fileInfo.Length,
            mtimeUnix: 1_780_538_292);

        _output.WriteLine(result.RawResponse);
        Assert.True(result.Success, result.RawResponse);
    }

    [Fact]
    public async Task Probe_replay_saz_3017_capture_upload_body()
    {
        var cfg = NasIntegrationSettings.Load();
        var capturePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "_saz_extract2", "raw", "3017_c.txt"));
        Assert.True(File.Exists(capturePath), $"缺少抓包文件: {capturePath}");

        var raw = await File.ReadAllBytesAsync(capturePath);
        var sep = "\r\n\r\n"u8.ToArray();
        var sepIdx = IndexOf(raw, sep);
        Assert.True(sepIdx >= 0, "抓包文件无 HTTP body 分隔");
        var body = raw.AsSpan(sepIdx + sep.Length).ToArray();

        const string boundary = "c31543b5-4fae-4761-9144-9bc62ea7b9ce";
        var client = new SynologyClient(cfg.BaseUrl);
        if (!string.IsNullOrWhiteSpace(cfg.PhotosDeviceId))
            client.PhotosDeviceId = cfg.PhotosDeviceId.Trim();

        Assert.True(await client.Auth.LoginAppStyleAsync(cfg.Username, cfg.Password));

        using var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation(
            "Content-Type", $"multipart/form-data; boundary={boundary}");
        using var request = new HttpRequestMessage(HttpMethod.Post, client.BuildApiUri(SynologyClient.DsmWebApiEntry))
        {
            Content = content
        };
        client.ApplyAppApiHeaders(request);

        var response = await client.SendAppRequestAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"replay-status={(int)response.StatusCode} body={text}");
        Assert.Contains("\"success\":true", text, StringComparison.OrdinalIgnoreCase);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }

    [Fact]
    public async Task Probe_app_post_vs_get_after_login()
    {
        var cfg = NasIntegrationSettings.Load();
        var client = new SynologyClient(cfg.BaseUrl);
        if (!string.IsNullOrWhiteSpace(cfg.PhotosDeviceId))
            client.PhotosDeviceId = cfg.PhotosDeviceId.Trim();
        client.ConfigureHttpTrace(true, _output.WriteLine);

        Assert.True(await client.Auth.LoginAppStyleAsync(cfg.Username, cfg.Password));
        _output.WriteLine($"encrypted-login={client.Auth.LastLoginUsedEncryptedPost}");

        // 对比：仅明文 GET 登录（非 SAZ 路径）
        var plainClient = new SynologyClient(cfg.BaseUrl);
        if (!string.IsNullOrWhiteSpace(cfg.PhotosDeviceId))
            plainClient.PhotosDeviceId = cfg.PhotosDeviceId.Trim();
        plainClient.ClearHttpCookiesPreservingDid(plainClient.PhotosDeviceId);
        var plainOk = await plainClient.Auth.LoginAsync(cfg.Username, cfg.Password);
        _output.WriteLine($"plain-dsm-login={plainOk}");
        if (plainOk)
        {
            var plainPost = await plainClient.PostAppFormRawAsync(
                "SYNO.Foto.Browse.Album", 4, "list",
                [
                    new KeyValuePair<string, string>("offset", "0"),
                    new KeyValuePair<string, string>("limit", "5"),
                    new KeyValuePair<string, string>("category", "\"all\""),
                    new KeyValuePair<string, string>("additional", AppCapture.BrowseAlbumListAdditional),
                    new KeyValuePair<string, string>("accept_language", "chs")
                ]);
            _output.WriteLine($"plain-login POST Browse: {plainPost}");
        }
        _output.WriteLine($"cookie-header={client.BuildCookieHeader()}");

        var postCompat = await client.PostAppFormRawAsync(
            "SYNO.Foto.Setting.MobileCompatibility", 1, "get");
        _output.WriteLine($"POST MobileCompatibility: {postCompat}");

        var postAlbum = await client.PostAppFormRawAsync(
            "SYNO.Foto.Browse.Album",
            4,
            "list",
            [
                new KeyValuePair<string, string>("offset", "0"),
                new KeyValuePair<string, string>("limit", "5"),
                new KeyValuePair<string, string>("category", "\"all\""),
                new KeyValuePair<string, string>("additional", AppCapture.BrowseAlbumListAdditional),
                new KeyValuePair<string, string>("accept_language", "chs")
            ]);
        _output.WriteLine($"POST Browse.Album list: {postAlbum}");

        var getUrl =
            $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Browse.Album&version=4&method=list" +
            $"&offset=0&limit=5&category=%22all%22&{client.BuildSessionQuery()}";
        var getBody = await client.HttpClient.GetStringAsync(getUrl);
        _output.WriteLine($"GET Browse.Album list: {getBody}");

        var sid = client.Sid!;
        var postWithSidUrl = $"{SynologyClient.DsmWebApiEntry}?_sid={Uri.EscapeDataString(sid)}";
        var postWithSidBody = await client.PostAppFormRawAsync(
            "SYNO.Foto.Browse.Album",
            4,
            "list",
            [
                new KeyValuePair<string, string>("offset", "0"),
                new KeyValuePair<string, string>("limit", "5"),
                new KeyValuePair<string, string>("category", "\"all\""),
                new KeyValuePair<string, string>("additional", AppCapture.BrowseAlbumListAdditional),
                new KeyValuePair<string, string>("accept_language", "chs")
            ]);
        _output.WriteLine($"POST Browse.Album (cookie only): already above");

        using (var content = new ByteArrayContent(
                   System.Text.Encoding.UTF8.GetBytes(
                       "api=SYNO.Foto.Browse.Album&method=list&version=4&offset=0&limit=5&category=%22all%22")))
        {
            content.Headers.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");
            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri($"{cfg.BaseUrl.TrimEnd('/')}/{postWithSidUrl}"))
            {
                Content = content
            };
            req.Headers.TryAddWithoutValidation("User-Agent", SynologyClient.AppPhotosAndroidUserAgent);
            req.Headers.TryAddWithoutValidation("Cookie", client.BuildCookieHeader());
            var resp = await client.HttpClient.SendAsync(req);
            _output.WriteLine($"POST Browse.Album (_sid+cookie): {await resp.Content.ReadAsStringAsync()}");
        }
    }
}
