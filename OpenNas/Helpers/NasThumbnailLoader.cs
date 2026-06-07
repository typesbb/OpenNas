using System.Collections.Concurrent;
using NSynology;
using NSynology.Foto;

namespace OpenNas.Helpers;

public static class NasThumbnailLoader
{
    private static readonly SemaphoreSlim ThumbnailGate = new(4, 4);
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> Cache = new();

    public static void TryLoadAlbumThumbnail(Image image, Album album)
    {
        if (image.Source != null)
            return;

        var thumb = album.Additional?.Thumbnail;
        if (thumb == null || thumb.UnitId <= 0 || string.IsNullOrEmpty(thumb.CacheKey))
            return;

        var unitId = thumb.UnitId;
        var cacheKey = thumb.CacheKey;
        image.Source = ImageSource.FromStream(_ => OpenCachedStreamAsync(unitId, cacheKey));
    }

    public static void TryLoadPhotoThumbnail(Image image, Photo photo)
    {
        if (image.Source != null)
            return;

        var thumb = photo.Additional?.Thumbnail;
        if (thumb == null || photo.Id <= 0 || string.IsNullOrEmpty(thumb.CacheKey))
            return;

        var id = photo.Id;
        var cacheKey = thumb.CacheKey;
        image.Source = ImageSource.FromStream(_ => OpenCachedStreamAsync(id, cacheKey));
    }

    private static async Task<Stream> OpenCachedStreamAsync(int id, string cacheKey)
    {
        var key = $"{id}:{cacheKey}";
        var bytes = await Cache.GetOrAdd(key, _ => DownloadBytesAsync(id, cacheKey)).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0)
            throw new InvalidOperationException("缩略图下载失败。");

        // Android Glide 对 .NET InputStreamAdapter 解码不稳定；写入临时文件再加载。
#if ANDROID
        var tempPath = Path.Combine(FileSystem.CacheDirectory, $"nas-thumb-{id}-{cacheKey.GetHashCode():x8}.jpg");
        await File.WriteAllBytesAsync(tempPath, bytes, CancellationToken.None);
        return File.OpenRead(tempPath);
#else
        return new MemoryStream(bytes, writable: false);
#endif
    }

    private static async Task<byte[]?> DownloadBytesAsync(int id, string cacheKey)
    {
        await ThumbnailGate.WaitAsync(CancellationToken.None);
        try
        {
            await using var network = await SynologyManager.Client.Foto.GetThumbnailAsync(
                id, cacheKey, CancellationToken.None);
            using var ms = new MemoryStream();
            await network.CopyToAsync(ms, CancellationToken.None);
            return ms.ToArray();
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
