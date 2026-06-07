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

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var retryFailed = intent?.GetBooleanExtra(ExtraRetryFailed, false) ?? false;
        CreateChannel();
        StartForeground(NotificationId, BuildNotification("准备备份...", 0, 1));
        _ = RunBackupAsync(startId, retryFailed);
        return StartCommandResult.NotSticky;
    }

    private async Task RunBackupAsync(int startId, bool retryFailed)
    {
        BackupLog.Info($"前台服务启动 backup retryFailed={retryFailed}");
        try
        {
            if (!await MediaPermissions.EnsureReadMediaAsync())
            {
                var nm = (NotificationManager?)GetSystemService(NotificationService);
                nm?.Notify(NotificationId, BuildNotification("缺少照片/视频读取权限，请在应用中授权后重试", 0, 1));
                return;
            }

            var engine = AppServices.GetRequired<BackupEngine>();
            engine.ProgressChanged += (_, _) => UpdateNotification(engine);
            var media = new LocalMediaService();
            await engine.RunBackupAsync(media, retryFailed);
            UpdateNotification(engine, done: true);
        }
        catch (Exception ex)
        {
            BackupLog.Error("前台备份任务失败", ex);
            var nm = (NotificationManager?)GetSystemService(NotificationService);
            nm?.Notify(NotificationId, BuildNotification($"备份失败: {ex.Message}", 0, 1));
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
        var text = done
            ? $"完成 {p.Completed}/{p.Total}，失败 {p.Failed}"
            : $"{p.Completed}/{p.Total} - {p.CurrentFileName}";
        var max = Math.Max(p.Total, 1);
        var nm = (NotificationManager?)GetSystemService(NotificationService);
        nm?.Notify(NotificationId, BuildNotification(text ?? "备份中", p.Completed, max));
    }

    private Notification BuildNotification(string text, int progress, int max)
    {
        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("OpenNas 备份")
            .SetContentText(text)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetOngoing(progress < max)
            .SetOnlyAlertOnce(true);
        if (max > 0)
            builder.SetProgress(max, progress, false);
        return builder.Build();
    }

    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var channel = new NotificationChannel(ChannelId, "OpenNas 备份", NotificationImportance.Low);
        var nm = (NotificationManager?)GetSystemService(NotificationService);
        nm?.CreateNotificationChannel(channel);
    }
}

public static class BackupServiceStarter
{
    public static Task StartAsync(bool retryFailedOnly = false)
    {
        var ctx = Platform.CurrentActivity ?? global::Android.App.Application.Context;
        var intent = new Intent(ctx, Java.Lang.Class.FromType(typeof(BackupForegroundService)));
        intent.PutExtra(BackupForegroundService.ExtraRetryFailed, retryFailedOnly);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            ctx.StartForegroundService(intent);
        else
            ctx.StartService(intent);
        return Task.CompletedTask;
    }
}
