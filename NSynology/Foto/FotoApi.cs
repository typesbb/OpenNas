namespace NSynology.Foto;

public class FotoApi(SynologyClient synologyClient) : ApiBase
{
    private readonly SynologyClient _client = synologyClient;

    /// <summary>SAZ 1421：官方 App 列相册（Cookie + POST form，无 <c>_sid</c>）。</summary>
    public async Task<IEnumerable<Album>> ListOfficialAlbumsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var parsed = await _client.PostOfficialAppFormAsync<ListObject<Album>>(
            "SYNO.Foto.Browse.Album",
            4,
            "list",
            [
                new KeyValuePair<string, string>("offset", offset.ToString()),
                new KeyValuePair<string, string>("limit", limit.ToString()),
                new KeyValuePair<string, string>("category", "\"all\""),
                new KeyValuePair<string, string>("additional", OfficialAppCapture.BrowseAlbumListAdditional),
                new KeyValuePair<string, string>("accept_language", "chs")
            ],
            cancellationToken);
        return parsed?.List ?? Array.Empty<Album>();
    }

    public async Task<IEnumerable<Album>> GetAlbumsAsync(int offset, int limit, CancellationToken cancellationToken = default) =>
        await ListOfficialAlbumsAsync(offset, limit, cancellationToken);

    public async Task<IEnumerable<Album>> GetOfficialAlbumsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        await ListOfficialAlbumsAsync(offset, limit, cancellationToken);

    public async Task<Album> CreateNormalAlbumAsync(string name, CancellationToken cancellationToken = default)
    {
        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Browse.NormalAlbum&version=2&method=create&name={Uri.EscapeDataString(name)}&{{0}}";
        var result = await _client.GetAsync<AlbumObject>(url, cancellationToken);
        return result.Album;
    }

    public async Task<Stream> GetThumbnailAsync(
        int id,
        string cacheKey,
        string size = "sm",
        CancellationToken cancellationToken = default)
    {
        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Thumbnail&version=1&method=get&id={id}&size={size}&cache_key={cacheKey}&type=unit&{{0}}";
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

        var parsed = await _client.PostOfficialAppFormAsync<ListObject<Photo>>(
            "SYNO.Foto.Browse.Item",
            5,
            "list",
            [
                new KeyValuePair<string, string>("offset", offset.ToString()),
                new KeyValuePair<string, string>("limit", limit.ToString()),
                new KeyValuePair<string, string>("album_id", album.Id.ToString()),
                new KeyValuePair<string, string>("sort_by", $"\"{apiSortBy}\""),
                new KeyValuePair<string, string>("sort_direction", $"\"{direction}\""),
                new KeyValuePair<string, string>("additional", OfficialAppCapture.BrowseItemListAdditional),
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
        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Download&version=1&method=download&unit_id=[{photo.Id}]&{{0}}";
        return await _client.GetStreamAsync(url, cancellationToken);
    }

    /// <summary>带会话参数的原始文件下载地址，供视频流式播放。</summary>
    public string GetDownloadUrl(Photo photo)
    {
        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Download&version=1&method=download&unit_id=[{photo.Id}]&{{0}}";
        return _client.BuildApiUri(_client.BuildAuthenticatedApiUrl(url)).AbsoluteUri;
    }

    /// <summary>备份开始前按相册预热官方 App 会话（每相册每轮仅一次）。</summary>
    public Task WarmupAlbumForBackupAsync(int albumId, CancellationToken cancellationToken = default) =>
        _client.WarmupOfficialAlbumBeforeUploadAsync(albumId, cancellationToken);

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
        _client.UploadItemOfficialAlbumAsync(
            openStream,
            fileName,
            mimeType,
            albumId,
            fileSize,
            dateModifiedUnix,
            uploadProgress,
            cancellationToken);

    public Task<UploadResult> UploadToAlbumFromBytesAsync(
        byte[] fileBytes,
        string fileName,
        string mimeType,
        int albumId,
        long fileSize = 0,
        long dateModifiedUnix = 0,
        IProgress<double>? uploadProgress = null,
        CancellationToken cancellationToken = default) =>
        _client.UploadItemOfficialAlbumFromBytesAsync(
            fileBytes,
            fileName,
            mimeType,
            albumId,
            fileSize,
            dateModifiedUnix,
            uploadProgress,
            cancellationToken);
}
