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

    public async Task<Stream> GetThumbnailAsync(int id, string cacheKey, CancellationToken cancellationToken = default)
    {
        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Thumbnail&version=1&method=get&id={id}&size=sm&cache_key={cacheKey}&type=unit&{{0}}";
        return await _client.GetStreamAsync(url, cancellationToken);
    }

    public async Task<IEnumerable<Photo>> GetPhotosAsync(Album album, int offset, int limit, CancellationToken cancellationToken = default)
    {
        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Browse.Item&version=1&method=list&album_id={album.Id}&additional=[\"thumbnail\",\"resolution\",\"orientation\",\"video_convert\",\"video_meta\",\"provider_user_id\"]&offset={offset}&limit={limit}&{{0}}";
        var result = await _client.GetAsync<ListObject<Photo>>(url, cancellationToken);
        return result.List ?? Array.Empty<Photo>();
    }

    public async Task<Stream> GetDownloadPhotoAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        var url = $"{SynologyClient.DsmWebApiEntry}?api=SYNO.Foto.Download&version=1&method=download&unit_id=[{photo.Id}]&{{0}}";
        return await _client.GetStreamAsync(url, cancellationToken);
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
}
