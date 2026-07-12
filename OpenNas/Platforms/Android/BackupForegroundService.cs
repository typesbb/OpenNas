using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using OpenNas.Media;
using OpenNas.Services;

namespace OpenNas.Platforms.Android;

[Service(ForegroundServiceType = ForegroundService.TypeDataSync)]
public class BackupForegroundService : Service
{
    public const string ExtraRetryFailed = "retry_failed";
    private const int NotificationId = 1001;
    private const string ChannelId = "opennas_backup";
    private const string NotificationTitle = "相册备份";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var retryFailed = intent?.GetBooleanExtra(ExtraRetryFailed, false) ?? false;
        var ruleId = intent?.HasExtra(BackupServiceStarter.ExtraRuleId) == true
            ? intent.GetIntExtra(BackupServiceStarter.ExtraRuleId, -1)
            : (int?)null;
        if (ruleId is <= 0)
            ruleId = null;
        CreateChannel();
        StartForeground(NotificationId, BuildNotification("准备中…", progress: 0, max: 1, done: false));
        _ = RunBackupAsync(startId, retryFailed, ruleId);
        return StartCommandResult.NotSticky;
    }

    private async Task RunBackupAsync(int startId, bool retryFailed, int? ruleId)
    {
        BackupLog.Info($"前台服务启动 backup retryFailed={retryFailed} ruleId={ruleId?.ToString() ?? "all"}");
        try
        {
            if (!await MediaPermissions.EnsureReadMediaAsync())
            {
                var nm = (NotificationManager?)GetSystemService(NotificationService);
                nm?.Notify(NotificationId, BuildNotification("缺少照片/视频权限", progress: 0, max: 1, done: true));
                return;
            }

            var engine = App.Services.GetRequiredService<BackupEngine>();
            engine.ProgressChanged += (_, _) => UpdateNotification(engine);
            var media = new LocalMediaService();
            await engine.RunBackupAsync(media, retryFailed, ruleId);
            UpdateNotification(engine, done: true);
        }
        catch (Exception ex)
        {
            BackupLog.Error("前台备份任务失败", ex);
            var nm = (NotificationManager?)GetSystemService(NotificationService);
            nm?.Notify(NotificationId, BuildNotification(TrimMessage($"失败：{ex.Message}", 48), progress: 0, max: 1, done: true));
        }
        finally
        {
            await Task.Delay(1500);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                StopForeground(StopForegroundFlags.Remove);
            StopSelfResult(startId);
        }
    }

    private void UpdateNotification(BackupEngine engine, bool done = false)
    {
        var p = engine.Progress;
        var max = Math.Max(p.Total, 1);
        var ruleLabel = string.IsNullOrWhiteSpace(p.ActiveRuleLabel) ? "…" : p.ActiveRuleLabel;

        string content;
        if (done)
        {
            content = p.Failed > 0
                ? $"{ruleLabel} · 完成 {p.Completed}/{p.Total}，失败 {p.Failed}"
                : $"{ruleLabel} · 完成 {p.Completed}/{p.Total}";
            var pendingDeletes = BackupPendingDeleteHelper.PendingCount;
            if (pendingDeletes > 0)
                content += $" · 待确认删除 {pendingDeletes} 项";
        }
        else if (p.IsPaused)
            content = $"{ruleLabel} · 已暂停 {p.Completed}/{p.Total}";
        else
            content = $"{ruleLabel} · {p.Completed}/{p.Total}";

        var nm = (NotificationManager?)GetSystemService(NotificationService);
        nm?.Notify(NotificationId, BuildNotification(TrimMessage(content, 48), p.Completed, max, done));
    }

    private Notification BuildNotification(string content, int progress, int max, bool done = false)
    {
        var launchIntent = new Intent(this, typeof(global::OpenNas.MainActivity));
        launchIntent.SetAction(Intent.ActionMain);
        launchIntent.AddCategory(Intent.CategoryLauncher);
        launchIntent.PutExtra(global::OpenNas.MainActivity.ExtraOpenTab, "tasks");
        launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        var contentPending = PendingIntent.GetActivity(
            this,
            0,
            launchIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(NotificationTitle)
            .SetContentText(content)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentIntent(contentPending)
            .SetAutoCancel(done)
            .SetOnlyAlertOnce(true);

        if (done || progress >= max)
            builder.SetOngoing(false);
        else
            builder.SetOngoing(true);

        if (max > 0 && !done)
            builder.SetProgress(max, progress, false);

        return builder.Build();
    }

    private static string TrimMessage(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + "…";

    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var channel = new NotificationChannel(ChannelId, NotificationTitle, NotificationImportance.Low);
        var nm = (NotificationManager?)GetSystemService(NotificationService);
        nm?.CreateNotificationChannel(channel);
    }
}

public static class BackupServiceStarter
{
    public const string ExtraRuleId = "rule_id";

    public static Task StartAsync(bool retryFailedOnly = false, int? ruleId = null)
    {
        var ctx = Platform.CurrentActivity ?? global::Android.App.Application.Context;
        var intent = new Intent(ctx, Java.Lang.Class.FromType(typeof(BackupForegroundService)));
        intent.PutExtra(BackupForegroundService.ExtraRetryFailed, retryFailedOnly);
        if (ruleId is int id)
            intent.PutExtra(ExtraRuleId, id);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            ctx.StartForegroundService(intent);
        else
            ctx.StartService(intent);
        return Task.CompletedTask;
    }
}
