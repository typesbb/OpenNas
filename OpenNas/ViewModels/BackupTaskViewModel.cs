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

public partial class BackupTaskViewModel : INotifyPropertyChanged, IDisposable
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
    private bool _rulesLoaded;

    public BackupTaskViewModel(BackupDatabase db, BackupEngine engine, ConnectionService connection)
    {
        _db = db;
        _engine = engine;
        _connection = connection;
        Rules = [];
        Rules = [];
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
        _engine.ProgressChanged -= OnEngineProgressChanged;
        _engine.ProgressChanged += OnEngineProgressChanged;
        if (!_rulesLoaded)
        {
            await LoadRulesAsync();
            _rulesLoaded = true;
        }
        else
        {
            var p = _engine.Progress;
            UpdateRuleStates(p.Completed, p.Total);
            _ = RefreshRulesAfterBackupAsync();
        }
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
        var p = _engine.Progress;
        UpdateRuleStates(p.Completed, p.Total);
        _rulesLoaded = true;
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
            await UiFeedback.ToastAsync("重试已开始，请勿强制退出应用");
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
            await UiFeedback.ToastAsync("备份已在后台运行，请勿强制退出应用");
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
        try
        {
            var rule = await BackupRuleCreator.CreateFromUserInputAsync(page, _db);
            if (rule == null)
                return;

            await LoadRulesAsync();
            await UiFeedback.ToastAsync("规则已添加");
        }
        catch (Exception ex)
        {
            AppLog.Error("添加备份规则失败", ex);
            await UiFeedback.AlertAsync(page, "错误", ex.Message);
        }
    }

    public async Task EditRuleAsync(Page page, BackupRuleItemViewModel item)
    {
        var rule = item.Rule;
        var action = await page.DisplayActionSheetAsync(
            rule.LocalAlbumName, "取消", "删除规则", "切换启用", "切换备份完成后删除");
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
        else if (action == "切换备份完成后删除")
        {
            if (!rule.DeleteAfterBackup)
            {
                var confirmed = await page.DisplayAlertAsync(
                    "⚠️ 风险确认",
                    "开启后将删除手机本地文件，不可恢复。\n\n请确认你已理解此风险。",
                    "确认开启", "取消");
                if (!confirmed) return;
                _connection.SetAcknowledgedDeleteRisk(true);
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
        GC.SuppressFinalize(this);
    }

    public void Detach()
    {
        _engine.ProgressChanged -= OnEngineProgressChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
