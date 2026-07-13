using System.Collections.Concurrent;

using Microsoft.Maui.ApplicationModel;

using NSynology;

using NSynology.Foto;

using OpenNas.Core.Helpers;

using OpenNas.Services;

namespace OpenNas.Helpers;

/// <summary>
/// 大图预览时发现照片/视频 sm 缩略图为占位图，则下载原片并以 duplicate=rename 重传。
/// rename 会创建新条目（新 id）；校验新条目缩略图后删除旧坏条目并更新引用。
/// </summary>
public static class NasVideoThumbnailRepair
{
    private static readonly ConcurrentDictionary<int, Task<bool>> InFlight = new();
    private static readonly ConcurrentDictionary<int, DateTime> CooldownUntil = new();
    private static readonly SemaphoreSlim UploadGate = new(1, 1);

    private static readonly TimeSpan CooldownDuration = TimeSpan.FromHours(6);
    private static readonly TimeSpan VerifyPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VerifyTimeout = TimeSpan.FromMinutes(2);

    /// <summary>当前环境是否可对占位缩略图尝试修复（相册上下文 + 已登录）。</summary>
    public static bool CanRepair(Photo photo) => ShouldAttempt(photo);

    /// <summary>预览页后台调度：仅当 sm 缩略图为占位图时才修复。</summary>
    public static void ScheduleRepairIfPlaceholder(Photo photo)
    {
#if ANDROID
        if (!ShouldAttempt(photo))
            return;

        _ = TryRepairIfPlaceholderAsync(photo);
#endif
    }

    /// <summary>检测到 sm 占位缩略图时修复，成功返回 true。</summary>
    public static async Task<bool> TryRepairIfPlaceholderAsync(
        Photo photo,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldAttempt(photo))
            return false;

        var bytes = await TryGetCachedSmThumbnailBytesAsync(photo, cancellationToken).ConfigureAwait(false);
        if (bytes == null || !NasThumbnailBytes.IsLikelyPlaceholder(bytes))
            return false;

        AppLog.Warn(
            $"占位缩略图（预览）id={photo.Id} {photo.Filename} type={photo.Type} bytes={bytes.Length}");
        return await RepairAsync(photo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>已知缩略图异常时修复，成功返回 true。</summary>
    public static Task<bool> RepairAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        if (!ShouldAttempt(photo))
            return Task.FromResult(false);

        if (InFlight.TryGetValue(photo.Id, out var existing))
            return existing;

        var task = InFlight.GetOrAdd(
            photo.Id,
            _ => Task.Run(() => RepairCoreAsync(photo, cancellationToken), cancellationToken));
        return AwaitAndCleanupAsync(photo.Id, task, cancellationToken);
    }

    private static async Task<bool> AwaitAndCleanupAsync(
        int photoId,
        Task<bool> task,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            InFlight.TryRemove(photoId, out _);
        }
    }

    private static bool ShouldAttempt(Photo photo)
    {
#if !ANDROID
        return false;
#else
        if (photo.Id <= 0)
            return false;

        if (PhotosAlbumMediaScope.CurrentAlbumId is not > 0)
            return false;

        if (CooldownUntil.TryGetValue(photo.Id, out var until) && until > DateTime.UtcNow)
            return false;

        var client = SynologyManager.Client;
        return client != null && !string.IsNullOrEmpty(client.Sid);
#endif
    }

    private static async Task<bool> RepairCoreAsync(Photo photo, CancellationToken cancellationToken)
    {
        var albumId = ResolveUploadAlbumId();
        if (albumId is not > 0)
        {
            AppLog.Debug($"缩略图修复跳过 id={photo.Id}：无 album_id（需从相册进入预览）");
            return false;
        }

        var client = SynologyManager.Client;
        if (client == null || string.IsNullOrEmpty(client.Sid))
            return false;

        var brokenId = photo.Id;
        var previousCacheKey = photo.Additional?.Thumbnail?.CacheKey;

        try
        {
#if ANDROID
            client.PrepareAppUploadSession();
#endif

            await RefreshPhotoMetadataAsync(client, photo, brokenId, cancellationToken).ConfigureAwait(false);

            ToastRepair("正在修复缩略图…");

            AppLog.Debug(
                $"视频缩略图修复开始 id={brokenId} {photo.Filename} album={albumId} mobile_cache_mtime={photo.Additional?.MobileCacheMtime ?? 0}");

            using (PhotosAlbumMediaScope.Use(albumId.Value, PhotosAlbumMediaScope.CurrentPassphrase))
            {
                var localPath = await NasOriginalLoader.EnsureCachedAsync(photo, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
                {
                    AppLog.Warn($"视频缩略图修复：无法下载原片 id={brokenId}");
                    ToastRepair("缩略图修复失败");
                    return false;
                }

                var uploadName = photo.Filename;
                var mime = AlbumPhotoUpload.GuessMimeType(uploadName);
                var mtimeCandidates = ResolveMtimeCandidates(photo, localPath, uploadName).ToList();
                if (mtimeCandidates.Count == 0)
                {
                    AppLog.Warn($"视频缩略图修复跳过 id={brokenId}：无有效 mtime");
                    ToastRepair("缩略图修复失败");
                    return false;
                }

                UploadResult? lastResult = null;

                await UploadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    foreach (var mtime in mtimeCandidates)
                    {
                        AppLog.Debug(
                            $"视频缩略图修复上传 id={brokenId} duplicate=rename mtime={mtime} name={uploadName}");

                        try
                        {
                            lastResult = await client.ReuploadVideoThumbnailFromLocalFileAsync(
                                localPath,
                                uploadName,
                                mime,
                                albumId.Value,
                                mtime,
                                AppUploadDuplicate.Rename,
                                cancellationToken).ConfigureAwait(false);

                            AppLog.Debug(
                                $"视频缩略图修复响应 id={brokenId} action={lastResult.Action} new_id={lastResult.PhotoId} body={TrimForLog(lastResult.RawResponse)}");
                            break;
                        }
                        catch (SynologyUploadException ex) when (ex.ErrorCode == 120)
                        {
                            if (SynologyClient.TryGetUploadFieldError(ex.RawResponse, out var field, out var reason)
                                && string.Equals(field, "duplicate", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(reason, "condition", StringComparison.OrdinalIgnoreCase))
                            {
                                AppLog.Warn(
                                    $"视频缩略图修复 duplicate 非法 id={brokenId} raw={TrimForLog(ex.RawResponse)}");
                                break;
                            }

                            AppLog.Debug(
                                $"视频缩略图修复 mtime={mtime} 未命中 id={brokenId}（{ex.Message}）");
                        }
                    }

                    if (lastResult == null || lastResult.PhotoId <= 0)
                    {
                        AppLog.Warn($"视频缩略图修复失败 id={brokenId}：无有效上传响应");
                        CooldownUntil[brokenId] = DateTime.UtcNow.Add(CooldownDuration);
                        ToastRepair("缩略图修复失败");
                        return false;
                    }

                    var replacementId = lastResult.PhotoId;
                    if (!await VerifyThumbnailFixedAsync(
                            client,
                            replacementId,
                            albumId.Value,
                            cancellationToken).ConfigureAwait(false))
                    {
                        AppLog.Warn(
                            $"视频缩略图修复失败 id={brokenId}：新条目 id={replacementId} 缩略图仍为占位图");
                        CooldownUntil[brokenId] = DateTime.UtcNow.Add(CooldownDuration);
                        ToastRepair("缩略图修复失败");
                        return false;
                    }

                    if (replacementId != brokenId)
                    {
                        var deleted = await client.FotoBrowse
                            .DeletePhotosAsync([brokenId], cancellationToken)
                            .ConfigureAwait(false);
                        if (!deleted)
                        {
                            AppLog.Warn(
                                $"视频缩略图修复：新条目 id={replacementId} 缩略图正常，但删除旧条目 id={brokenId} 失败");
                        }
                        else
                        {
                            AppLog.Debug($"视频缩略图修复已删除旧条目 id={brokenId}，保留 id={replacementId}");
                        }
                    }

                    await ApplyReplacementPhotoAsync(client, photo, replacementId, cancellationToken)
                        .ConfigureAwait(false);
                    InvalidateLocalThumbnailCache(photo, brokenId, previousCacheKey);

                    var newKey = photo.Additional?.Thumbnail?.CacheKey ?? "";
                    AppLog.Debug(
                        $"视频缩略图已修复 broken_id={brokenId} replacement_id={replacementId} action={lastResult.Action} cache_key={newKey}");
                    CooldownUntil.TryRemove(brokenId, out _);
                    ToastRepair("缩略图已修复");
                    return true;
                }
                finally
                {
                    UploadGate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"视频缩略图修复异常 id={brokenId}", ex);
            if (!IsThumbnailGenerationFailure(ex))
                CooldownUntil[brokenId] = DateTime.UtcNow.Add(CooldownDuration);
            ToastRepair("缩略图修复失败");
            return false;
        }
    }

    private static void ToastRepair(string message)
    {
#if ANDROID
        MainThread.BeginInvokeOnMainThread(() => _ = UiFeedback.ToastAsync(message));
#endif
    }

    private static bool IsThumbnailGenerationFailure(Exception ex) =>
        ex is InvalidOperationException ioe
        && ioe.Message.Contains("生成有效缩略图", StringComparison.Ordinal);

    private static async Task ApplyReplacementPhotoAsync(
        SynologyClient client,
        Photo photo,
        int replacementId,
        CancellationToken cancellationToken)
    {
        var fresh = await client.FotoBrowse.GetPhotoAsync(replacementId, cancellationToken).ConfigureAwait(false);
        if (fresh == null)
            return;

        photo.Id = replacementId;
        if (!string.IsNullOrEmpty(fresh.Filename))
            photo.Filename = fresh.Filename;
        if (fresh.FileSize > 0)
            photo.FileSize = fresh.FileSize;
        MergePhotoMetadata(photo, fresh);
    }

    private static async Task<bool> VerifyThumbnailFixedAsync(
        SynologyClient client,
        int photoId,
        int albumId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.Add(VerifyTimeout);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Photo? fresh;
            try
            {
                fresh = await client.FotoBrowse.GetPhotoAsync(photoId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Debug($"视频缩略图修复校验 metadata 失败 id={photoId}", ex);
                await Task.Delay(VerifyPollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var thumb = fresh?.Additional?.Thumbnail;
            var cacheKey = thumb?.CacheKey;
            if (string.IsNullOrEmpty(cacheKey))
            {
                await Task.Delay(VerifyPollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var unitId = thumb!.UnitId > 0 ? thumb.UnitId : photoId;
            var converting = string.Equals(thumb.Sm, "converting", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(thumb.M, "converting", StringComparison.OrdinalIgnoreCase);

            try
            {
                var bytes = await DownloadThumbnailBytesAsync(
                        client,
                        unitId,
                        cacheKey,
                        albumId,
                        PhotosAlbumMediaScope.CurrentPassphrase,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!NasThumbnailBytes.IsLikelyPlaceholder(bytes))
                    return true;
            }
            catch (Exception ex)
            {
                AppLog.Debug($"视频缩略图修复校验拉取失败 id={photoId}", ex);
            }

            if (!converting)
                break;

            await Task.Delay(VerifyPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<byte[]> DownloadThumbnailBytesAsync(
        SynologyClient client,
        int unitId,
        string cacheKey,
        int albumId,
        string? passphrase,
        CancellationToken cancellationToken)
    {
        await using var stream = await client.Foto
            .GetSynoFotoThumbnailAsync(
                unitId,
                cacheKey,
                "sm",
                "unit",
                albumId,
                passphrase,
                cancellationToken)
            .ConfigureAwait(false);

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }

    private static int? ResolveUploadAlbumId() => PhotosAlbumMediaScope.CurrentAlbumId;

    /// <summary>仅读本地磁盘缓存的 sm 缩略图，检测占位图时不访问 NAS。</summary>
    private static async Task<byte[]?> TryGetCachedSmThumbnailBytesAsync(
        Photo photo,
        CancellationToken cancellationToken)
    {
        var thumb = photo.Additional?.Thumbnail;
        if (thumb == null || string.IsNullOrEmpty(thumb.CacheKey))
            return null;

        var unitId = thumb.UnitId > 0 ? thumb.UnitId : photo.Id;
        if (unitId <= 0 || !NasMediaCache.TryGetThumbnailFile(unitId, thumb.CacheKey, out var cachedPath))
            return null;

        try
        {
            return await File.ReadAllBytesAsync(cachedPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Debug($"读取本地缩略图缓存失败 id={photo.Id}", ex);
            return null;
        }
    }

    private static async Task RefreshPhotoMetadataAsync(
        SynologyClient client,
        Photo photo,
        int photoId,
        CancellationToken cancellationToken)
    {
        try
        {
            var fresh = await client.FotoBrowse.GetPhotoAsync(photoId, cancellationToken).ConfigureAwait(false);
            if (fresh != null)
                MergePhotoMetadata(photo, fresh);
        }
        catch (Exception ex)
        {
            AppLog.Debug($"视频缩略图修复拉取 metadata 失败 id={photoId}", ex);
        }
    }

    internal static void MergePhotoMetadata(Photo target, Photo fresh)
    {
        target.Additional ??= new Additional();
        fresh.Additional ??= new Additional();

        if (fresh.Additional.MobileCacheMtime > 0)
            target.Additional.MobileCacheMtime = fresh.Additional.MobileCacheMtime;

        if (fresh.Additional.Thumbnail != null)
            target.Additional.Thumbnail = fresh.Additional.Thumbnail;
    }

    private static IEnumerable<long> ResolveMtimeCandidates(Photo photo, string localFilePath, string fileName)
    {
        var seen = new HashSet<long>();

        foreach (var value in CollectMtimeCandidates(photo, localFilePath, fileName))
        {
            if (value > 0 && seen.Add(value))
                yield return value;
        }
    }

    private static IEnumerable<long> CollectMtimeCandidates(Photo photo, string localFilePath, string fileName)
    {
        var mobileCacheMtime = photo.Additional?.MobileCacheMtime ?? 0;
        if (mobileCacheMtime > 0)
        {
            yield return mobileCacheMtime;
            yield break;
        }

        AppLog.Debug($"视频缩略图修复 id={photo.Id} 缺少 mobile_cache_mtime，尝试回退 mtime");

        if (photo.Time > 0)
            yield return photo.Time;

        var parsed = MediaUploadTimeHelper.TryParseFromFileName(fileName);
        if (parsed > 0)
            yield return parsed;
    }

    private static void InvalidateLocalThumbnailCache(
        Photo photo,
        int previousPhotoId,
        string? previousCacheKey = null)
    {
        var thumb = photo.Additional?.Thumbnail;
        var unitId = thumb?.UnitId > 0 ? thumb!.UnitId : photo.Id;
        var cacheKey = thumb?.CacheKey;

        if (previousPhotoId > 0 && !string.IsNullOrEmpty(previousCacheKey))
            NasMediaCache.TryInvalidateThumbnail(previousPhotoId, previousCacheKey);

        if (unitId > 0 && !string.IsNullOrEmpty(cacheKey))
            NasMediaCache.TryInvalidateThumbnail(unitId, cacheKey);

        NasThumbnailLoader.ClearMemoryCache();
    }

    private static string TrimForLog(string? text, int max = 240)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return text.Length <= max ? text : text[..max] + "…";
    }
}
