using System.Collections.Concurrent;
using NSynology;
using NSynology.Foto;
using OpenNas.Services;

namespace OpenNas.Helpers;

public static class NasThumbnailLoader
{
#pragma warning disable CA1068 // CancellationToken ordering: forGrid mode switch comes last by design
    private static readonly SemaphoreSlim ThumbnailGate = new(2, 2);
    private const int MaxMemoryCacheEntries = 200;
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> MemoryCache = new();
    private static readonly ConcurrentQueue<string> MemoryCacheOrder = new();

    public static void ClearMemoryCache() => MemoryCache.Clear();

    private static void TrimMemoryCache()
    {
        while (MemoryCache.Count > MaxMemoryCacheEntries && MemoryCacheOrder.TryDequeue(out var oldKey))
            MemoryCache.TryRemove(oldKey, out _);
    }

    public static void TryLoadAlbumThumbnail(Image image, Album album)
    {
        image.Source = null;

        var thumb = album.Additional?.Thumbnail;
        if (thumb == null || string.IsNullOrEmpty(thumb.CacheKey))
            return;

        if (!string.IsNullOrEmpty(AlbumShareHelper.ResolvePassphrase(album)))
        {
            if (album.Id <= 0)
                return;

            _ = LoadIntoImageAsync(
                new WeakReference<Image>(image),
                album.Id,
                thumb.CacheKey,
                type: "album",
                passphrase: AlbumShareHelper.ResolvePassphrase(album));
            return;
        }

        if (thumb.UnitId <= 0)
            return;

        _ = LoadIntoImageAsync(new WeakReference<Image>(image), thumb.UnitId, thumb.CacheKey);
    }

    public static void TryLoadBrowseItemThumbnail(Image image, BrowseAlbumItem item)
    {
        image.Source = null;
        var thumb = item.Additional?.Thumbnail;
        var unitId = item.Cover > 0 ? item.Cover : thumb?.UnitId ?? 0;
        var cacheKey = thumb?.CacheKey;
        if (unitId <= 0 || string.IsNullOrEmpty(cacheKey))
            return;

        _ = LoadIntoImageAsync(new WeakReference<Image>(image), unitId, cacheKey);
    }

    public static Task EnsureCachedAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        var thumb = photo.Additional?.Thumbnail;
        if (thumb == null || string.IsNullOrEmpty(thumb.CacheKey))
            return Task.CompletedTask;

        var id = thumb.UnitId > 0 ? thumb.UnitId : photo.Id;
        if (id <= 0)
            return Task.CompletedTask;

        if (NasMediaCache.TryGetThumbnailFile(id, thumb.CacheKey, out _))
            return Task.CompletedTask;

        var resolvedAlbumId = PhotosAlbumMediaScope.CurrentAlbumId;
        var resolvedPassphrase = PhotosAlbumMediaScope.CurrentPassphrase;
        var key = BuildMemoryCacheKey(id, thumb.CacheKey, "unit", resolvedAlbumId, resolvedPassphrase);
        if (MemoryCache.TryGetValue(key, out var existing))
            return existing;

        return MemoryCache.GetOrAdd(
                key,
                _ => DownloadAndCacheAsync(
                    id,
                    thumb.CacheKey,
                    cancellationToken,
                    albumId: resolvedAlbumId,
                    passphrase: resolvedPassphrase))
            .WaitAsync(cancellationToken);
    }

    /// <param name="forGrid">相册网格：推迟到 bind 栈外，滚动中不做磁盘/网络/贴图。</param>
    public static void TryLoadPhotoThumbnail(
        Image image,
        Photo photo,
        Func<bool>? canApply = null,
        CancellationToken cancellationToken = default,
        bool forGrid = false)
    {
        if (forGrid)
        {
            NasGridImageApplyScheduler.ScheduleLoad(() =>
                StartPhotoThumbnailLoad(new WeakReference<Image>(image), photo, canApply, cancellationToken, forGrid: true));
            return;
        }

        StartPhotoThumbnailLoad(new WeakReference<Image>(image), photo, canApply, cancellationToken, forGrid);
    }

    /// <summary>已由网格控件 ScheduleLoad 后调用，避免重复入队。</summary>
    internal static void TryLoadPhotoThumbnailDirect(
        Image image,
        Photo photo,
        Func<bool>? canApply = null,
        CancellationToken cancellationToken = default)
    {
        StartPhotoThumbnailLoad(new WeakReference<Image>(image), photo, canApply, cancellationToken, forGrid: true);
    }

    private static void StartPhotoThumbnailLoad(
        WeakReference<Image> target,
        Photo photo,
        Func<bool>? canApply,
        CancellationToken cancellationToken,
        bool forGrid)
    {
        var thumb = photo.Additional?.Thumbnail;
        if (thumb == null || string.IsNullOrEmpty(thumb.CacheKey))
        {
            if (forGrid)
                ScheduleApplySource(target, null, canApply, cancellationToken, forGrid: true);
            return;
        }

        var id = thumb.UnitId > 0 ? thumb.UnitId : photo.Id;
        if (id <= 0)
        {
            if (forGrid)
                ScheduleApplySource(target, null, canApply, cancellationToken, forGrid: true);
            return;
        }

        _ = LoadPhotoThumbnailIntoImageAsync(target, photo, id, thumb.CacheKey, canApply, cancellationToken, forGrid);
    }

    private static async Task LoadPhotoThumbnailIntoImageAsync(
        WeakReference<Image> target,
        Photo photo,
        int id,
        string cacheKey,
        Func<bool>? canApply,
        CancellationToken cancellationToken,
        bool forGrid)
    {
        try
        {
            var resolvedAlbumId = PhotosAlbumMediaScope.CurrentAlbumId;
            var resolvedPassphrase = PhotosAlbumMediaScope.CurrentPassphrase;

            if (forGrid && NasGridImageApplyScheduler.IsScrolling)
            {
                NasGridImageApplyScheduler.RunWhenIdle(() =>
                    _ = LoadPhotoThumbnailIntoImageAsync(
                        target, photo, id, cacheKey, canApply, cancellationToken, forGrid: true));
                return;
            }

            byte[]? bytes = null;
            var cachedPath = await Task.Run(() =>
                NasMediaCache.TryGetThumbnailFile(id, cacheKey, out var path) ? path : null,
                cancellationToken).ConfigureAwait(false);

            if (cachedPath != null)
            {
                bytes = await File.ReadAllBytesAsync(cachedPath, cancellationToken).ConfigureAwait(false);
                if (!NasThumbnailBytes.IsLikelyPlaceholder(bytes))
                {
                    ScheduleApplyBytes(target, bytes, canApply, cancellationToken, forGrid);
                    return;
                }

                NasMediaCache.TryInvalidateThumbnail(id, cacheKey);
            }

            if (forGrid && NasGridImageApplyScheduler.IsScrolling)
            {
                NasGridImageApplyScheduler.RunWhenIdle(() =>
                    _ = LoadPhotoThumbnailIntoImageAsync(
                        target, photo, id, cacheKey, canApply, cancellationToken, forGrid: true));
                return;
            }

            if (forGrid)
                ScheduleApplySource(target, null, canApply, cancellationToken, forGrid);

            bytes ??= await DownloadThumbnailBytesAsync(
                    id, cacheKey, cancellationToken, resolvedAlbumId, resolvedPassphrase)
                .ConfigureAwait(false);

            if (bytes == null || bytes.Length == 0 || cancellationToken.IsCancellationRequested)
                return;

            if (NasThumbnailBytes.IsLikelyPlaceholder(bytes))
                return;

            ScheduleApplyBytes(target, bytes, canApply, cancellationToken, forGrid);
        }
        catch (OperationCanceledException)
        {
            // cell 复用或页面离开
        }
        catch (Exception ex)
        {
            AppLog.Debug("缩略图加载失败", ex);
        }
    }

    private static void ClearThumbnailMemoryCacheEntry(int id, string cacheKey)
    {
        var albumId = PhotosAlbumMediaScope.CurrentAlbumId;
        var passphrase = PhotosAlbumMediaScope.CurrentPassphrase;
        MemoryCache.TryRemove(BuildMemoryCacheKey(id, cacheKey, "unit", albumId, passphrase), out _);
    }

    private static async Task<byte[]?> DownloadThumbnailBytesAsync(
        int id,
        string cacheKey,
        CancellationToken cancellationToken,
        int? albumId,
        string? passphrase)
    {
        var key = BuildMemoryCacheKey(id, cacheKey, "unit", albumId, passphrase);
        var bytes = await MemoryCache
            .GetOrAdd(
                key,
                _ => DownloadAndCacheAsync(id, cacheKey, cancellationToken, "unit", albumId, passphrase))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        MemoryCacheOrder.Enqueue(key);
        TrimMemoryCache();
        return bytes;
    }

    private static async Task LoadIntoImageAsync(
        WeakReference<Image> target,
        int id,
        string cacheKey,
        Func<bool>? canApply = null,
        CancellationToken cancellationToken = default,
        bool forGrid = false,
        string type = "unit",
        int? albumId = null,
        string? passphrase = null)
    {
        try
        {
            var resolvedAlbumId = albumId ?? PhotosAlbumMediaScope.CurrentAlbumId;
            var resolvedPassphrase = passphrase ?? PhotosAlbumMediaScope.CurrentPassphrase;

            if (forGrid && NasGridImageApplyScheduler.IsScrolling)
            {
                NasGridImageApplyScheduler.RunWhenIdle(() =>
                    _ = LoadIntoImageAsync(
                        target, id, cacheKey, canApply, cancellationToken, forGrid: true,
                        type, resolvedAlbumId, resolvedPassphrase));
                return;
            }

            var cachedPath = await Task.Run(() =>
                NasMediaCache.TryGetThumbnailFile(id, cacheKey, out var path) ? path : null,
                cancellationToken).ConfigureAwait(false);

            if (cachedPath != null)
            {
                ScheduleApplyFile(target, cachedPath, canApply, cancellationToken, forGrid);
                return;
            }

            if (forGrid && NasGridImageApplyScheduler.IsScrolling)
            {
                NasGridImageApplyScheduler.RunWhenIdle(() =>
                    _ = LoadIntoImageAsync(
                        target, id, cacheKey, canApply, cancellationToken, forGrid: true,
                        type, resolvedAlbumId, resolvedPassphrase));
                return;
            }

            if (forGrid)
                ScheduleApplySource(target, null, canApply, cancellationToken, forGrid);

            var bytes = await DownloadThumbnailBytesAsync(
                    id, cacheKey, cancellationToken, resolvedAlbumId, resolvedPassphrase)
                .ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0 || cancellationToken.IsCancellationRequested)
                return;

            var path = NasMediaCache.GetThumbnailFilePath(id, cacheKey);
            ScheduleApplyFile(target, path, canApply, cancellationToken, forGrid);
        }
        catch (OperationCanceledException)
        {
            // cell 复用或页面离开
        }
        catch (Exception ex)
        {
            AppLog.Debug("缩略图加载失败", ex);
        }
    }

    private static void ScheduleApplyBytes(
        WeakReference<Image> target,
        byte[] bytes,
        Func<bool>? canApply,
        CancellationToken cancellationToken,
        bool forGrid)
    {
        if (cancellationToken.IsCancellationRequested || bytes.Length == 0)
            return;

        var payload = bytes.ToArray();

        void Apply()
        {
            if (cancellationToken.IsCancellationRequested || canApply != null && !canApply())
                return;
            if (!target.TryGetTarget(out var image))
                return;

            image.Source = ImageSource.FromStream(() => new MemoryStream(payload));
        }

        if (forGrid)
            NasGridImageApplyScheduler.RunWhenIdle(Apply);
        else
            MainThread.BeginInvokeOnMainThread(Apply);
    }

    private static void ScheduleApplyFile(
        WeakReference<Image> target,
        string path,
        Func<bool>? canApply,
        CancellationToken cancellationToken,
        bool forGrid)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        void Apply()
        {
            if (cancellationToken.IsCancellationRequested || canApply != null && !canApply())
                return;
            if (!target.TryGetTarget(out var image))
                return;
            if (!File.Exists(path))
                return;

            image.Source = ImageSource.FromFile(path);
        }

        if (forGrid)
            NasGridImageApplyScheduler.RunWhenIdle(Apply);
        else
            MainThread.BeginInvokeOnMainThread(Apply);
    }

    private static void ScheduleApplySource(
        WeakReference<Image> target,
        ImageSource? source,
        Func<bool>? canApply,
        CancellationToken cancellationToken,
        bool forGrid)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        void Apply()
        {
            if (cancellationToken.IsCancellationRequested || canApply != null && !canApply())
                return;
            if (!target.TryGetTarget(out var image))
                return;

            image.Source = source;
        }

        if (forGrid)
            NasGridImageApplyScheduler.RunWhenIdle(Apply);
        else
            MainThread.BeginInvokeOnMainThread(Apply);
    }

    private static string BuildMemoryCacheKey(
        int id,
        string cacheKey,
        string type,
        int? albumId,
        string? passphrase) =>
        $"{type}:{id}:{cacheKey}:{albumId}:{passphrase}";

    private static async Task<byte[]?> DownloadAndCacheAsync(
        int id,
        string cacheKey,
        CancellationToken cancellationToken,
        string type = "unit",
        int? albumId = null,
        string? passphrase = null)
    {
        await ThumbnailGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (NasMediaCache.TryGetThumbnailFile(id, cacheKey, out var cachedPath))
                return await File.ReadAllBytesAsync(cachedPath, cancellationToken).ConfigureAwait(false);

            if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
                return null;

            await using var network = await NasFotoMediaApi.GetThumbnailAsync(
                SynologyManager.Client,
                id,
                cacheKey,
                type: type,
                albumId: albumId,
                passphrase: passphrase,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            using var ms = new MemoryStream();
            await network.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                return null;

            await NasMediaCache.WriteThumbnailAsync(id, cacheKey, bytes, cancellationToken).ConfigureAwait(false);
            MemoryCacheOrder.Enqueue(BuildMemoryCacheKey(id, cacheKey, type, albumId, passphrase));
            TrimMemoryCache();
            return bytes;
        }
        catch (SynologyApiException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Debug("缩略图下载失败", ex);
            return null;
        }
        finally
        {
            ThumbnailGate.Release();
            var resolvedAlbumId = albumId ?? PhotosAlbumMediaScope.CurrentAlbumId;
            var resolvedPassphrase = passphrase ?? PhotosAlbumMediaScope.CurrentPassphrase;
            MemoryCache.TryRemove(
                BuildMemoryCacheKey(id, cacheKey, type, resolvedAlbumId, resolvedPassphrase),
                out _);
        }
    }
}
