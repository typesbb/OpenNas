using System.Collections.Concurrent;
using NSynology;
using NSynology.Foto;

namespace OpenNas.Helpers;

public static class NasOriginalLoader
{
    private static readonly SemaphoreSlim Gate = new(2, 2);
    private static readonly ConcurrentDictionary<int, Task<string?>> InFlight = new();

    public static void TryLoad(
        Image image,
        Photo photo,
        Action<bool>? onLoadingChanged = null,
        Func<bool>? canApply = null)
    {
        if (photo.Id <= 0)
            return;

        _ = LoadIntoImageAsync(image, photo, onLoadingChanged, canApply);
    }

    private static async Task LoadIntoImageAsync(
        Image image,
        Photo photo,
        Action<bool>? onLoadingChanged,
        Func<bool>? canApply)
    {
        try
        {
            SetLoading(onLoadingChanged, true);

            if (NasMediaCache.TryGetOriginalFile(photo, out var cached))
            {
                await ApplyImageAsync(image, cached, canApply);
                return;
            }

            var path = await InFlight.GetOrAdd(photo.Id, _ => DownloadAndCacheAsync(photo))
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(path))
                await ApplyImageAsync(image, path, canApply);
        }
        catch
        {
            // ignore
        }
        finally
        {
            SetLoading(onLoadingChanged, false);
        }
    }

    private static async Task ApplyImageAsync(Image image, string path, Func<bool>? canApply)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (canApply != null && !canApply())
                return;

            image.Source = ImageSource.FromFile(path);
        });
    }

    private static void SetLoading(Action<bool>? onLoadingChanged, bool loading)
    {
        if (onLoadingChanged == null)
            return;

        MainThread.BeginInvokeOnMainThread(() => onLoadingChanged(loading));
    }

    private static async Task<string?> DownloadAndCacheAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (NasMediaCache.TryGetOriginalFile(photo, out var cached))
                return cached;

            if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
                return null;

            await using var network = await SynologyManager.Client.Foto.GetDownloadPhotoAsync(photo, cancellationToken);
            var path = await NasMediaCache.WriteOriginalFromStreamAsync(photo, network, cancellationToken);
            return path;
        }
        catch
        {
            return null;
        }
        finally
        {
            Gate.Release();
            InFlight.TryRemove(photo.Id, out _);
        }
    }

    public static void ClearMemoryCache() => InFlight.Clear();

    /// <summary>通过应用 HttpClient 下载原文件到本地缓存（可信任内网自签 HTTPS），供视频播放等场景使用。</summary>
    public static Task<string?> EnsureCachedAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        if (photo.Id <= 0)
            return Task.FromResult<string?>(null);

        if (NasMediaCache.TryGetOriginalFile(photo, out var cached))
            return Task.FromResult<string?>(cached);

        return InFlight.GetOrAdd(photo.Id, _ => DownloadAndCacheAsync(photo, cancellationToken));
    }
}
