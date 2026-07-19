using System.Collections.Concurrent;
using NSynology;
using NSynology.Foto;
using OpenNas.Services;

namespace OpenNas.Helpers;

public readonly record struct NasDownloadProgress(long BytesReceived, long? TotalBytes, bool IsComplete);

public static class NasOriginalLoader
{
    private static readonly SemaphoreSlim Gate = new(2, 2);
    private static readonly ConcurrentDictionary<int, Task<string?>> InFlight = new();

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
        var temp = "";
        string? protectedPath = null;
        var keepProtected = false;
        try
        {
            if (NasMediaCache.TryGetOriginalFile(photo, out var cached))
                return cached;

            if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
                return null;

            await using var network = await NasFotoMediaApi.GetDownloadPhotoAsync(
                SynologyManager.Client, photo, cancellationToken);
            var path = NasMediaCache.GetOriginalFilePath(photo);
            protectedPath = path;
            NasMediaCache.ProtectPath(path);
            temp = path + ".tmp";
            long? totalBytes = photo.FileSize > 0 ? photo.FileSize : null;
            long received = 0;
            var lastReportAt = 0L;
            var lastReportBytes = 0L;

            await using (var file = File.Create(temp))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await network.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;

                    var now = Environment.TickCount64;
                    if (received - lastReportBytes >= 512 * 1024 || now - lastReportAt >= 250)
                    {
                        lastReportBytes = received;
                        lastReportAt = now;
                        progress?.Report(new NasDownloadProgress(received, totalBytes, false));
                    }
                }

                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
            temp = "";
            keepProtected = true;
            NasMediaCache.NotifyOriginalStored();
            progress?.Report(new NasDownloadProgress(received, totalBytes ?? received, true));
            return path;
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
            AppLog.Error($"原文件下载失败 id={photo.Id} size={photo.FileSize}", ex);
            return null;
        }
        finally
        {
            if (!string.IsNullOrEmpty(temp))
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch
                {
                    // ignore
                }
            }

            if (!keepProtected)
                NasMediaCache.UnprotectPath(protectedPath);

            Gate.Release();
            InFlight.TryRemove(photo.Id, out _);
        }
    }
}
