using System.Collections.Concurrent;

namespace OpenNas.Helpers;

public static class LocalMediaThumbnailLoader
{
    private const int MaxCacheEntries = 32;
    private static readonly SemaphoreSlim DecodeGate = new(3, 3);
    private static readonly ConcurrentDictionary<string, byte[]> ByteCache = new();
    private static readonly ConcurrentDictionary<string, ImageSource> ImageCache = new();
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> InFlight = new();
    private static readonly ConcurrentQueue<string> CacheOrder = new();

    public static void TryLoad(Image image, string? contentUri, CancellationToken cancelToken = default)
    {
        if (string.IsNullOrEmpty(contentUri))
        {
            image.Source = null;
            return;
        }

#if ANDROID
        if (ImageCache.TryGetValue(contentUri, out var cachedSource))
        {
            if (!cancelToken.IsCancellationRequested)
                image.Source = cachedSource;
            return;
        }

        if (ByteCache.TryGetValue(contentUri, out var cachedBytes) && cachedBytes.Length > 0)
        {
            var source = ImageCache.GetOrAdd(
                contentUri,
                _ => ImageSource.FromStream(() => new MemoryStream(cachedBytes)));
            if (!cancelToken.IsCancellationRequested)
                image.Source = source;
            return;
        }

        image.Source = null;
        _ = LoadAndroidAsync(image, contentUri, cancelToken);
#else
        image.Source = null;
#endif
    }

#if ANDROID
    private static async Task LoadAndroidAsync(Image image, string contentUri, CancellationToken cancelToken)
    {
        var target = new WeakReference<Image>(image);
        try
        {
            var bytes = await GetBytesAsync(contentUri, cancelToken);
            if (bytes is null || bytes.Length == 0 || cancelToken.IsCancellationRequested)
                return;

            var source = ImageCache.GetOrAdd(
                contentUri,
                _ => ImageSource.FromStream(() => new MemoryStream(bytes)));

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (cancelToken.IsCancellationRequested)
                    return;
                if (!target.TryGetTarget(out var img))
                    return;

                img.Source = source;
            });
        }
        catch (OperationCanceledException)
        {
            // cell recycled
        }
        catch
        {
            // ignore thumbnail failures in UI
        }
    }

    private static async Task<byte[]?> GetBytesAsync(string contentUri, CancellationToken cancelToken)
    {
        if (ByteCache.TryGetValue(contentUri, out var cached))
            return cached;

        var loadTask = InFlight.GetOrAdd(contentUri, uri => DecodeAndCacheAsync(uri));

        try
        {
            return await loadTask.WaitAsync(cancelToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> DecodeAndCacheAsync(string contentUri)
    {
        await DecodeGate.WaitAsync();
        try
        {
            if (ByteCache.TryGetValue(contentUri, out var hit))
                return hit;

            var bytes = await Task.Run(() => LoadJpegBytes(contentUri));
            if (bytes is not null && bytes.Length > 0)
                StoreCache(contentUri, bytes);
            return bytes;
        }
        finally
        {
            DecodeGate.Release();
            InFlight.TryRemove(contentUri, out _);
        }
    }

    private static void StoreCache(string key, byte[] bytes)
    {
        ByteCache[key] = bytes;
        CacheOrder.Enqueue(key);
        while (CacheOrder.Count > MaxCacheEntries && CacheOrder.TryDequeue(out var oldKey))
        {
            ByteCache.TryRemove(oldKey, out _);
            ImageCache.TryRemove(oldKey, out _);
        }
    }

    private static byte[]? LoadJpegBytes(string contentUri)
    {
        var ctx = Platform.CurrentActivity ?? global::Android.App.Application.Context;
        var uri = Android.Net.Uri.Parse(contentUri);

        using var bitmap = LoadBitmap(ctx, uri);
        if (bitmap is null)
            return null;

        using var ms = new MemoryStream();
        bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, 70, ms);
        return ms.ToArray();
    }

    private static Android.Graphics.Bitmap? LoadBitmap(global::Android.Content.Context ctx, Android.Net.Uri uri)
    {
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
        {
            try
            {
                return ctx.ContentResolver?.LoadThumbnail(uri, new Android.Util.Size(72, 72), null);
            }
            catch
            {
                // fall through
            }
        }

        var mime = ctx.ContentResolver?.GetType(uri) ?? "";
        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return LoadVideoFrame(ctx, uri);

        try
        {
            using var stream = ctx.ContentResolver?.OpenInputStream(uri);
            if (stream is null)
                return LoadVideoFrame(ctx, uri);

            var bounds = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeStream(stream, null, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
                return LoadVideoFrame(ctx, uri);

            stream.Dispose();
            using var stream2 = ctx.ContentResolver.OpenInputStream(uri);
            if (stream2 is null)
                return null;

            var sample = CalcSampleSize(bounds.OutWidth, bounds.OutHeight, 120);
            var opts = new Android.Graphics.BitmapFactory.Options { InSampleSize = sample };
            return Android.Graphics.BitmapFactory.DecodeStream(stream2, null, opts);
        }
        catch
        {
            return LoadVideoFrame(ctx, uri);
        }
    }

    private static Android.Graphics.Bitmap? LoadVideoFrame(global::Android.Content.Context ctx, Android.Net.Uri uri)
    {
        try
        {
            using var retriever = new Android.Media.MediaMetadataRetriever();
            retriever.SetDataSource(ctx, uri);
            return retriever.GetFrameAtTime(1_000_000, (int)Android.Media.Option.ClosestSync)
                   ?? retriever.GetFrameAtTime(0, (int)Android.Media.Option.ClosestSync);
        }
        catch
        {
            return null;
        }
    }

    private static int CalcSampleSize(int width, int height, int maxEdge)
    {
        var longest = Math.Max(width, height);
        var sample = 1;
        while (longest / sample > maxEdge)
            sample *= 2;
        return sample;
    }
#endif
}
