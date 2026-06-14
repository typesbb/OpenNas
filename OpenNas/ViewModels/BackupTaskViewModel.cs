using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NSynology;
using NSynology.Foto;
using OpenNas;
using OpenNas.Data;
using OpenNas.Helpers;
using OpenNas.Models;
using OpenNas.Services;

namespace OpenNas.ViewModels;

public class BackupTaskViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly BackupDatabase _db;
    private readonly BackupEngine _engine;
    private readonly ConnectionService _connection;

    private bool _wasRunning;
    private bool _isRunning;
    private bool _isPaused;
    private bool _updateScheduled;
    private bool _lastKnownPaused;
    private BackupSummarySnapshot _lastSummary;

    public BackupTaskViewModel(BackupDatabase db, BackupEngine engine, ConnectionService connection)
    {
        _db = db;
        _engine = engine;
        _connection = connection;
        Rules = new ObservableCollection<BackupRuleItemViewModel>();
        _engine.ProgressChanged += OnEngineProgressChanged;
    }

    public ObservableCollection<BackupRuleItemViewModel> Rules { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            OnPropertyChanged();
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (_isPaused == value) return;
            _isPaused = value;
            OnPropertyChanged();
        }
    }

    public async Task RefreshAsync()
    {
        await LoadRulesAsync();
        _lastSummary = default;
        UpdateFromEngine(force: true);
    }

    public async Task LoadRulesAsync()
    {
        var rules = await _db.GetRulesAsync();
        Rules.Clear();
        foreach (var rule in rules)
        {
            var failed = await _db.CountFailedByRuleAsync(rule.Id);
            Rules.Add(new BackupRuleItemViewModel(rule, failed));
        }
        UpdateRuleStates();
    }

    public async Task ToggleRuleActionAsync(Page page, BackupRuleItemViewModel item)
    {
        var p = _engine.Progress;
        if (p.IsRunning && p.ActiveRuleId == item.Id)
        {
            if (p.IsPaused)
                _engine.Resume();
            else
                _engine.Pause();

            p = _engine.Progress;
            IsPaused = p.IsPaused;
            _lastSummary = _engine.CaptureSummarySnapshot();
            UpdateRuleStates(p.Completed, p.Total);
            return;
        }

        if (p.IsRunning)
            return;

        await StartRuleAsync(page, item.Id);
    }

    public async Task RetryRuleAsync(Page page, BackupRuleItemViewModel item)
    {
        if (_engine.Progress.IsRunning)
            return;

        if (!await EnsureLoggedInAsync(page))
            return;

#if ANDROID
        if (!await EnsurePermissionsAsync(page))
            return;

        try
        {
            await MediaPermissions.EnsureNotificationsAsync();
            await Platforms.Android.BackupServiceStarter.StartAsync(retryFailedOnly: true, ruleId: item.Id);
        }
        catch (Exception ex)
        {
            AppLog.Error("重试规则失败项失败", ex);
            await UiFeedback.AlertAsync(page, "备份", ex.Message);
        }
#else
        await UiFeedback.AlertAsync(page, "提示", "相册备份目前仅支持 Android。");
#endif
    }

    public async Task StartRuleAsync(Page page, int ruleId)
    {
        if (!await EnsureLoggedInAsync(page))
            return;

#if ANDROID
        if (!await EnsurePermissionsAsync(page))
            return;

        try
        {
            await MediaPermissions.EnsureNotificationsAsync();
            await Platforms.Android.BackupServiceStarter.StartAsync(ruleId: ruleId);
        }
        catch (Exception ex)
        {
            AppLog.Error("启动规则备份失败", ex);
            await UiFeedback.AlertAsync(page, "备份", ex.Message);
        }
#else
        await UiFeedback.AlertAsync(page, "提示", "相册备份目前仅支持 Android。");
#endif
    }

    public async Task AddRuleAsync(Page page)
    {
#if !ANDROID
        await UiFeedback.AlertAsync(page, "提示", "备份规则仅支持 Android。");
        return;
#else
        try
        {
            if (!await EnsurePermissionsAsync(page))
                return;

            var media = new Media.LocalMediaService();
            var localAlbums = await media.GetLocalAlbumsAsync();
            if (localAlbums.Count == 0)
            {
                await UiFeedback.AlertAsync(page, "提示", "未找到本机相册");
                return;
            }

            var localNames = localAlbums.Select(a => a.Name).ToArray();
            var pickLocal = await page.DisplayActionSheet("选择本机相册", "取消", null, localNames);
            if (pickLocal is null or "取消") return;
            var local = localAlbums.First(a => a.Name == pickLocal);

            var nasAlbums = (await SynologyManager.Client.Foto.GetAlbumsAsync(0, 100)).ToList();
            var nasNames = nasAlbums.Select(a => a.Name).ToArray();
            var pickNas = await page.DisplayActionSheet("选择 NAS 相册", "取消", "新建相册...", nasNames);
            if (pickNas is null or "取消") return;

            Album remote;
            if (pickNas == "新建相册...")
            {
                var newName = await page.DisplayPromptAsync("新建相册", "相册名称");
                if (string.IsNullOrWhiteSpace(newName)) return;
                remote = await SynologyManager.Client.Foto.CreateNormalAlbumAsync(newName);
            }
            else
                remote = nasAlbums.First(a => a.Name == pickNas);

            var delete = await page.DisplayAlert(
                "备份后删除", "是否在备份成功后删除手机上的原文件？", "是", "否");

            var rule = new BackupRule
            {
                LocalAlbumId = local.Id,
                LocalAlbumName = local.Name,
                RemoteAlbumId = remote.Id,
                RemoteAlbumName = remote.Name,
                Enabled = true,
                DeleteAfterBackup = delete
            };
            await _db.SaveRuleAsync(rule);
            await LoadRulesAsync();
            await UiFeedback.ToastAsync("规则已添加");
        }
        catch (Exception ex)
        {
            AppLog.Error("添加备份规则失败", ex);
            await UiFeedback.AlertAsync(page, "错误", ex.Message);
        }
#endif
    }

    public async Task EditRuleAsync(Page page, BackupRuleItemViewModel item)
    {
        var rule = item.Rule;
        var action = await page.DisplayActionSheet(
            rule.LocalAlbumName, "取消", "删除规则", "切换启用", "切换备份后删除");
        if (action == "删除规则")
        {
            await _db.DeleteRuleAsync(rule.Id);
            await LoadRulesAsync();
            await UiFeedback.ToastAsync("规则已删除");
        }
        else if (action == "切换启用")
        {
            rule.Enabled = !rule.Enabled;
            await _db.SaveRuleAsync(rule);
            await LoadRulesAsync();
        }
        else if (action == "切换备份后删除")
        {
            if (!rule.DeleteAfterBackup)
            {
                var ok = await page.DisplayAlert(
                    "备份后删除",
                    "开启后，文件成功上传到 NAS 后将尝试删除手机本地副本。是否开启？",
                    "开启", "取消");
                if (!ok) return;
            }
            rule.DeleteAfterBackup = !rule.DeleteAfterBackup;
            await _db.SaveRuleAsync(rule);
            await LoadRulesAsync();
        }
    }

    private static async Task<bool> EnsurePermissionsAsync(Page page)
    {
#if ANDROID
        if (await MediaPermissions.EnsureReadMediaAsync())
            return true;

        await UiFeedback.AlertAsync(page, "需要权限", "请允许访问照片和视频后再备份。");
        return false;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    private async Task<bool> EnsureLoggedInAsync(Page page)
    {
        if (_connection.IsLoggedIn)
            return true;

        var goLogin = await UiFeedback.ConfirmAsync(
            page,
            "需要登录",
            "NAS 会话已过期或未登录，请重新登录后再备份。",
            "去登录",
            "取消");
        if (goLogin && Application.Current?.Windows.Count > 0)
            Application.Current.Windows[0].Page = new NavigationPage(AppServices.GetRequired<LoginPage>());

        return false;
    }

    private void OnEngineProgressChanged(object? sender, EventArgs e)
    {
        if (_updateScheduled)
            return;

        _updateScheduled = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _updateScheduled = false;
            UpdateFromEngine();
        });
    }

    private void UpdateFromEngine(bool force = false)
    {
        var p = _engine.Progress;
        var summaryChanged = force || _engine.HasSummaryChangedSince(_lastSummary);

        if (summaryChanged)
        {
            _lastSummary = _engine.CaptureSummarySnapshot();
            IsRunning = p.IsRunning;
            IsPaused = p.IsPaused;
            UpdateRuleStates(p.Completed, p.Total);
        }

        var pauseToggled = p.IsPaused != _lastKnownPaused;
        _lastKnownPaused = p.IsPaused;

        if (p.IsRunning && (!pauseToggled || !p.IsPaused))
            SyncQueuesFromEngine();
        else if (_wasRunning && !p.IsRunning)
        {
            ClearAllRuleQueues();
            _ = RefreshRulesAfterBackupAsync();
        }
        else if (force)
            SyncQueuesFromEngine();

        _wasRunning = p.IsRunning;
    }

    private void SyncQueuesFromEngine()
    {
        var p = _engine.Progress;
        if (p.IsRunning && p.ActiveRuleId is int activeId)
        {
            Rules.FirstOrDefault(r => r.Id == activeId)
                ?.SyncQueueItems(_engine.GetQueueSnapshot(activeId));
            return;
        }

        foreach (var rule in Rules)
            rule.SyncQueueItems(_engine.GetQueueSnapshot(rule.Id));
    }

    private void ClearAllRuleQueues()
    {
        foreach (var rule in Rules)
            rule.ClearQueueItems();
    }

    private void UpdateRuleStates(int completed = 0, int total = 0)
    {
        var p = _engine.Progress;
        foreach (var rule in Rules)
            rule.ApplyEngineState(
                p.IsRunning,
                p.IsPaused,
                p.ActiveRuleId,
                completed,
                total);
    }

    private async Task RefreshRulesAfterBackupAsync()
    {
        foreach (var ruleVm in Rules)
        {
            var failed = await _db.CountFailedByRuleAsync(ruleVm.Id);
            ruleVm.SetFailedCount(failed);
        }
    }

    public void Dispose()
    {
        _engine.ProgressChanged -= OnEngineProgressChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
