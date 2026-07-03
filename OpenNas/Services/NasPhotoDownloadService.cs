using NSynology;
using NSynology.Foto;
using OpenNas.Helpers;

namespace OpenNas.Services;

public sealed record NasBatchDownloadProgress(
    int CompletedCount,
    int TotalCount,
    long BytesReceived,
    long? CurrentFileTotal,
    string? CurrentFileName,
    bool IsComplete);

public sealed class NasPhotoDownloadResult
{
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public bool Cancelled { get; init; }
    public IReadOnlyList<(Photo Photo, string Reason)> Failures { get; init; } = [];
}

public static class NasPhotoDownloadService
{
    private const long ConfirmSizeThresholdBytes = 50L * 1024 * 1024;
    private const int ConfirmCountThreshold = 20;
    private static readonly TimeSpan ItemTimeout = TimeSpan.FromMinutes(5);

    public static bool ShouldConfirmBatch(IReadOnlyList<Photo> photos)
    {
        if (photos.Count > ConfirmCountThreshold)
            return true;

        var totalSize = photos.Where(p => p.FileSize > 0).Sum(p => p.FileSize);
        return totalSize > ConfirmSizeThresholdBytes;
    }

    public static string BuildConfirmMessage(IReadOnlyList<Photo> photos)
    {
        var totalSize = photos.Where(p => p.FileSize > 0).Sum(p => p.FileSize);
        return totalSize > 0
            ? $"将下载 {photos.Count} 项（约 {NasMediaCache.FormatBytes(totalSize)}），是否继续？"
            : $"将下载 {photos.Count} 项，是否继续？";
    }

    public static bool IsWifiBlocked(ConnectionService connection) =>
        connection.GetDownloadWifiOnly() && !NetworkHelper.IsOnWifi();

    public static async Task<NasPhotoDownloadResult> DownloadBatchAsync(
        IReadOnlyList<Photo> photos,
        IProgress<NasBatchDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (photos.Count == 0)
            return new NasPhotoDownloadResult();

        if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
            throw new InvalidOperationException("未连接 NAS，请重新登录。");

        var distinct = photos
            .Where(p => p.Id > 0)
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .ToList();

        var failures = new List<(Photo Photo, string Reason)>();
        var successCount = 0;
        var total = distinct.Count;

        for (var index = 0; index < distinct.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var photo = distinct[index];

            try
            {
                using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                itemCts.CancelAfter(ItemTimeout);

                var fileProgress = new Progress<NasDownloadProgress>(p =>
                {
                    progress?.Report(new NasBatchDownloadProgress(
                        CompletedCount: index,
                        TotalCount: total,
                        BytesReceived: p.BytesReceived,
                        CurrentFileTotal: p.TotalBytes,
                        CurrentFileName: photo.Filename,
                        IsComplete: false));
                });

                var cachedPath = await NasOriginalLoader.EnsureCachedWithProgressAsync(
                    photo,
                    fileProgress,
                    itemCts.Token).ConfigureAwait(false);

                if (string.IsNullOrEmpty(cachedPath) || !File.Exists(cachedPath))
                    throw new InvalidOperationException("下载失败");

                var saved = await DeviceMediaSaver.SaveToGalleryAsync(
                    cachedPath,
                    ResolveFileName(photo),
                    ResolveMimeType(photo),
                    itemCts.Token).ConfigureAwait(false);

                if (!saved)
                    throw new InvalidOperationException("保存到本机失败");

                successCount++;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add((photo, "下载超时"));
            }
            catch (OperationCanceledException)
            {
                return new NasPhotoDownloadResult
                {
                    SuccessCount = successCount,
                    FailedCount = failures.Count,
                    Cancelled = true,
                    Failures = failures
                };
            }
            catch (Exception ex)
            {
                failures.Add((photo, ex.Message));
            }

            progress?.Report(new NasBatchDownloadProgress(
                CompletedCount: index + 1,
                TotalCount: total,
                BytesReceived: 0,
                CurrentFileTotal: null,
                CurrentFileName: null,
                IsComplete: index + 1 >= total));
        }

        return new NasPhotoDownloadResult
        {
            SuccessCount = successCount,
            FailedCount = failures.Count,
            Failures = failures
        };
    }

    public static async Task<string?> EnsureLocalFileAsync(
        Photo photo,
        IProgress<NasDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
            throw new InvalidOperationException("未连接 NAS，请重新登录。");

        if (NasMediaCache.TryGetOriginalFile(photo, out var cached))
            return cached;

        return await NasOriginalLoader.EnsureCachedWithProgressAsync(photo, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<bool> DownloadSingleToGalleryAsync(
        Photo photo,
        IProgress<NasDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = await EnsureLocalFileAsync(photo, progress, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        return await DeviceMediaSaver.SaveToGalleryAsync(
            path,
            ResolveFileName(photo),
            ResolveMimeType(photo),
            cancellationToken).ConfigureAwait(false);
    }

    public static string ResolveFileName(Photo photo)
    {
        var name = Path.GetFileName(photo.Filename ?? "");
        if (string.IsNullOrWhiteSpace(name))
            return photo.IsVideo ? $"video_{photo.Id}.mp4" : $"photo_{photo.Id}.jpg";

        return name;
    }

    public static string ResolveMimeType(Photo photo)
    {
        if (photo.IsVideo)
            return GuessVideoMime(ResolveFileName(photo));

        return GuessImageMime(ResolveFileName(photo));
    }

    private static string GuessImageMime(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };
    }

    private static string GuessVideoMime(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".3gp" => "video/3gpp",
            _ => "video/mp4"
        };
    }
}
