using System.Collections.Concurrent;
using NSynology;
using NSynology.Foto;
using OpenNas.Services;

namespace OpenNas.Helpers;

public static class NasThumbnailLoader
{
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
        if (thumb == null || thumb.UnitId <= 0 || string.IsNullOrEmpty(thumb.CacheKey))
            return;

        _ = LoadIntoImageAsync(new WeakReference<Image>(image), thumb.UnitId, thumb.CacheKey);
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

        var key = $"{id}:{thumb.CacheKey}";
        if (MemoryCache.TryGetValue(key, out var existing))
            return existing;

        return MemoryCache.GetOrAdd(key, _ => DownloadAndCacheAsync(id, thumb.CacheKey, cancellationToken))
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
                StartPhotoThumbnailLoad(new WeakReference<Image>(image), photo, canApply, cancellationToken));
            return;
        }

        StartPhotoThumbnailLoad(new WeakReference<Image>(image), photo, canApply, cancellationToken);
    }

    /// <summary>已由网格控件 ScheduleLoad 后调用，避免重复入队。</summary>
    internal static void TryLoadPhotoThumbnailDirect(
        Image image,
        Photo photo,
        Func<bool>? canApply = null,
        CancellationToken cancellationToken = default)
    {
        StartPhotoThumbnailLoad(new WeakReference<Image>(image), photo, canApply, cancellationToken);
    }

    private static void StartPhotoThumbnailLoad(
        WeakReference<Image> target,
        Photo photo,
        Func<bool>? canApply,
        CancellationToken cancellationToken)
    {
        var thumb = photo.Additional?.Thumbnail;
        if (thumb == null || string.IsNullOrEmpty(thumb.CacheKey))
        {
            ScheduleApplySource(target, null, canApply, cancellationToken, forGrid: true);
            return;
        }

        var id = thumb.UnitId > 0 ? thumb.UnitId : photo.Id;
        if (id <= 0)
        {
            ScheduleApplySource(target, null, canApply, cancellationToken, forGrid: true);
            return;
        }

        _ = LoadIntoImageAsync(target, id, thumb.CacheKey, canApply, cancellationToken, forGrid: true);
    }

    private static async Task LoadIntoImageAsync(
        WeakReference<Image> target,
        int id,
        string cacheKey,
        Func<bool>? canApply = null,
        CancellationToken cancellationToken = default,
        bool forGrid = false)
    {
        try
        {
            if (forGrid && NasGridImageApplyScheduler.IsScrolling)
            {
                NasGridImageApplyScheduler.RunWhenIdle(() =>
                    _ = LoadIntoImageAsync(target, id, cacheKey, canApply, cancellationToken, forGrid: true));
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
                    _ = LoadIntoImageAsync(target, id, cacheKey, canApply, cancellationToken, forGrid: true));
                return;
            }

            ScheduleApplySource(target, null, canApply, cancellationToken, forGrid);

            var key = $"{id}:{cacheKey}";
            var bytes = await MemoryCache
                .GetOrAdd(key, _ => DownloadAndCacheAsync(id, cacheKey, cancellationToken))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            MemoryCacheOrder.Enqueue(key);
            TrimMemoryCache();
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

    private static async Task<byte[]?> DownloadAndCacheAsync(int id, string cacheKey, CancellationToken cancellationToken)
    {
        await ThumbnailGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (NasMediaCache.TryGetThumbnailFile(id, cacheKey, out var cachedPath))
                return await File.ReadAllBytesAsync(cachedPath, cancellationToken).ConfigureAwait(false);

            if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
                return null;

            await using var network = await SynologyManager.Client.Foto.GetThumbnailAsync(
                id, cacheKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            using var ms = new MemoryStream();
            await network.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                return null;

            await NasMediaCache.WriteThumbnailAsync(id, cacheKey, bytes).ConfigureAwait(false);
            MemoryCacheOrder.Enqueue($"{id}:{cacheKey}");
            TrimMemoryCache();
            return bytes;
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
            MemoryCache.TryRemove($"{id}:{cacheKey}", out _);
        }
    }
}
