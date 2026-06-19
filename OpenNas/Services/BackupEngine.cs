using System.Collections.Concurrent;
using NSynology;

using NSynology.Foto;

using OpenNas.Data;

using OpenNas.Media;

using OpenNas.Models;



namespace OpenNas.Services;



public class BackupEngine

{

    /// <summary>3 路并行加载，同时仅 1 路上传。</summary>
    private const int SlotCount = BackupQueueTracker.SlotCount;
    private const int MaxParallelUpload = 1;
    /// <summary>超过此大小或大小未知时走流式上传，避免整文件进内存 OOM。</summary>
    private const long InMemoryUploadThreshold = SynologyClient.InMemoryUploadMaxBytes;

    private readonly BackupDatabase _db;

    private readonly ConnectionService _connection;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    private CancellationTokenSource? _cts;

    private volatile bool _paused;

    private bool _retryFailedOnly;

    private int? _ruleIdFilter;

    private readonly BackupQueueTracker _queue = new();

    public BackupProgressInfo Progress { get; } = new();

    public event EventHandler? ProgressChanged;



    public BackupEngine(BackupDatabase db, ConnectionService connection)
    {
        _db = db;
        _connection = connection;
    }



    public IReadOnlyList<BackupQueueItem> GetQueueSnapshot(int? ruleId = null) =>
        _queue.GetVisibleSnapshot(ruleId);

    /// <summary>仅在有汇总字段变化时触发 UI/通知刷新（非逐字节进度）。</summary>
    public bool HasSummaryChangedSince(BackupSummarySnapshot previous)
    {
        var p = Progress;
        return p.IsRunning != previous.IsRunning
            || p.IsPaused != previous.IsPaused
            || p.Completed != previous.Completed
            || p.Total != previous.Total
            || p.Failed != previous.Failed
            || p.ActiveRuleId != previous.ActiveRuleId;
    }

    public BackupSummarySnapshot CaptureSummarySnapshot() => new(Progress);

    public void Pause()
    {
        _paused = true;
        Progress.IsPaused = true;
        Notify(force: true);
    }

    public void Resume()
    {
        _paused = false;
        Progress.IsPaused = false;
        Notify(force: true);
    }

    public void Cancel() => _cts?.Cancel();



    public Task RunBackupAsync(ILocalMediaService mediaService, bool retryFailedOnly = false, int? ruleId = null)

    {

        _retryFailedOnly = retryFailedOnly;

        _ruleIdFilter = ruleId;

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

            _queue.Reset();

            var activeRuleIds = work.Select(w => w.Rule.Id).Distinct().ToList();
            lock (Progress)
            {
                Progress.ActiveRuleId = _ruleIdFilter
                    ?? (activeRuleIds.Count == 1 ? activeRuleIds[0] : null);
                Progress.ActiveRuleLabel = BuildActiveRuleLabel(work);
            }

            Notify(force: true);

            await RunSlotWorkersAsync(work, mediaService, token);

            BackupLog.Info(
                $"备份完成 合计={Progress.Total} 成功={Progress.Completed} 失败={Progress.Failed}");
            if (Progress.Failed > 0)
                BackupLog.Warn("部分文件未进入目标相册，请在任务页对该规则点「重试」");

        }
        catch (Exception ex)
        {
            lock (Progress) { Progress.LastError = ex.Message; }
            BackupLog.Error("备份任务异常结束", ex);
            throw;
        }

        finally

        {

            var cts = Interlocked.Exchange(ref _cts, null);
            cts?.Dispose();
            ResetProgress(running: false);

            _retryFailedOnly = false;

            _ruleIdFilter = null;

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

                if (_ruleIdFilter is int filterId && rule.Id != filterId) continue;

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

        if (_ruleIdFilter is int ruleId)
            enabledRules = enabledRules.Where(r => r.Id == ruleId).ToList();

        if (enabledRules.Count == 0)

            throw new InvalidOperationException(_ruleIdFilter.HasValue
                ? "该规则未启用或不存在。"
                : "请先添加并启用至少一条备份规则。");

        foreach (var rule in enabledRules)

        {

            var completed = await _db.GetCompletedMediaKeysForRuleAsync(rule.Id);
            var items = await mediaService.GetMediaItemsAsync(rule.LocalAlbumId);
            BackupLog.Info($"相册「{rule.LocalAlbumName}」共 {items.Count} 项，规则 → NAS「{rule.RemoteAlbumName}」");

            foreach (var item in items)

            {

                var key = BackupDatabase.BackupMediaKey(item.MediaStoreId, item.Size, item.DateModified);
                if (completed.Contains(key)) continue;

                var existing = await _db.FindOpenRecordAsync(
                    item.MediaStoreId, item.Size, item.DateModified);

                work.Add(new WorkItem(rule, item, existing));

            }

        }



        return work;

    }



    private async Task RunSlotWorkersAsync(
        List<WorkItem> work,
        ILocalMediaService mediaService,
        CancellationToken token)
    {
        var nextIndex = 0;
        var assignedKeys = new ConcurrentDictionary<string, byte>();

        WorkItem? TakeNext()
        {
            while (true)
            {
                var index = Interlocked.Increment(ref nextIndex) - 1;
                if (index >= work.Count)
                    return null;

                var candidate = work[index];
                var key = MediaKey(candidate.Media);
                if (assignedKeys.TryAdd(key, 0))
                    return candidate;
            }
        }

        void ReleaseKey(string key)
        {
            assignedKeys.TryRemove(key, out _);
        }

        var uploadSem = new SemaphoreSlim(MaxParallelUpload, MaxParallelUpload);

        async Task RunSlotAsync(int slotIndex)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(token);

                var workItem = TakeNext();
                if (workItem is null)
                    break;

                var key = MediaKey(workItem.Media);
                try
                {
                    await RunSlotWorkAsync(slotIndex, workItem, key, mediaService, uploadSem, token);
                }
                catch (Exception ex) when (IsSessionError(ex))
                {
                    BackupLog.Warn($"会话错误 {workItem.Media.DisplayName}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    BackupLog.Warn($"处理失败 {workItem.Media.DisplayName}: {ex.Message}");
                    Progress.IncrementFailed();
                }
                finally
                {
                    ReleaseKey(key);
                    _queue.ClearSlot(slotIndex);
                    Notify(force: true);
                }
            }
        }

        var slotTasks = Enumerable.Range(0, SlotCount)
            .Select(RunSlotAsync)
            .ToArray();
        await Task.WhenAll(slotTasks);
    }

    private async Task RunSlotWorkAsync(
        int slotIndex,
        WorkItem workItem,
        string key,
        ILocalMediaService mediaService,
        SemaphoreSlim uploadSem,
        CancellationToken token)
    {
        _queue.AssignSlot(slotIndex, new BackupQueueItem
        {
            SlotIndex = slotIndex,
            Key = key,
            FileName = workItem.Media.DisplayName,
            ContentUri = workItem.Media.ContentUri,
            RuleId = workItem.Rule.Id,
            Stage = BackupQueueStage.Loading,
            Progress = 0
        });
        Notify(force: true);

        var useStreaming = ShouldStreamUpload(workItem.Media);

        byte[]? bytes = null;
        if (useStreaming)
        {
            if (_queue.SetSlotReady(slotIndex))
                Notify(force: true);
        }
        else
        {
#if ANDROID
            bytes = await LoadBytesAsync(
                workItem.Media,
                token,
                p =>
                {
                    if (_queue.UpdateSlotProgress(slotIndex, p))
                        Notify();
                });
#endif

            if (_queue.SetSlotReady(slotIndex))
                Notify(force: true);
        }

        await WaitIfPausedAsync(token);

        await uploadSem.WaitAsync(token);
        try
        {
            await WaitIfPausedAsync(token);

            if (_queue.SetSlotUploading(slotIndex))
                Notify(force: true);

            await UploadWorkItemAsync(workItem, key, bytes, mediaService, token, slotIndex);
        }
        finally
        {
            uploadSem.Release();
        }
    }

    private static bool ShouldStreamUpload(LocalMediaItem item) =>
        item.Size <= 0 || item.Size > InMemoryUploadThreshold;

    private async Task UploadWorkItemAsync(
        WorkItem workItem,
        string key,
        byte[]? bytes,
        ILocalMediaService mediaService,
        CancellationToken token,
        int slotIndex)
    {
        var rule = workItem.Rule;
        var item = workItem.Media;

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
            lock (Progress)
            {
                Progress.CurrentFileName = item.DisplayName;
                Progress.CurrentContentUri = item.ContentUri;
                Progress.CurrentMimeType = item.MimeType;
            }

            BackupLog.Info(
                $"开始上传 {item.DisplayName} ({item.Size} bytes) → 相册 {rule.RemoteAlbumName} (id={rule.RemoteAlbumId})");

            var uploadFileSize = item.Size;
            if (uploadFileSize <= 0)
            {
                uploadFileSize = await ResolveContentSizeAsync(item.ContentUri);
                if (uploadFileSize > 0)
                    BackupLog.Info($"MediaStore 未返回大小，ContentResolver 测得 {uploadFileSize} bytes");
            }

            Notify(force: true);

            var mtime = item.DateModified > 0 ? item.DateModified : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var uploadProgress = new Progress<double>(p =>
            {
                if (_queue.UpdateSlotUploadProgress(slotIndex, p))
                    Notify();
            });

            UploadResult result;
            if (bytes is not null)
            {
                result = await SynologyManager.Client.Foto.UploadToAlbumFromBytesAsync(
                    bytes,
                    item.DisplayName,
                    item.MimeType,
                    rule.RemoteAlbumId,
                    uploadFileSize,
                    mtime,
                    uploadProgress,
                    token);
            }
            else
            {
                result = await SynologyManager.Client.Foto.UploadToAlbumAsync(
                    async ct =>
                    {
                        var stream = await OpenFileStreamAsync(item.ContentUri);
                        return stream;
                    },
                    item.DisplayName,
                    item.MimeType,
                    rule.RemoteAlbumId,
                    uploadFileSize,
                    mtime,
                    uploadProgress,
                    cancellationToken: token);
            }

            if (!result.VerifiedOnServer)
            {
                var detail = result.PhotoId > 0
                    ? $"action={result.Action} photoId={result.PhotoId}"
                    : "NAS 未返回有效 photoId";
                throw new Exception($"文件未出现在目标相册（{detail}）");
            }

            record.Status = BackupItemStatus.Uploaded;
            record.RemotePhotoId = result.PhotoId;
            record.UploadedAt = DateTime.UtcNow;
            record.LastError = result.SkippedAsDuplicate ? "NAS 已有同名同大小文件（Search 跳过上传）" : null;
            await _db.UpsertRecordAsync(record);

            Progress.IncrementCompleted();
            BackupLog.Info(
                $"上传成功 {item.DisplayName} photoId={result.PhotoId} action={result.Action} skipped={result.SkippedAsDuplicate}");

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
        }
        catch (OperationCanceledException ex)
        {
            record.Status = BackupItemStatus.Failed;
            record.LastError = "上传已取消";
            await _db.UpsertRecordAsync(record);
            Progress.IncrementFailed();
            BackupLog.Warn($"上传取消 {item.DisplayName}: {ex.Message}");
        }
        catch (Exception ex)
        {
            record.Status = BackupItemStatus.Failed;
            record.LastError = ex is OutOfMemoryException
                ? "文件过大，内存不足"
                : FormatUploadError(ex);
            await _db.UpsertRecordAsync(record);
            Progress.IncrementFailed();
            lock (Progress) { Progress.LastError = record.LastError; }
            BackupLog.Warn($"上传失败 {item.DisplayName}: {record.LastError}");
        }
    }

#if ANDROID
    private Task<byte[]> LoadBytesAsync(
        LocalMediaItem item,
        CancellationToken token,
        Action<double>? reportProgress = null)
    {
        return LoadBytesCoreAsync(item, token, reportProgress);
    }

    private async Task<byte[]> LoadBytesCoreAsync(
        LocalMediaItem item,
        CancellationToken token,
        Action<double>? reportProgress)
    {
        await using var stream = await OpenFileStreamAsync(item.ContentUri);
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            long totalRead = 0;
            var sizeHint = item.Size > 0 ? item.Size : 0L;
            if (sizeHint > InMemoryUploadThreshold)
                throw new InvalidOperationException($"文件过大（{sizeHint} 字节），应使用流式上传。");

            while (true)
            {
                token.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(token);

                var read = await stream.ReadAsync(buffer, token);
                if (read <= 0) break;
                ms.Write(buffer, 0, read);
                totalRead += read;
                if (totalRead > InMemoryUploadThreshold)
                    throw new InvalidOperationException($"文件过大（{totalRead} 字节），应使用流式上传。");
                if (sizeHint > 0 && !_paused)
                    reportProgress?.Invoke(Math.Min(1.0, (double)totalRead / sizeHint));
            }

            if (!_paused)
                reportProgress?.Invoke(1.0);
            return ms.ToArray();
    }
#endif

    private static string MediaKey(LocalMediaItem item) =>
        BackupDatabase.BackupMediaKey(item.MediaStoreId, item.Size, item.DateModified);

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

    private static Task<long> ResolveContentSizeAsync(string contentUri)
    {
        var ctx = Platform.CurrentActivity ?? Android.App.Application.Context;
        var uri = Android.Net.Uri.Parse(contentUri);
        try
        {
            using var afd = ctx.ContentResolver!.OpenAssetFileDescriptor(uri, "r");
            if (afd?.Length >= 0)
                return Task.FromResult(afd.Length);
        }
        catch (Exception ex)
        {
            BackupLog.Warn($"无法通过 ContentResolver 获取文件大小: {ex.Message}");
        }

        return Task.FromResult(0L);
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



    private static string BuildActiveRuleLabel(List<WorkItem> work)
    {
        var rules = work.Select(w => w.Rule).GroupBy(r => r.Id).Select(g => g.First()).ToList();
        if (rules.Count == 1)
            return TrimRuleLabel(rules[0]);

        return $"{rules.Count} 条规则";
    }

    private static string TrimRuleLabel(BackupRule rule)
    {
        var label = $"{rule.LocalAlbumName}→{rule.RemoteAlbumName}";
        if (label.Length <= 22)
            return label;

        if (rule.LocalAlbumName.Length <= 18)
            return rule.LocalAlbumName;

        return rule.LocalAlbumName[..15] + "…";
    }

    private void ResetProgress(bool running)

    {

        Progress.IsRunning = running;

        if (!running)

        {

            Progress.CurrentFileName = null;

            Progress.CurrentContentUri = null;

            Progress.CurrentMimeType = null;

            Progress.ActiveRuleId = null;

            Progress.ActiveRuleLabel = null;

            Progress.IsPaused = false;

            _paused = false;

            _queue.Reset();

        }

        Notify(force: true);

    }



    private void Notify(bool force = false)
    {
        if (!force && _paused)
            return;
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }



    private sealed class WorkItem(BackupRule rule, LocalMediaItem media, BackupRecord? existing)

    {

        public BackupRule Rule { get; } = rule;

        public LocalMediaItem Media { get; } = media;

        public BackupRecord? ExistingRecord { get; } = existing;

    }

}


