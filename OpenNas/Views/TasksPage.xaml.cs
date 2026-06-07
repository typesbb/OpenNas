using OpenNas.Data;
using OpenNas.Models;
using OpenNas.Services;
using OpenNas.ViewModels;

namespace OpenNas.Views;

public partial class TasksPage : ContentPage
{
    private readonly BackupEngine _backup;
    private readonly BackupDatabase _db;
    private readonly BackupTaskViewModel _vm;

    public TasksPage(BackupEngine backup, BackupDatabase db, BackupTaskViewModel vm)
    {
        InitializeComponent();
        _backup = backup;
        _db = db;
        _vm = vm;
        RulesHost.Content = new BackupRulesView(db);
        BindingContext = _vm;

        _backup.ProgressChanged += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateProgressUi);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshStatsAsync();
        await _vm.RefreshAsync();
        UpdateProgressUi();
    }

    private async Task RefreshStatsAsync()
    {
        var uploaded = await _db.CountByStatusAsync(BackupItemStatus.Uploaded, BackupItemStatus.LocalDeleted);
        var failed = await _db.CountByStatusAsync(BackupItemStatus.Failed, BackupItemStatus.DeleteFailed);
        StatsLabel.Text = $"累计成功 {uploaded}，失败 {failed}";
    }

    private void UpdateProgressUi()
    {
        ProgressLabel.Text = _vm.ProgressText;
        PauseButton.Text = _backup.Progress.IsPaused ? "继续" : "暂停";
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await _vm.RefreshAsync();
        await RefreshStatsAsync();
        UpdateProgressUi();
    }

    private async void OnStartBackupClicked(object sender, EventArgs e)
    {
#if ANDROID
        try
        {
            if (!await MediaPermissions.EnsureReadMediaAsync())
            {
                await DisplayAlert("需要权限", "请允许访问照片和视频后再备份。", "确定");
                return;
            }
            await MediaPermissions.EnsureNotificationsAsync();
            await Platforms.Android.BackupServiceStarter.StartAsync();
            await RefreshStatsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("启动备份失败", ex);
            await DisplayAlert("备份", ex.Message, "确定");
        }
#else
        await DisplayAlert("提示", "相册备份目前仅支持 Android。", "确定");
#endif
    }

    private void OnPauseClicked(object sender, EventArgs e)
    {
        if (_backup.Progress.IsPaused)
            _backup.Resume();
        else
            _backup.Pause();
        UpdateProgressUi();
    }

    private async void OnRetryFailedClicked(object sender, EventArgs e)
    {
#if ANDROID
        try
        {
            if (!await MediaPermissions.EnsureReadMediaAsync())
            {
                await DisplayAlert("需要权限", "请允许访问照片和视频后再重试。", "确定");
                return;
            }
            await MediaPermissions.EnsureNotificationsAsync();
            await Platforms.Android.BackupServiceStarter.StartAsync(retryFailedOnly: true);
            await RefreshStatsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("重试备份失败", ex);
            await DisplayAlert("重试", ex.Message, "确定");
        }
#else
        await DisplayAlert("提示", "仅支持 Android。", "确定");
#endif
    }
}
