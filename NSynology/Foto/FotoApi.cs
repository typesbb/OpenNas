namespace NSynology.Foto;

using System.Text.Json;

public class FotoApi(SynologyClient synologyClient) : ApiBase
{
    private readonly SynologyClient _client = synologyClient;

    /// <summary>SAZ 1421：官方 App 列相册（Cookie + POST form，无 <c>_sid</c>）。</summary>
    public async Task<IEnumerable<Album>> ListAppAlbumsAsync(
        int offset,
        int limit,
        string category = "all",
        string sortBy = "start_time",
        string sortDirection = "desc",
        CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("offset", offset.ToString()),
            new("limit", limit.ToString()),
            new("category", $"\"{category}\""),
            new("accept_language", "chs")
        };

        // SAZ 1421：category=all 不支持 sort_by/sort_direction，传参会触发 code=120。
        if (string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
            fields.Add(new KeyValuePair<string, string>("additional", AppCapture.BrowseAlbumListAdditional));
        else
        {
            fields.Add(new KeyValuePair<string, string>("sort_by", $"\"{sortBy}\""));
            fields.Add(new KeyValuePair<string, string>("sort_direction", $"\"{sortDirection}\""));
            fields.Add(new KeyValuePair<string, string>("additional", "[\"sharing_info\",\"thumbnail\"]"));
        }

        var parsed = await _client.PostAppFormAsync<ListObject<Album>>(
            "SYNO.Foto.Browse.Album",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Album", 4),
            "list",
            fields,
            cancellationToken);
        return parsed?.List ?? Array.Empty<Album>();
    }

    public async Task<IReadOnlyList<PhotoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var parsed = await _client.PostAppFormAsync<ListObject<PhotoCategory>>(
            "SYNO.Foto.Browse.Category",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Category", 2),
            "get",
            [],
            cancellationToken);
        return parsed?.List?.ToList() ?? [];
    }

    public Task<TeamSpaceSettings?> GetTeamSpaceSettingsAsync(CancellationToken cancellationToken = default) =>
        _client.PostAppFormAsync<TeamSpaceSettings>(
            "SYNO.Foto.Setting.TeamSpace",
            _client.GetMaxApiVersion("SYNO.Foto.Setting.TeamSpace", 1),
            "get",
            [],
            cancellationToken);

    public Task<AlbumListOrder?> GetAlbumListOrderAsync(CancellationToken cancellationToken = default) =>
        _client.PostAppFormAsync<AlbumListOrder>(
            "SYNO.Foto.Browse.Album",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Album", 2),
            "get_album_list_order",
            [],
            cancellationToken);

    public Task SetAlbumListOrderAsync(AlbumListOrder order, CancellationToken cancellationToken = default) =>
        _client.PostAppFormAsync(
            "SYNO.Foto.Browse.Album",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Album", 2),
            "set_album_list_order",
            [
                new KeyValuePair<string, string>("album_list_sort_by", $"\"{order.AlbumListSortBy}\""),
                new KeyValuePair<string, string>("album_list_sort_direction", $"\"{order.AlbumListSortDirection}\""),
                new KeyValuePair<string, string>("shared_with_me_sort_by", $"\"{order.SharedWithMeSortBy}\""),
                new KeyValuePair<string, string>("shared_with_me_sort_direction", $"\"{order.SharedWithMeSortDirection}\"")
            ],
            cancellationToken);

    public Task<AlbumListDisplay?> GetAlbumListDisplayAsync(CancellationToken cancellationToken = default) =>
        _client.PostAppFormAsync<AlbumListDisplay>(
            "SYNO.Foto.Browse.Album",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Album", 3),
            "get_album_list_display",
            [],
            cancellationToken);

    public Task SetAlbumListDisplayAsync(string displayType, CancellationToken cancellationToken = default) =>
        _client.PostAppFormAsync(
            "SYNO.Foto.Browse.Album",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Album", 3),
            "set_album_list_display",
            [new KeyValuePair<string, string>("album_display_type", $"\"{displayType}\"")],
            cancellationToken);

    /// <summary>SAZ：与我共享相册走 Sharing.Misc，不是 Browse.Album category。</summary>
    public async Task<IEnumerable<Album>> ListSharedWithMeAlbumsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var parsed = await _client.PostAppFormAsync<ListObject<Album>>(
            "SYNO.Foto.Sharing.Misc",
            _client.GetMaxApiVersion("SYNO.Foto.Sharing.Misc", 1),
            "list_shared_with_me_album",
            [
                new KeyValuePair<string, string>("offset", offset.ToString()),
                new KeyValuePair<string, string>("limit", limit.ToString()),
                new KeyValuePair<string, string>("additional", "[\"thumbnail\",\"sharing_info\",\"provider_count\",\"access_permission\"]")
            ],
            cancellationToken);
        return parsed?.List ?? Array.Empty<Album>();
    }

    public async Task<IEnumerable<Album>> GetAlbumsAsync(
        int offset,
        int limit,
        string category = "all",
        string sortBy = "start_time",
        string sortDirection = "desc",
        CancellationToken cancellationToken = default) =>
        await ListAppAlbumsAsync(offset, limit, category, sortBy, sortDirection, cancellationToken);

    public async Task<IEnumerable<Album>> GetAppAlbumsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        await ListAppAlbumsAsync(offset, limit, cancellationToken: cancellationToken);

    public async Task<Album> CreateNormalAlbumAsync(string name, CancellationToken cancellationToken = default)
    {
        var version = _client.GetMaxApiVersion("SYNO.Foto.Browse.NormalAlbum", 2);
        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Browse.NormalAlbum&version={version}&method=create&name={Uri.EscapeDataString(name)}&{{0}}";
        var result = await _client.GetAsync<AlbumObject>(url, cancellationToken);
        return result.Album;
    }

    /// <summary>重命名相册。</summary>
    public async Task<Album> RenameAlbumAsync(int id, string name, CancellationToken cancellationToken = default)
    {
        var version = _client.GetMaxApiVersion("SYNO.Foto.Browse.Album", 1);
        var result = await _client.PostAppFormAsync<AlbumObject>(
            "SYNO.Foto.Browse.Album",
            version,
            "set_name",
            [
                new KeyValuePair<string, string>("id", id.ToString()),
                new KeyValuePair<string, string>("name", name)
            ],
            cancellationToken);
        return result?.Album ?? new Album { Id = id, Name = name };
    }

    /// <summary>删除相册（不可恢复）。</summary>
    public async Task DeleteAlbumAsync(int id, CancellationToken cancellationToken = default)
    {
        var version = _client.GetMaxApiVersion("SYNO.Foto.Browse.Album", 1);
        await _client.PostAppFormAsync(
            "SYNO.Foto.Browse.Album",
            version,
            "delete",
            [new KeyValuePair<string, string>("id", JsonSerializer.Serialize(new[] { id }))],
            cancellationToken);
    }

    public async Task<Stream> GetThumbnailAsync(
        int id,
        string cacheKey,
        string size = "sm",
        CancellationToken cancellationToken = default)
    {
        var version = _client.GetMaxApiVersion("SYNO.Foto.Thumbnail", 1);
        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Thumbnail&version={version}&method=get&id={id}&size={size}&cache_key={cacheKey}&type=unit&{{0}}";
        return await _client.GetStreamAsync(url, cancellationToken);
    }

    /// <summary>官方 App 抓包：<c>/synofoto/api/v2/p/Thumbnail/get</c>（与我共享相册需 passphrase / album_id）。</summary>
    public Task<Stream> GetSynoFotoThumbnailAsync(
        int id,
        string cacheKey,
        string size = "sm",
        string type = "unit",
        int? albumId = null,
        string? passphrase = null,
        CancellationToken cancellationToken = default)
    {
        var query = BuildSynoFotoThumbnailQuery(id, cacheKey, size, type, albumId, passphrase);
        return _client.GetCookieAuthenticatedStreamAsync($"/synofoto/api/v2/p/Thumbnail/get?{query}", cancellationToken);
    }

    internal static string BuildSynoFotoThumbnailQuery(
        int id,
        string cacheKey,
        string size,
        string type,
        int? albumId,
        string? passphrase)
    {
        var parts = new List<string>
        {
            $"id={id}",
            $"type={QuoteJsonString(type)}",
            $"size={QuoteJsonString(size)}",
            $"cache_key={QuoteJsonString(cacheKey)}"
        };
        if (albumId.HasValue && string.IsNullOrEmpty(passphrase))
            parts.Add($"album_id={albumId.Value}");
        if (!string.IsNullOrEmpty(passphrase))
            parts.Add($"passphrase={QuoteJsonString(passphrase)}");
        return string.Join('&', parts);
    }

    private static string QuoteJsonString(string value) =>
        Uri.EscapeDataString($"\"{value}\"");

    public async Task<IEnumerable<Photo>> GetPhotosAsync(
        Album album,
        int offset,
        int limit,
        string sortField = "time",
        bool sortDescending = true,
        CancellationToken cancellationToken = default)
    {
        var apiSortBy = MapSortField(sortField);
        var direction = sortDescending ? "desc" : "asc";
        var passphrase = album.ResolveSharePassphrase();
        var fields = new List<KeyValuePair<string, string>>
        {
            new("offset", offset.ToString()),
            new("limit", limit.ToString()),
            new("sort_by", $"\"{apiSortBy}\""),
            new("sort_direction", $"\"{direction}\""),
            new("additional", AppCapture.BrowseItemListAdditional),
            new("geocoding_accept_language", "chs")
        };

        // 与我共享 list：v5 用 album id + JSON 引号 passphrase（浏览器/HAR）；不用 album_id。
        if (!string.IsNullOrEmpty(passphrase))
        {
            fields.Add(new KeyValuePair<string, string>("id", album.Id.ToString()));
            fields.Add(new KeyValuePair<string, string>("passphrase", $"\"{passphrase}\""));
        }
        else
        {
            fields.Add(new KeyValuePair<string, string>("album_id", album.Id.ToString()));
        }

        var parsed = await _client.PostAppFormAsync<ListObject<Photo>>(
            "SYNO.Foto.Browse.Item",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Item", 5),
            "list",
            fields,
            cancellationToken);
        return parsed?.List ?? Array.Empty<Photo>();
    }

    private static string MapSortField(string sortField) => sortField switch
    {
        "name" => "filename",
        "size" => "filesize",
        _ => "takentime"
    };

    public async Task<Stream> GetDownloadPhotoAsync(
        Photo photo,
        int? albumId = null,
        string? passphrase = null,
        CancellationToken cancellationToken = default)
    {
        var version = _client.GetMaxApiVersion("SYNO.Foto.Download", 1);
        var url =
            $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Download&version={version}&method=download&unit_id=[{photo.Id}]";
        if (albumId.HasValue)
            url += $"&album_id={albumId.Value}";
        if (!string.IsNullOrEmpty(passphrase))
            url += $"&passphrase={Uri.EscapeDataString(passphrase)}";
        url += "&{0}";
        return await _client.GetStreamAsync(url, cancellationToken);
    }

    /// <summary>带会话参数的原始文件下载地址，供视频流式播放。</summary>
    public string GetDownloadUrl(Photo photo, int? albumId = null, string? passphrase = null)
    {
        var version = _client.GetMaxApiVersion("SYNO.Foto.Download", 1);
        var url =
            $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Download&version={version}&method=download&unit_id=[{photo.Id}]";
        if (albumId.HasValue)
            url += $"&album_id={albumId.Value}";
        if (!string.IsNullOrEmpty(passphrase))
            url += $"&passphrase={Uri.EscapeDataString(passphrase)}";
        url += "&{0}";
        return _client.BuildApiUri(_client.BuildAuthenticatedApiUrl(url)).AbsoluteUri;
    }


    /// <summary>
    /// 仅官方 Synology Photos Android App 抓包路径：<c>SYNO.Foto.Upload.Item v5 upload</c> + <c>album_id</c>。
    /// </summary>
    public Task<UploadResult> UploadToAlbumAsync(
        UploadStreamFactory openStream,
        string fileName,
        string mimeType,
        int albumId,
        long fileSize = 0,
        long dateModifiedUnix = 0,
        IProgress<double>? uploadProgress = null,
        string? remoteAlbumName = null,
        CancellationToken cancellationToken = default) =>
        _client.UploadItemAppAlbumAsync(
            openStream,
            fileName,
            mimeType,
            albumId,
            fileSize,
            dateModifiedUnix,
            uploadProgress,
            cancellationToken);

    /// <summary>向指定相册批量添加照片。</summary>
    public async Task<bool> AddPhotosToAlbumAsync(
        int albumId,
        IEnumerable<Photo> photos,
        CancellationToken cancellationToken = default)
    {
        return await _client.AddPhotosToNormalAlbumAsync(
            albumId, photos.Select(p => p.Id), cancellationToken);
    }

    /// <summary>从相册移除照片（仅移除关联，不删除文件）。</summary>
    public async Task<bool> RemovePhotosFromAlbumAsync(
        int albumId,
        IEnumerable<Photo> photos,
        CancellationToken cancellationToken = default)
    {
        return await _client.RemovePhotosFromNormalAlbumAsync(
            albumId, photos.Select(p => p.Id), cancellationToken);
    }

    public Task<UploadResult> UploadToAlbumFromBytesAsync(
        byte[] fileBytes,
        string fileName,
        string mimeType,
        int albumId,
        long fileSize = 0,
        long dateModifiedUnix = 0,
        IProgress<double>? uploadProgress = null,
        CancellationToken cancellationToken = default) =>
        _client.UploadItemAppAlbumFromBytesAsync(
            fileBytes,
            fileName,
            mimeType,
            albumId,
            fileSize,
            dateModifiedUnix,
            uploadProgress,
            cancellationToken);
}
