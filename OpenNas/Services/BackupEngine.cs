using NSynology;

using NSynology.Foto;

using OpenNas.Data;

using OpenNas.Media;

using OpenNas.Models;



namespace OpenNas.Services;



public class BackupEngine

{

    /// <summary>串行上传，避免多张大图/视频同时读入内存触发 GC 风暴。</summary>

    private const int MaxParallelUploads = 1;

    private readonly BackupDatabase _db;

    private readonly ConnectionService _connection;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    private CancellationTokenSource? _cts;

    private bool _paused;

    private bool _retryFailedOnly;



    public BackupProgressInfo Progress { get; } = new();

    public event EventHandler? ProgressChanged;



    public BackupEngine(BackupDatabase db, ConnectionService connection)
    {
        _db = db;
        _connection = connection;
    }



    public bool IsRunning => Progress.IsRunning;

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    public void Cancel() => _cts?.Cancel();



    public Task RunBackupAsync(ILocalMediaService mediaService, bool retryFailedOnly = false)

    {

        _retryFailedOnly = retryFailedOnly;

        return RunBackupCoreAsync(mediaService);

    }



    private async Task RunBackupCoreAsync(ILocalMediaService mediaService)

    {

        if (!await _runLock.WaitAsync(0))
        {
            BackupLog.Warn("备份已在运行，忽略重复启动");
            return;
        }



        _cts = new CancellationTokenSource();

        var token = _cts.Token;

        ResetProgress(running: true);



        try

        {

            if (!_connection.IsLoggedIn)
                throw new InvalidOperationException("请先登录 NAS（会话已过期时请从「我的」退出后重新登录）。");

#if ANDROID
            SynologyManager.Client.PrepareOfficialAppUploadSession();
#endif
            BackupLog.Info($"备份开始 retryFailed={_retryFailedOnly}");

            if (_connection.GetWifiOnly() && !IsOnWifi())
                throw new InvalidOperationException("当前非 Wi-Fi，已启用「仅 Wi-Fi 备份」。");

            BackupLog.Info("正在扫描本地相册并比对数据库…");
            var work = await BuildWorkQueueAsync(mediaService, token);
            BackupLog.Info($"待上传 {work.Count} 个文件");
            if (work.Count == 0)
                throw new InvalidOperationException(_retryFailedOnly ? "没有可重试的失败项。" : "没有待备份的新文件。");

#if ANDROID
            foreach (var albumId in work.Select(w => w.Rule.RemoteAlbumId).Distinct())
            {
                token.ThrowIfCancellationRequested();
                BackupLog.Info($"预热 NAS 相册 id={albumId}…");
                await SynologyManager.Client.Foto.WarmupAlbumForBackupAsync(albumId, token);
            }
#endif

            Progress.Total = work.Count;

            Progress.ResetCounters();

            Notify();



            using var parallel = new SemaphoreSlim(MaxParallelUploads);

            var tasks = work.Select(item => ProcessOneAsync(item, mediaService, parallel, token)).ToList();

            await Task.WhenAll(tasks);

        }
        catch (Exception ex)
        {
            lock (Progress) { Progress.LastError = ex.Message; }
            BackupLog.Error("备份任务异常结束", ex);
            throw;
        }

        finally

        {

            ResetProgress(running: false);

            _retryFailedOnly = false;

            _runLock.Release();

        }

    }



    private async Task<List<WorkItem>> BuildWorkQueueAsync(ILocalMediaService mediaService, CancellationToken token)

    {

        var work = new List<WorkItem>();



        if (_retryFailedOnly)

        {

            var failed = await _db.GetFailedRecordsAsync();

            var rules = (await _db.GetRulesAsync()).ToDictionary(r => r.Id);

            foreach (var record in failed)

            {

                token.ThrowIfCancellationRequested();

                if (!rules.TryGetValue(record.RuleId, out var rule) || !rule.Enabled) continue;

                work.Add(new WorkItem(rule, new LocalMediaItem

                {

                    MediaStoreId = record.LocalMediaId,

                    ContentUri = record.ContentUri,

                    DisplayName = record.FileName,

                    Size = record.Size,

                    DateModified = record.DateModified,

                    MimeType = GuessMimeType(record.FileName),

                    LocalAlbumId = rule.LocalAlbumId

                }, record));

            }

            return work;

        }



        var enabledRules = (await _db.GetRulesAsync()).Where(r => r.Enabled).ToList();

        if (enabledRules.Count == 0)

            throw new InvalidOperationException("请先在备份规则中启用至少一条映射。");

        var completed = await _db.GetCompletedMediaKeysAsync();

        foreach (var rule in enabledRules)

        {

            var items = await mediaService.GetMediaItemsAsync(rule.LocalAlbumId);
            BackupLog.Info($"相册「{rule.LocalAlbumName}」共 {items.Count} 项，规则 → NAS「{rule.RemoteAlbumName}」");

            foreach (var item in items)

            {

                var key = BackupDatabase.BackupMediaKey(item.MediaStoreId, item.Size, item.DateModified);
                if (completed.Contains(key)) continue;

                work.Add(new WorkItem(rule, item, null));

            }

        }



        return work;

    }



    private async Task ProcessOneAsync(WorkItem workItem, ILocalMediaService mediaService, SemaphoreSlim parallel, CancellationToken token)

    {

        await parallel.WaitAsync(token);

        try

        {

            await WaitIfPausedAsync(token);



            var rule = workItem.Rule;

            var item = workItem.Media;

            lock (Progress)

            {

                Progress.CurrentFileName = item.DisplayName;

            }

            Notify();



            var record = workItem.ExistingRecord ?? new BackupRecord

            {

                RuleId = rule.Id,

                LocalMediaId = item.MediaStoreId,

                ContentUri = item.ContentUri,

                FileName = item.DisplayName,

                Size = item.Size,

                DateModified = item.DateModified

            };

            record.Status = BackupItemStatus.Uploading;

            record.LastError = null;

            await _db.UpsertRecordAsync(record);



            try

            {

#if ANDROID

                BackupLog.Info(
                    $"开始上传 {item.DisplayName} ({item.Size} bytes) → 相册 {rule.RemoteAlbumName} (id={rule.RemoteAlbumId})");

                var mtime = item.DateModified > 0 ? item.DateModified : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var result = await SynologyManager.Client.Foto.UploadToAlbumAsync(

                    ct => OpenFileStreamAsync(item.ContentUri),

                    item.DisplayName,

                    item.MimeType,

                    rule.RemoteAlbumId,

                    item.Size,

                    mtime,

                    remoteAlbumName: rule.RemoteAlbumName,

                    token);



                if (!result.VerifiedOnServer)

                    throw new Exception("NAS 未确认文件已成功入库。");



                record.Status = BackupItemStatus.Uploaded;

                record.RemotePhotoId = result.PhotoId;

                record.UploadedAt = DateTime.UtcNow;

                record.LastError = result.SkippedAsDuplicate ? "NAS 已有同名同大小文件（Search 跳过上传）" : null;

                await _db.UpsertRecordAsync(record);

                Progress.IncrementCompleted();

                BackupLog.Info($"上传成功 {item.DisplayName} photoId={result.PhotoId} skipped={result.SkippedAsDuplicate}");



                if (rule.DeleteAfterBackup)

                    await TryDeleteLocalAsync(mediaService, rule, item, record, result, token);

#else

                throw new PlatformNotSupportedException("备份仅支持 Android。");

#endif

            }

            catch (Exception ex) when (IsSessionError(ex))

            {

                record.Status = BackupItemStatus.Failed;

                record.LastError = "会话已过期，请重新登录";

                await _db.UpsertRecordAsync(record);

                Progress.IncrementFailed();

                lock (Progress) { Progress.LastError = record.LastError; }

                BackupLog.Warn($"会话错误 {item.DisplayName}: {ex.Message}");
                BackupLog.Error($"会话错误 {item.DisplayName}", ex);

                _cts?.Cancel();

            }

            catch (Exception ex)

            {

                record.Status = BackupItemStatus.Failed;

                record.LastError = FormatUploadError(ex);

                await _db.UpsertRecordAsync(record);

                Progress.IncrementFailed();

                lock (Progress) { Progress.LastError = record.LastError; }

                BackupLog.Warn($"上传失败 {item.DisplayName}: {record.LastError}");
                BackupLog.Error($"上传失败 {item.DisplayName}", ex);

            }



            Notify();

        }

        finally

        {

            parallel.Release();

        }

    }



#if ANDROID

    /// <summary>仅在 NAS 已确认存在/上传成功后才删除本地文件。</summary>

    private async Task TryDeleteLocalAsync(

        ILocalMediaService mediaService,

        BackupRule rule,

        LocalMediaItem item,

        BackupRecord record,

        UploadResult uploadResult,

        CancellationToken token)

    {

        token.ThrowIfCancellationRequested();



        if (record.Status != BackupItemStatus.Uploaded || !uploadResult.VerifiedOnServer)

        {

            record.LastError = "上传未确认，已跳过本地删除";

            await _db.UpsertRecordAsync(record);

            BackupLog.Info($"跳过删除（未确认上传）{item.DisplayName}");

            return;

        }



        if (_connection.GetConfirmBeforeDelete() && !_connection.HasAcknowledgedDeleteRisk())

        {

            record.LastError = "删除本地需先在「更多 → 连接设置」确认风险说明";

            record.Status = BackupItemStatus.DeleteFailed;

            await _db.UpsertRecordAsync(record);

            return;

        }



        var deleted = await mediaService.DeleteMediaAsync(item.ContentUri);

        if (deleted)

        {

            record.Status = BackupItemStatus.LocalDeleted;

            record.LastError = null;

            BackupLog.Info($"已删除本地 {item.DisplayName}");

        }

        else

        {

            record.Status = BackupItemStatus.DeleteFailed;

            record.LastError = "NAS 已确认备份，但本地删除失败（可能需系统授权）";

            BackupLog.Warn($"本地删除失败 {item.DisplayName}");

        }



        await _db.UpsertRecordAsync(record);

    }

#endif



    private async Task WaitIfPausedAsync(CancellationToken token)

    {

        while (_paused && !token.IsCancellationRequested)

            await Task.Delay(300, token);

    }



    private static bool IsSessionError(Exception ex) => NasSessionHelper.IsSessionError(ex);



#if ANDROID

    private static async Task<Stream> OpenFileStreamAsync(string contentUri)

    {

        var ctx = Platform.CurrentActivity ?? Android.App.Application.Context;

        var uri = Android.Net.Uri.Parse(contentUri);

        var stream = ctx.ContentResolver!.OpenInputStream(uri);

        if (stream == null)

            throw new IOException($"无法打开文件：{contentUri}");

        return stream;

    }

#endif



    private static string FormatUploadError(Exception ex)
    {
        if (ex is SynologyUploadException sx)
        {
            var raw = TrimForLog(sx.RawResponse, 120);
            return string.IsNullOrEmpty(raw) ? sx.Message : $"{sx.Message} [{raw}]";
        }
        if (ex.InnerException != null)
            return $"{ex.Message} | {ex.InnerException.Message}";
        return ex.Message;
    }

    private static string TrimForLog(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "…";

    private static string GuessMimeType(string fileName)

    {

        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch

        {

            ".mp4" or ".mov" or ".mkv" => "video/mp4",

            ".png" => "image/png",

            ".gif" => "image/gif",

            ".webp" => "image/webp",

            _ => "image/jpeg"

        };

    }



    private static bool IsOnWifi() =>

        Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.WiFi);



    private void ResetProgress(bool running)

    {

        Progress.IsRunning = running;

        if (!running)

        {

            Progress.CurrentFileName = null;

            _paused = false;

        }

        Notify();

    }



    private void Notify() => ProgressChanged?.Invoke(this, EventArgs.Empty);



    private sealed class WorkItem(BackupRule rule, LocalMediaItem media, BackupRecord? existing)

    {

        public BackupRule Rule { get; } = rule;

        public LocalMediaItem Media { get; } = media;

        public BackupRecord? ExistingRecord { get; } = existing;

    }

}


