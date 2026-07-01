namespace NSynology.Foto;

using System.Text.Json;

public class FotoApi(SynologyClient synologyClient) : ApiBase
{
    private readonly SynologyClient _client = synologyClient;

    /// <summary>SAZ 1421：官方 App 列相册（Cookie + POST form，无 <c>_sid</c>）。</summary>
    public async Task<IEnumerable<Album>> ListAppAlbumsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var parsed = await _client.PostAppFormAsync<ListObject<Album>>(
            "SYNO.Foto.Browse.Album",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Album", 4),
            "list",
            [
                new KeyValuePair<string, string>("offset", offset.ToString()),
                new KeyValuePair<string, string>("limit", limit.ToString()),
                new KeyValuePair<string, string>("category", "\"all\""),
                new KeyValuePair<string, string>("additional", AppCapture.BrowseAlbumListAdditional),
                new KeyValuePair<string, string>("accept_language", "chs")
            ],
            cancellationToken);
        return parsed?.List ?? Array.Empty<Album>();
    }

    public async Task<IEnumerable<Album>> GetAlbumsAsync(int offset, int limit, CancellationToken cancellationToken = default) =>
        await ListAppAlbumsAsync(offset, limit, cancellationToken);

    public async Task<IEnumerable<Album>> GetAppAlbumsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        await ListAppAlbumsAsync(offset, limit, cancellationToken);

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

        var parsed = await _client.PostAppFormAsync<ListObject<Photo>>(
            "SYNO.Foto.Browse.Item",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Item", 5),
            "list",
            [
                new KeyValuePair<string, string>("offset", offset.ToString()),
                new KeyValuePair<string, string>("limit", limit.ToString()),
                new KeyValuePair<string, string>("album_id", album.Id.ToString()),
                new KeyValuePair<string, string>("sort_by", $"\"{apiSortBy}\""),
                new KeyValuePair<string, string>("sort_direction", $"\"{direction}\""),
                new KeyValuePair<string, string>("additional", AppCapture.BrowseItemListAdditional),
                new KeyValuePair<string, string>("geocoding_accept_language", "chs")
            ],
            cancellationToken);
        return parsed?.List ?? Array.Empty<Photo>();
    }

    private static string MapSortField(string sortField) => sortField switch
    {
        "name" => "filename",
        "size" => "filesize",
        _ => "takentime"
    };

    public async Task<Stream> GetDownloadPhotoAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        var version = _client.GetMaxApiVersion("SYNO.Foto.Download", 1);
        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Download&version={version}&method=download&unit_id=[{photo.Id}]&{{0}}";
        return await _client.GetStreamAsync(url, cancellationToken);
    }

    /// <summary>带会话参数的原始文件下载地址，供视频流式播放。</summary>
    public string GetDownloadUrl(Photo photo)
    {
        var version = _client.GetMaxApiVersion("SYNO.Foto.Download", 1);
        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Download&version={version}&method=download&unit_id=[{photo.Id}]&{{0}}";
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
