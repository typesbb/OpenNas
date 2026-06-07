using System.Net;
using System.Text;
using NSynology.Diagnostics;
using NSynology.Foto;
using Xunit;
using Xunit.Abstractions;

namespace NSynology.Tests;

/// <summary>
/// 对照官方 Synology Android App 抓包：<c>SYNO.Foto.Upload.Item v5 method=upload</c> + <c>album_id</c>。
/// </summary>
[Collection(NasIntegrationCollection.Name)]
public sealed class OfficialAppAlbumUploadTests
{
    private readonly ITestOutputHelper _output;

    public OfficialAppAlbumUploadTests(ITestOutputHelper output) => _output = output;

    /// <summary>离线：加密登录 JSON 中 data.sid 即 Cookie id，data.did 即 Cookie did。</summary>
    [Fact]
    public void Official_app_login_response_maps_sid_to_id_cookie()
    {
        const string body =
            """{"data":{"did":"NoRE8tXwJKrq_dSzkRVjkvsVEN9Mkn1xAJLSKUlsaLHK8ppAe3URrQSiXZhBgqkKihIaavqTwAG3ZqLKMaGkNw","is_portal_port":false,"sid":"pAKVbw9oBEpMD5oZNQ9Y-gipUvYnaKBBcO6O1jYMrVoTeRZ4mQv1NFryo1iypDKBcCsMESe8eHIj66za8bFOAM"},"success":true}""";

        var auth = SynologyClient.ParseAuthResponse(body);
        Assert.NotNull(auth);
        Assert.Equal("pAKVbw9oBEpMD5oZNQ9Y-gipUvYnaKBBcO6O1jYMrVoTeRZ4mQv1NFryo1iypDKBcCsMESe8eHIj66za8bFOAM", auth!.Sid);
        Assert.Equal("NoRE8tXwJKrq_dSzkRVjkvsVEN9Mkn1xAJLSKUlsaLHK8ppAe3URrQSiXZhBgqkKihIaavqTwAG3ZqLKMaGkNw", auth.Did);

        var client = new SynologyClient("https://192.168.0.2:5001/");
        client.ApplyOfficialAppAuthResult(auth);
        Assert.Equal(auth.Sid, client.GetSessionIdCookieValue());
        Assert.Equal(auth.Did, client.GetDidCookieValue());
    }

    /// <summary>离线契约：URL 与 multipart 字段与官方抓包一致（不访问 NAS）。</summary>
    [Fact]
    public async Task Official_app_album_upload_request_matches_mobile_capture_contract()
    {
        const int albumId = 15;
        const string fileName = "capture-contract.jpg";
        var recorder = new RecordingHttpHandler(
            """{"success":true,"data":{"id":9001}}""");

        var client = new SynologyClient("https://192.168.0.2:5001/", recorder)
        {
            Sid = "main-dsm-sid",
            SynoToken = "main-syno-token",
            PhotosDeviceId = "capture-device-id"
        };
        client.EnsureOfficialAppDeviceCookie();

        var jpegBytes = MinimalJpegBytes();
        var result = await client.UploadItemOfficialAlbumAsync(
            _ => Task.FromResult<Stream>(new MemoryStream(jpegBytes)),
            fileName,
            "image/jpeg",
            albumId,
            fileSize: 3,
            mtimeUnix: 1_700_000_000);

        Assert.True(result.Success);
        Assert.Equal(9001, result.PhotoId);

        var request = recorder.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(HttpMethod.Post, request!.Method);

        var uri = request.RequestUri!.ToString();
        Assert.Contains("webapi/entry.cgi", uri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api=SYNO.Foto.Upload.Item", uri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("method=upload", uri, StringComparison.OrdinalIgnoreCase);

        Assert.True(
            request.Headers.UserAgent?.ToString().Contains("Synology_Photos", StringComparison.OrdinalIgnoreCase) == true,
            "应使用官方 Photos User-Agent");

        Assert.False(string.IsNullOrEmpty(recorder.LastBody));
        foreach (var field in new[]
                 {
                     "method", "api", "version", "require_thumb_version", "name", "mtime", "date", "folder",
                     "album_id", "duplicate", "file"
                 })
        {
            Assert.True(
                recorder.LastBody!.Contains($"name=\"{field}\"", StringComparison.OrdinalIgnoreCase)
                || recorder.LastBody.Contains($"name={field}", StringComparison.OrdinalIgnoreCase),
                $"multipart 应包含字段 {field}");
        }

        Assert.Contains("SYNO.Foto.Upload.Item", recorder.LastBody!, StringComparison.Ordinal);
        Assert.Contains("upload", recorder.LastBody!, StringComparison.Ordinal);
        Assert.Contains("\"ignore\"", recorder.LastBody!, StringComparison.Ordinal);
        Assert.Contains(fileName, recorder.LastBody!, StringComparison.Ordinal);
        Assert.Contains("[\"PhotoLibrary\"]", recorder.LastBody!, StringComparison.Ordinal);
        Assert.Contains(albumId.ToString(), recorder.LastBody!, StringComparison.Ordinal);
        Assert.Contains("1700000000", recorder.LastBody!, StringComparison.Ordinal);
        Assert.Contains("filename=\"xl\"", recorder.LastBody!, StringComparison.Ordinal);
        Assert.Contains("filename=\"sm\"", recorder.LastBody!, StringComparison.Ordinal);
        Assert.Contains("_inference_by_mobile", recorder.LastBody!, StringComparison.Ordinal);
    }

    /// <summary>集成：仅主 DSM 会话（无 Photos 子会话 / _SSID）上传到 NAS 相册。</summary>
    [Fact]
    public async Task Official_app_album_upload_v5_succeeds_on_real_nas_without_photos_subsession()
    {
        var cfg = NasIntegrationSettings.Load();
        var imagePath = cfg.PickLocalImagePath();
        var fileName = $"opennas-official-album-{DateTime.UtcNow:yyyyMMddHHmmss}.jpg";

        var client = new SynologyClient(cfg.BaseUrl);
        if (!string.IsNullOrWhiteSpace(cfg.PhotosDeviceId))
            client.PhotosDeviceId = cfg.PhotosDeviceId.Trim();

        Assert.True(await client.Auth.LoginOfficialAppStyleAsync(cfg.Username, cfg.Password),
                "官方相册上传应使用 SynologyPhotos 加密登录");

            _output.WriteLine(
                $"official-login id={client.GetSessionIdCookieValue()} sid={client.Sid} " +
                $"did={client.GetDidCookieValue()} match={client.GetSessionIdCookieValue() == client.Sid}");

            var albums = (await client.Foto.ListOfficialAlbumsAsync(0, 50)).ToList();
            var album = albums.FirstOrDefault(a =>
                string.Equals(a.Name, cfg.RemoteAlbumName, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(album);
            _output.WriteLine($"target album id={album.Id} name={album.Name}");

            var fileInfo = new FileInfo(imagePath);
            var mtime = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds();

            UploadResult result;
            try
            {
                result = await client.UploadItemOfficialAlbumAsync(
                    ct => Task.FromResult<Stream>(File.OpenRead(imagePath)),
                    fileName,
                    GuessMime(Path.GetExtension(imagePath)),
                    album.Id,
                    fileInfo.Length,
                    mtime);
            }
            catch (SynologyUploadException ex)
            {
                Assert.Fail($"官方相册上传失败 code={ex.ErrorCode} message={ex.Message} body={ex.RawResponse}");
                return;
            }

            _output.WriteLine($"upload: success={result.Success} photoId={result.PhotoId}");
            _output.WriteLine(result.RawResponse);

        Assert.True(result.Success);
        Assert.True(result.PhotoId > 0, "期望 NAS 返回照片 id；Raw=" + result.RawResponse);
    }

    private static byte[] MinimalJpegBytes() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
        0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
        0x00, 0x08, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01,
        0x01, 0x11, 0x00, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F,
        0x00, 0x7F, 0xFF, 0xD9
    ];

    private static string GuessMime(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };

    private sealed class RecordingHttpHandler(string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                LastBody = Encoding.UTF8.GetString(bytes);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
