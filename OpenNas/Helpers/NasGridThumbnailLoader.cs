using System.Collections.Concurrent;
using NSynology.Foto;

namespace OpenNas.Helpers;

/// <summary>
/// 相册网格缩略图：后台缩小解码 + 滚动停止后再显示（Android 直连 ImageView）。
/// </summary>
public static partial class NasGridThumbnailLoader
{
    private const int MaxByteCache = 128;
    private const int GridMaxEdgePx = 200;

    private static readonly SemaphoreSlim DecodeGate = new(2, 2);
    private static readonly ConcurrentDictionary<string, byte[]> ByteCache = new();
    private static readonly ConcurrentDictionary<string, ImageSource> ImageSourceCache = new();
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> InFlight = new();
    private static readonly ConcurrentQueue<string> CacheOrder = new();

    public static void TryLoad(
        Image image,
        Photo photo,
        Func<bool> canApply,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCacheKey(photo, out var key, out var id, out var cacheKey))
        {
            NasGridScrollGate.RunWhenIdle(() =>
            {
                if (canApply())
                    ClearImage(image);
            });
            return;
        }

        if (ByteCache.TryGetValue(key, out var readyBytes))
        {
            _ = PresentBytesAsync(new WeakReference<Image>(image), key, readyBytes, canApply, cancellationToken);
            return;
        }

        if (NasMediaCache.TryGetThumbnailFile(id, cacheKey, out var cachedPath))
        {
            _ = EnsureDecodedAsync(image, key, cachedPath, canApply, cancellationToken);
            return;
        }

        _ = DownloadAndDecodeAsync(image, photo, key, id, cacheKey, canApply, cancellationToken);
    }

    private static async Task DownloadAndDecodeAsync(
        Image image,
        Photo photo,
        string key,
        int id,
        string cacheKey,
        Func<bool> canApply,
        CancellationToken cancellationToken)
    {
        var target = new WeakReference<Image>(image);
        try
        {
            await NasThumbnailLoader.EnsureCachedAsync(photo, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                return;

            if (!NasMediaCache.TryGetThumbnailFile(id, cacheKey, out var path))
                return;

            await EnsureDecodedAsync(target, key, path, canApply, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // cell recycled
        }
        catch
        {
            // ignore
        }
    }

    private static Task EnsureDecodedAsync(
        Image image,
        string key,
        string path,
        Func<bool> canApply,
        CancellationToken cancellationToken) =>
        EnsureDecodedAsync(new WeakReference<Image>(image), key, path, canApply, cancellationToken);

    private static async Task EnsureDecodedAsync(
        WeakReference<Image> target,
        string key,
        string path,
        Func<bool> canApply,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await GetDecodedBytesAsync(key, path, cancellationToken).ConfigureAwait(false);
            if (bytes is null or { Length: 0 } || cancellationToken.IsCancellationRequested)
                return;

            await PresentBytesAsync(target, key, bytes, canApply, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // cell recycled
        }
        catch
        {
            // ignore
        }
    }

    private static async Task PresentBytesAsync(
        WeakReference<Image> target,
        string key,
        byte[] bytes,
        Func<bool> canApply,
        CancellationToken cancellationToken)
    {
#if ANDROID
        await PresentBytesAndroidAsync(target, key, bytes, canApply, cancellationToken).ConfigureAwait(false);
#else
        if (!ImageSourceCache.TryGetValue(key, out var source))
        {
            source = ImageSource.FromStream(() => new MemoryStream(bytes));
            ImageSourceCache[key] = source;
        }

        NasGridScrollGate.RunWhenIdle(() =>
        {
            if (cancellationToken.IsCancellationRequested || !canApply())
                return;
            if (!target.TryGetTarget(out var image))
                return;

            image.Source = source;
        });
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    private static Task PresentBytesAndroidAsync(
        WeakReference<Image> target,
        string key,
        byte[] bytes,
        Func<bool> canApply,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        NasGridScrollGate.RunWhenIdle(() => _ = ApplyWhenIdleAsync());

        return tcs.Task;

        async Task ApplyWhenIdleAsync()
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                var bitmap = await Task.Run(() => DecodeGridBitmap(key, bytes), cancellationToken).ConfigureAwait(false);
                if (bitmap == null || cancellationToken.IsCancellationRequested)
                    return;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested || !canApply())
                        return;
                    if (!target.TryGetTarget(out var image))
                        return;

                    if (image.Handler?.PlatformView is Android.Widget.ImageView)
                        SetAndroidBitmap(image, bitmap);
                    else
                        image.HandlerChanged += ApplyWhenReady;

                    void ApplyWhenReady(object? sender, EventArgs e)
                    {
                        image.HandlerChanged -= ApplyWhenReady;
                        if (cancellationToken.IsCancellationRequested || !canApply())
                            return;
                        SetAndroidBitmap(image, bitmap);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // cell recycled
            }
            finally
            {
                tcs.TrySetResult();
            }
        }
    }
#endif

    private static async Task<byte[]?> GetDecodedBytesAsync(string key, string path, CancellationToken cancellationToken)
    {
        if (ByteCache.TryGetValue(key, out var cached))
            return cached;

        var task = InFlight.GetOrAdd(key, _ => DecodeFileAsync(key, path));
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> DecodeFileAsync(string key, string path)
    {
        await DecodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ByteCache.TryGetValue(key, out var hit))
                return hit;

            var bytes = await Task.Run(() => DecodeGridJpeg(path)).ConfigureAwait(false);
            if (bytes is null or { Length: 0 })
                return null;

            StoreBytes(key, bytes);
            ImageSourceCache.GetOrAdd(key, _ => ImageSource.FromStream(() => new MemoryStream(bytes)));
            return bytes;
        }
        finally
        {
            DecodeGate.Release();
            InFlight.TryRemove(key, out _);
        }
    }

    private static void StoreBytes(string key, byte[] bytes)
    {
        ByteCache[key] = bytes;
        CacheOrder.Enqueue(key);
        while (CacheOrder.Count > MaxByteCache && CacheOrder.TryDequeue(out var oldKey))
        {
            ByteCache.TryRemove(oldKey, out _);
            ImageSourceCache.TryRemove(oldKey, out _);
        }
    }

    private static bool TryGetCacheKey(Photo photo, out string key, out int id, out string cacheKey)
    {
        key = string.Empty;
        id = 0;
        cacheKey = string.Empty;

        var thumb = photo.Additional?.Thumbnail;
        if (thumb == null || string.IsNullOrEmpty(thumb.CacheKey))
            return false;

        id = thumb.UnitId > 0 ? thumb.UnitId : photo.Id;
        if (id <= 0)
            return false;

        cacheKey = thumb.CacheKey;
        key = $"{id}:{cacheKey}";
        return true;
    }

    private static partial void ClearImage(Image image);
    private static partial byte[]? DecodeGridJpeg(string path);
#if ANDROID
    private static partial object? DecodeGridBitmap(string key, byte[] bytes);
    private static partial void SetAndroidBitmap(Image image, object bitmap);
#endif
}
