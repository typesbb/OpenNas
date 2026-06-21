using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NSynology.FileStation;

/// <summary>通过 File Station 上传到 Photos 目录（Foto 失败时的回退）。</summary>
public class FileStationApi(SynologyClient client)
{
    private readonly SynologyClient _client = client;
    /// <summary>卷上真实路径，如 /volume1/homes/yue。</summary>
    private string? _cachedHomeRealPath;
    /// <summary>File Station 虚拟路径，如 /home。</summary>
    private string? _cachedHomeVirtualPath;
    private string? _cachedPhotosRoot;
    private string? _cachedPhotosVirtualPath;
    private string? _cachedPhotoShareRealPath;
    private string? _cachedPhotoShareVirtualPath;

    /// <summary>逻辑默认路径；实际上传前会按 list_share 映射为真实路径。</summary>
    public const string DefaultPhotosRoot = "/home/Photos";

    /// <summary>探测 File Station 是否可访问用户 home（用于诊断与回退上传）。</summary>
    public async Task<bool> CanAccessHomeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_client.Sid))
            return false;

        try
        {
            await GetHomeSharePathAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>home/Photos 在卷上的真实路径（如 /volume1/homes/user/Photos）。</summary>
    public async Task<string?> TryGetVolumePhotosPathAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetPhotosRootAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>共享文件夹 photo 的卷路径（如 /volume1/photo），供 backup_to_path 使用。</summary>
    public async Task<string?> TryGetPhotoShareRealPathAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetPhotoShareRealPathAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> UploadToFolderAsync(
        Stream stream,
        string fileName,
        string mimeType,
        string folderPath,
        long mtimeMs,
        long fileSize = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_client.Sid))
            throw new InvalidOperationException("未登录，无法使用 File Station 上传。");

        await EnsureFileStationAccessAsync(cancellationToken);

            var fileBytes = await ReadFileBytesAsync(stream, fileSize, cancellationToken);
            var folder = await ResolveFullFolderAsync(folderPath, cancellationToken);
            await EnsurePhotosFolderExistsAsync(folder, cancellationToken);

            var pathsToTry = await BuildCandidatePathsAsync(folder, cancellationToken);

            Exception? lastEx = null;
            foreach (var path in pathsToTry)
            {
                try
                {
                    await UploadOnceAsync(fileBytes, fileName, mimeType, path, mtimeMs, cancellationToken);
                    return true;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            var sharesHint = await DescribeSharesAsync(cancellationToken);
            throw lastEx ?? new Exception($"File Station 上传失败。可用共享：{sharesHint}");
    }

    private async Task EnsureFileStationAccessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await GetHomeSharePathAsync(cancellationToken);
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "File Station 会话无效，请在设置中退出后重新登录。", ex);
        }
    }

    /// <summary>api/method/version 在 URL；multipart 仅 path 与 file（file 在最后）。</summary>
    private async Task UploadOnceAsync(
        byte[] fileBytes,
        string fileName,
        string mimeType,
        string folderPath,
        long mtimeMs,
        CancellationToken cancellationToken)
    {
        Exception? lastEx = null;
        var maxFromApi = _client.GetMaxApiVersion("SYNO.FileStation.Upload", 1);
        var candidateVersions = maxFromApi > 1
            ? new[] { maxFromApi }.Concat(new[] { 2, 3 }.Where(v => v != maxFromApi)).ToArray()
            : new[] { 2, 3 };
        foreach (var version in candidateVersions)
        {
            try
            {
                await PostUploadAsync(fileBytes, fileName, mimeType, folderPath, mtimeMs, version, cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                lastEx = ex;
            }
        }

        throw lastEx ?? new Exception($"File Station 上传失败（path={folderPath}）");
    }

    private async Task PostUploadAsync(
        byte[] fileBytes,
        string fileName,
        string mimeType,
        string folderPath,
        long mtimeMs,
        int version,
        CancellationToken cancellationToken)
    {
        var url = _client.AppendFileStationSession(
            $"webapi/entry.cgi?api=SYNO.FileStation.Upload&version={version}&method=upload");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(folderPath), "path");
        form.Add(new StringContent("true"), "create_parents");
        form.Add(new StringContent(version >= 3 ? "overwrite" : "true"), "overwrite");

        if (mtimeMs > 0)
            form.Add(new StringContent(mtimeMs.ToString()), "mtime");

        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        form.Add(fileContent, "file", fileName);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(15));

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        _client.ApplyFileStationSynoTokenHeader(request);
        var response = await _client.HttpClient.SendAsync(request, cts.Token);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"File Station HTTP {(int)response.StatusCode}: {Trim(body, 300)}");

        ParseSuccess(body, folderPath);
    }

    private async Task EnsurePhotosFolderExistsAsync(string targetFolder, CancellationToken cancellationToken)
    {
        var photosRoot = await GetPhotosRootAsync(cancellationToken);
        if (!targetFolder.StartsWith(photosRoot, StringComparison.Ordinal))
            return;

        var home = await GetHomeSharePathAsync(cancellationToken);
        if (targetFolder.Equals(photosRoot, StringComparison.Ordinal) ||
            targetFolder.Equals(home, StringComparison.Ordinal))
        {
            await CreateFolderAsync(home, "Photos", cancellationToken);
            return;
        }

        var relative = targetFolder.Length > photosRoot.Length
            ? targetFolder[photosRoot.Length..].TrimStart('/')
            : "";
        if (string.IsNullOrEmpty(relative))
            return;

        var first = relative.Split('/')[0];
        if (!first.Equals("Photos", StringComparison.OrdinalIgnoreCase))
            await CreateFolderAsync(photosRoot, first, cancellationToken);
    }

    private async Task CreateFolderAsync(string parentPath, string name, CancellationToken cancellationToken)
    {
        var folderPath = Uri.EscapeDataString($"[\"{parentPath.TrimEnd('/')}\"]");
        var folderName = Uri.EscapeDataString($"[\"{name}\"]");
        var version = _client.GetMaxApiVersion("SYNO.FileStation.CreateFolder", 2);
        var url = _client.AppendSession(
            $"webapi/entry.cgi?api=SYNO.FileStation.CreateFolder&version={version}&method=create" +
            $"&folder_path={folderPath}&name={folderName}&force_parent=true");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        _client.ApplySynoTokenHeader(request);
        var response = await _client.HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return;

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("success", out var ok) && ok.GetBoolean())
            return;

        var code = doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("code", out var c)
            ? c.GetInt32()
            : -1;
        // 1100/1101 等表示已存在，可忽略
        if (code is 1100 or 1101)
            return;
    }

    private async Task<string> ResolveFullFolderAsync(string folderPath, CancellationToken cancellationToken)
    {
        var normalized = NormalizeFolderPath(string.IsNullOrWhiteSpace(folderPath) ? DefaultPhotosRoot : folderPath);
        var home = await GetHomeSharePathAsync(cancellationToken);
        var photosRoot = await GetPhotosRootAsync(cancellationToken);

        if (normalized.Equals(DefaultPhotosRoot, StringComparison.Ordinal) ||
            normalized.Equals("/home/Photos", StringComparison.Ordinal))
            return photosRoot;

        if (normalized.StartsWith(DefaultPhotosRoot + "/", StringComparison.Ordinal))
        {
            var sub = normalized[DefaultPhotosRoot.Length..].TrimStart('/');
            return string.IsNullOrEmpty(sub) ? photosRoot : $"{photosRoot}/{sub}";
        }

        if (normalized.Equals("/home", StringComparison.Ordinal))
            return home;

        if (normalized.StartsWith("/home/", StringComparison.Ordinal))
        {
            var suffix = normalized["/home".Length..].TrimStart('/');
            return string.IsNullOrEmpty(suffix) ? home : $"{home}/{suffix}";
        }

        return normalized;
    }

    private async Task<IReadOnlyList<string>> BuildCandidatePathsAsync(string folder, CancellationToken cancellationToken)
    {
        var list = new List<string>();
        var photosRoot = await GetPhotosRootAsync(cancellationToken);
        var photosVirtual = await GetPhotosVirtualPathAsync(cancellationToken);

        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            p = NormalizeFolderPath(p);
            if (!list.Contains(p, StringComparer.Ordinal))
                list.Add(p);
        }

        var photoShareVirtual = await GetPhotoShareVirtualPathAsync(cancellationToken);
        var photoShareReal = await GetPhotoShareRealPathAsync(cancellationToken);

        // 优先共享 photo（/photo），再试 home/Photos
        Add(photoShareVirtual);
        Add(photosVirtual);
        Add(photosRoot);
        Add(photoShareReal);
        if (!folder.Equals(photosRoot, StringComparison.OrdinalIgnoreCase)
            && !folder.Equals(photosVirtual, StringComparison.OrdinalIgnoreCase)
            && !folder.Equals(photoShareReal, StringComparison.OrdinalIgnoreCase)
            && !folder.Equals(photoShareVirtual, StringComparison.OrdinalIgnoreCase))
            Add(folder);

        return list;
    }

    private async Task<string?> GetPhotoShareRealPathAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_cachedPhotoShareRealPath))
            return _cachedPhotoShareRealPath;

        await LoadPhotoSharePathsAsync(cancellationToken);
        return _cachedPhotoShareRealPath;
    }

    private async Task<string?> GetPhotoShareVirtualPathAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_cachedPhotoShareVirtualPath))
            return _cachedPhotoShareVirtualPath;

        await LoadPhotoSharePathsAsync(cancellationToken);
        return _cachedPhotoShareVirtualPath;
    }

    private async Task LoadPhotoSharePathsAsync(CancellationToken cancellationToken)
    {
        var additional = Uri.EscapeDataString("[\"real_path\"]");
        var version = _client.GetMaxApiVersion("SYNO.FileStation.List", 2);
        var url = _client.AppendSession(
            $"webapi/entry.cgi?api=SYNO.FileStation.List&version={version}&method=list_share&additional={additional}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        _client.ApplySynoTokenHeader(request);
        var response = await _client.HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return;

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("success", out var ok) || !ok.GetBoolean())
            return;
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("shares", out var shares))
            return;

        foreach (var share in shares.EnumerateArray())
        {
            if (!share.TryGetProperty("name", out var nameEl)
                || !nameEl.GetString()?.Equals("photo", StringComparison.OrdinalIgnoreCase) == true)
                continue;

            if (share.TryGetProperty("path", out var pathEl))
            {
                var virtualPath = pathEl.GetString()?.TrimEnd('/');
                if (!string.IsNullOrEmpty(virtualPath))
                    _cachedPhotoShareVirtualPath = virtualPath;
            }

            if (share.TryGetProperty("additional", out var additionalEl)
                && additionalEl.TryGetProperty("real_path", out var realPathEl))
            {
                var real = realPathEl.GetString()?.TrimEnd('/');
                if (!string.IsNullOrEmpty(real))
                    _cachedPhotoShareRealPath = real;
            }

            break;
        }
    }

    private async Task<string> GetPhotosRootAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_cachedPhotosRoot))
            return _cachedPhotosRoot;

        var home = await GetHomeSharePathAsync(cancellationToken);
        _cachedPhotosRoot = $"{home}/Photos";
        return _cachedPhotosRoot;
    }

    private async Task<string> GetPhotosVirtualPathAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_cachedPhotosVirtualPath))
            return _cachedPhotosVirtualPath;

        await GetHomeSharePathAsync(cancellationToken);
        var homeVirtual = _cachedHomeVirtualPath ?? DefaultPhotosRoot.TrimEnd('/').Replace("/Photos", "");
        _cachedPhotosVirtualPath = $"{homeVirtual}/Photos";
        return _cachedPhotosVirtualPath;
    }

    private async Task<string> GetHomeSharePathAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_cachedHomeRealPath))
            return _cachedHomeRealPath;

        var additional = Uri.EscapeDataString("[\"real_path\"]");
        var version = _client.GetMaxApiVersion("SYNO.FileStation.List", 2);
        var url = _client.AppendSession(
            $"webapi/entry.cgi?api=SYNO.FileStation.List&version={version}&method=list_share&additional={additional}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        _client.ApplySynoTokenHeader(request);
        var response = await _client.HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"list_share HTTP {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("success", out var ok) || !ok.GetBoolean())
        {
            var code = doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("code", out var c)
                ? c.GetInt32()
                : -1;
            throw new Exception($"list_share 失败，错误码 {code}");
        }

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("shares", out var shares))
            throw new Exception("list_share 无 shares 数据");

        string? homePath = null;
        foreach (var share in shares.EnumerateArray())
        {
            if (!share.TryGetProperty("name", out var nameEl) ||
                !share.TryGetProperty("path", out var pathEl))
                continue;
            var name = nameEl.GetString() ?? "";
            if (!name.Equals("home", StringComparison.OrdinalIgnoreCase))
                continue;

            var virtualPath = pathEl.GetString()?.TrimEnd('/') ?? "";
            _cachedHomeVirtualPath = string.IsNullOrEmpty(virtualPath) ? "/home" : virtualPath;

            homePath = _cachedHomeVirtualPath;
            if (share.TryGetProperty("additional", out var additionalEl)
                && additionalEl.TryGetProperty("real_path", out var realPathEl))
            {
                var real = realPathEl.GetString()?.TrimEnd('/');
                if (!string.IsNullOrEmpty(real))
                    homePath = real;
            }

            break;
        }

        if (string.IsNullOrEmpty(homePath))
            throw new Exception("未找到 home 共享文件夹，请在 DSM 中确认已启用个人主目录");

        _cachedHomeRealPath = homePath;
        return _cachedHomeRealPath;
    }

    private async Task<string> DescribeSharesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var version = _client.GetMaxApiVersion("SYNO.FileStation.List", 2);
            var url = _client.AppendSession(
                $"webapi/entry.cgi?api=SYNO.FileStation.List&version={version}&method=list_share");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            _client.ApplySynoTokenHeader(request);
            var response = await _client.HttpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("shares", out var shares))
                return "(无法列出)";

            var names = shares.EnumerateArray()
                .Select(s => s.TryGetProperty("path", out var p) ? p.GetString() : null)
                .Where(p => !string.IsNullOrEmpty(p))
                .Take(8);
            return string.Join(", ", names);
        }
        catch
        {
            return "(无法列出)";
        }
    }

    private static async Task<byte[]> ReadFileBytesAsync(Stream stream, long expectedSize, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment) && segment.Count == ms.Length)
            return segment.ToArray();

        var capacity = expectedSize > 0 && expectedSize < int.MaxValue
            ? (int)expectedSize
            : 0;
        using var buffer = new MemoryStream(capacity);
        if (stream.CanSeek)
            stream.Position = 0;
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string NormalizeFolderPath(string folderPath)
    {
        var p = folderPath.Trim().Replace('\\', '/');
        if (!p.StartsWith('/'))
            p = "/" + p;
        return p.TrimEnd('/');
    }

    private static void ParseSuccess(string body, string path)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("success", out var ok) && ok.GetBoolean())
            return;

        var code = root.TryGetProperty("error", out var err) && err.TryGetProperty("code", out var c)
            ? c.GetInt32()
            : -1;
        throw new Exception($"{DescribeFileStationError(code)}（path={path}）。响应：{Trim(body, 400)}");
    }

    private static string DescribeFileStationError(int code) => code switch
    {
        101 => "File Station 请求缺少 api/method/version（应放在 URL 查询参数中）",
        401 => "File Station 文件操作失败（路径无效、无写权限或会话非 FileStation，请重新登录）",
        403 => "File Station 无权限访问目标文件夹",
        1800 => "File Station 缺少或错误的 Content-Length",
        1802 => "File Station 未识别上传文件名",
        418 => "File Station 路径或文件名非法",
        _ => $"File Station 上传失败，错误码 {code}"
    };

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
