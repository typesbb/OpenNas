using NSynology;
using NSynology.Foto;
using OpenNas.Services;

namespace OpenNas.Helpers;

internal static class NasFotoMediaApi
{
    public static Task<Stream> GetThumbnailAsync(
        SynologyClient client,
        int id,
        string cacheKey,
        string size = "sm",
        string type = "unit",
        int? albumId = null,
        string? passphrase = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedAlbumId = albumId ?? PhotosAlbumMediaScope.CurrentAlbumId;
        var resolvedPassphrase = passphrase ?? PhotosAlbumMediaScope.CurrentPassphrase;

        // 「与我共享」走 Foto + synofoto + passphrase，不是 FotoTeam 共享空间。
        if (!string.IsNullOrEmpty(resolvedPassphrase))
        {
            // HAR：unit + passphrase 时不带 album_id（仅 id + passphrase + size）。
            var sharedAlbumId = string.Equals(type, "unit", StringComparison.OrdinalIgnoreCase)
                ? null
                : resolvedAlbumId;
            return client.Foto.GetSynoFotoThumbnailAsync(
                id,
                cacheKey,
                size,
                type,
                sharedAlbumId,
                resolvedPassphrase,
                cancellationToken);
        }

        if (PhotosMediaLibraryScope.Current == PhotosLibrary.SharedSpace)
            return client.FotoTeam.GetThumbnailAsync(id, cacheKey, size, cancellationToken);

        if (resolvedAlbumId.HasValue)
        {
            return client.Foto.GetSynoFotoThumbnailAsync(
                id,
                cacheKey,
                size,
                type,
                resolvedAlbumId,
                null,
                cancellationToken);
        }

        return client.Foto.GetThumbnailAsync(id, cacheKey, size, cancellationToken);
    }

    public static Task<Stream> GetDownloadPhotoAsync(
        SynologyClient client,
        Photo photo,
        CancellationToken cancellationToken = default)
    {
        var albumId = PhotosAlbumMediaScope.CurrentAlbumId;
        var passphrase = PhotosAlbumMediaScope.CurrentPassphrase;

        if (!string.IsNullOrEmpty(passphrase))
        {
            // 浏览器照片大图走 synofoto xl；视频仍走 Download。
            if (!photo.IsVideo)
            {
                var thumb = photo.Additional?.Thumbnail;
                var id = thumb?.UnitId > 0 ? thumb!.UnitId : photo.Id;
                var cacheKey = thumb?.CacheKey ?? string.Empty;
                if (id > 0 && !string.IsNullOrEmpty(cacheKey))
                {
                    return client.Foto.GetSynoFotoThumbnailAsync(
                        id,
                        cacheKey,
                        "xl",
                        "unit",
                        albumId: null,
                        passphrase,
                        cancellationToken);
                }
            }

            return client.Foto.GetDownloadPhotoAsync(
                photo,
                albumId,
                passphrase,
                cancellationToken);
        }

        if (PhotosMediaLibraryScope.Current == PhotosLibrary.SharedSpace)
            return client.FotoTeam.GetDownloadPhotoAsync(photo, cancellationToken);

        return client.Foto.GetDownloadPhotoAsync(photo, albumId, null, cancellationToken);
    }

    public static string GetDownloadUrl(SynologyClient client, Photo photo)
    {
        var albumId = PhotosAlbumMediaScope.CurrentAlbumId;
        var passphrase = PhotosAlbumMediaScope.CurrentPassphrase;

        if (!string.IsNullOrEmpty(passphrase))
            return client.Foto.GetDownloadUrl(photo, albumId, passphrase);

        if (PhotosMediaLibraryScope.Current == PhotosLibrary.SharedSpace)
            return client.FotoTeam.GetDownloadUrl(photo);

        return client.Foto.GetDownloadUrl(photo, albumId, null);
    }
}
