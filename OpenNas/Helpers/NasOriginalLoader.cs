using System.Collections.Concurrent;
using NSynology;
using NSynology.Foto;

namespace OpenNas.Helpers;

public readonly record struct NasDownloadProgress(long BytesReceived, long? TotalBytes, bool IsComplete);

public static class NasOriginalLoader
{
    private static readonly SemaphoreSlim Gate = new(2, 2);
    private static readonly ConcurrentDictionary<int, Task<string?>> InFlight = new();

    public static void TryLoad(
        Image image,
        Photo photo,
        Action<bool>? onLoadingChanged = null,
        Func<bool>? canApply = null,
        Action? onApplied = null)
    {
        if (photo.Id <= 0)
            return;

        _ = LoadIntoImageAsync(image, photo, onLoadingChanged, canApply, onApplied);
    }

    private static async Task LoadIntoImageAsync(
        Image image,
        Photo photo,
        Action<bool>? onLoadingChanged,
        Func<bool>? canApply,
        Action? onApplied)
    {
        try
        {
            SetLoading(onLoadingChanged, true);

            if (NasMediaCache.TryGetOriginalFile(photo, out var cached))
            {
                await ApplyImageAsync(image, cached, canApply, onApplied);
                return;
            }

            var path = await InFlight.GetOrAdd(photo.Id, _ => DownloadAndCacheAsync(photo))
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(path))
                await ApplyImageAsync(image, path, canApply, onApplied);
        }
        catch (SynologyApiException)
        {
            throw;
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

    private static async Task ApplyImageAsync(Image image, string path, Func<bool>? canApply, Action? onApplied = null)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (canApply != null && !canApply())
                return;

            image.Source = ImageSource.FromFile(path);
            onApplied?.Invoke();
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

            await using var network = await NasFotoMediaApi.GetDownloadPhotoAsync(
                SynologyManager.Client, photo, cancellationToken);
            var path = await NasMediaCache.WriteOriginalFromStreamAsync(photo, network, cancellationToken);
            return path;
        }
        catch (SynologyApiException)
        {
            throw;
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

    public static Task<string?> EnsureCachedWithProgressAsync(
        Photo photo,
        IProgress<NasDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (photo.Id <= 0)
            return Task.FromResult<string?>(null);

        if (NasMediaCache.TryGetOriginalFile(photo, out var cached))
        {
            progress?.Report(new NasDownloadProgress(0, photo.FileSize > 0 ? photo.FileSize : null, true));
            return Task.FromResult<string?>(cached);
        }

        return InFlight.GetOrAdd(
            photo.Id,
            _ => DownloadAndCacheWithProgressAsync(photo, progress, cancellationToken));
    }

    private static async Task<string?> DownloadAndCacheWithProgressAsync(
        Photo photo,
        IProgress<NasDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (NasMediaCache.TryGetOriginalFile(photo, out var cached))
                return cached;

            if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
                return null;

            await using var network = await NasFotoMediaApi.GetDownloadPhotoAsync(
                SynologyManager.Client, photo, cancellationToken);
            var path = NasMediaCache.GetOriginalFilePath(photo);
            var temp = path + ".tmp";
            long? totalBytes = photo.FileSize > 0 ? photo.FileSize : null;
            long received = 0;

            await using (var file = File.Create(temp))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await network.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new NasDownloadProgress(received, totalBytes, false));
                }

                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
            progress?.Report(new NasDownloadProgress(received, totalBytes ?? received, true));
            return path;
        }
        catch (SynologyApiException)
        {
            throw;
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
}
