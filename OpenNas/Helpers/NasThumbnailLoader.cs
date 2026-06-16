using System.Collections.Concurrent;
using NSynology;
using NSynology.Foto;

namespace OpenNas.Helpers;

public static class NasThumbnailLoader
{
    private static readonly SemaphoreSlim ThumbnailGate = new(4, 4);
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> MemoryCache = new();

    public static void ClearMemoryCache() => MemoryCache.Clear();

    public static void TryLoadAlbumThumbnail(Image image, Album album)
    {
        image.Source = null;

        var thumb = album.Additional?.Thumbnail;
        if (thumb == null || thumb.UnitId <= 0 || string.IsNullOrEmpty(thumb.CacheKey))
            return;

        _ = LoadIntoImageAsync(image, thumb.UnitId, thumb.CacheKey);
    }

    public static void TryLoadPhotoThumbnail(Image image, Photo photo, Func<bool>? canApply = null)
    {
        var thumb = photo.Additional?.Thumbnail;
        if (thumb == null || string.IsNullOrEmpty(thumb.CacheKey))
        {
            if (canApply == null || canApply())
                image.Source = null;
            return;
        }

        var id = thumb.UnitId > 0 ? thumb.UnitId : photo.Id;
        if (id <= 0)
        {
            if (canApply == null || canApply())
                image.Source = null;
            return;
        }

        if (NasMediaCache.TryGetThumbnailFile(id, thumb.CacheKey, out var cachedPath))
        {
            if (canApply == null || canApply())
                image.Source = ImageSource.FromFile(cachedPath);
            return;
        }

        if (canApply == null || canApply())
            image.Source = null;
        _ = LoadIntoImageAsync(image, id, thumb.CacheKey, canApply);
    }

    private static async Task LoadIntoImageAsync(Image image, int id, string cacheKey, Func<bool>? canApply = null)
    {
        try
        {
            if (NasMediaCache.TryGetThumbnailFile(id, cacheKey, out var cachedPath))
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (canApply != null && !canApply())
                        return;
                    image.Source = ImageSource.FromFile(cachedPath);
                });
                return;
            }

            var key = $"{id}:{cacheKey}";
            var bytes = await MemoryCache.GetOrAdd(key, _ => DownloadAndCacheAsync(id, cacheKey))
                .ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0)
                return;

            var path = NasMediaCache.GetThumbnailFilePath(id, cacheKey);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (canApply != null && !canApply())
                    return;
                image.Source = ImageSource.FromFile(path);
            });
        }
        catch
        {
            // 缩略图失败时保留占位背景
        }
    }

    private static async Task<byte[]?> DownloadAndCacheAsync(int id, string cacheKey)
    {
        await ThumbnailGate.WaitAsync(CancellationToken.None);
        try
        {
            if (NasMediaCache.TryGetThumbnailFile(id, cacheKey, out var cachedPath))
                return await File.ReadAllBytesAsync(cachedPath);

            if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
                return null;

            await using var network = await SynologyManager.Client.Foto.GetThumbnailAsync(
                id, cacheKey, cancellationToken: CancellationToken.None);
            using var ms = new MemoryStream();
            await network.CopyToAsync(ms, CancellationToken.None);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                return null;

            await NasMediaCache.WriteThumbnailAsync(id, cacheKey, bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
        finally
        {
            ThumbnailGate.Release();
        }
    }
}
