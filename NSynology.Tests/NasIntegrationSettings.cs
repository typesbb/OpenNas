using System.Text.Json;

namespace NSynology.Tests;

public sealed class NasIntegrationSettings
{
    public string BaseUrl { get; init; } = "";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string RemoteFolderName { get; init; } = "laoba";
    public string RemoteAlbumName { get; init; } = "laoba";
    public string LocalImageDirectory { get; init; } = "";
    public string? PhotosDeviceId { get; init; }

    /// <summary>可选：从浏览器成功上传请求的 Cookie 头复制，用于回放验证（会过期，仅本地调试）。</summary>
    public string? BrowserCookieHeader { get; init; }

    /// <summary>可选：与 BrowserCookieHeader 配套的 X-SYNO-TOKEN（DevTools 请求头里复制）。</summary>
    public string? BrowserSynoToken { get; init; }

    /// <summary>可选：管理员账号，用于权限诊断对比（仅 nas.integration.local.json，勿提交 git）。</summary>
    public string? AdminUsername { get; init; }

    public string? AdminPassword { get; init; }

    public static NasIntegrationSettings Load()
    {
        var fromEnv = TryLoadFromEnvironment();
        if (fromEnv != null)
            return fromEnv;

        var baseDir = AppContext.BaseDirectory;
        foreach (var fileName in new[] { "nas.integration.local.json", "nas.integration.sample.json" })
        {
            var path = Path.Combine(baseDir, fileName);
            if (!File.Exists(path))
                continue;

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<NasIntegrationSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (settings == null)
                continue;

            if (fileName.Contains("sample", StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(settings.Password)
                    || settings.Password.Contains("your_", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"请复制 {fileName} 为 nas.integration.local.json 并填写真实凭据，或设置环境变量 NAS_BASE_URL / NAS_USER / NAS_PASSWORD。");
            }

            return settings;
        }

        throw new InvalidOperationException(
            "未找到 NAS 集成测试配置。请创建 NSynology.Tests/nas.integration.local.json 或设置环境变量 NAS_BASE_URL、NAS_USER、NAS_PASSWORD、NAS_IMAGE_DIR。");
    }

    private static NasIntegrationSettings? TryLoadFromEnvironment()
    {
        var baseUrl = Environment.GetEnvironmentVariable("NAS_BASE_URL");
        var user = Environment.GetEnvironmentVariable("NAS_USER");
        var password = Environment.GetEnvironmentVariable("NAS_PASSWORD");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
            return null;

        return new NasIntegrationSettings
        {
            BaseUrl = baseUrl.Trim(),
            Username = user.Trim(),
            Password = password,
            RemoteFolderName = Environment.GetEnvironmentVariable("NAS_REMOTE_FOLDER")?.Trim() ?? "laoba",
            RemoteAlbumName = Environment.GetEnvironmentVariable("NAS_REMOTE_ALBUM")?.Trim() ?? "laoba",
            LocalImageDirectory = Environment.GetEnvironmentVariable("NAS_IMAGE_DIR")?.Trim() ?? ""
        };
    }

    public string PickLocalImagePath()
    {
        if (!string.IsNullOrWhiteSpace(LocalImageDirectory) && Directory.Exists(LocalImageDirectory))
        {
            var fromDir = Directory.EnumerateFiles(LocalImageDirectory)
                .Where(p => IsImageExtension(Path.GetExtension(p)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (fromDir != null)
                return fromDir;
        }

        throw new InvalidOperationException(
            $"本地测试图片目录无效或为空: {LocalImageDirectory}");
    }

    private static bool IsImageExtension(string? ext) =>
        ext is ".jpg" or ".jpeg" or ".png" or ".heic" or ".webp" or ".gif";
}
