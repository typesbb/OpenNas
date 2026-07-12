using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using OpenNas.Services;

namespace OpenNas.Platforms.Android;

/// <summary>
/// Android 11+ 删除媒体须用户确认；后台备份完成后持久化待删 URI，回到 Activity 时再弹出系统对话框。
/// </summary>
internal static class BackupPendingDeleteHelper
{
    public const int DeleteRequestCode = 1002;
    private const string PrefsName = "opennas_backup";
    private const string PrefKeyPendingUris = "pending_delete_uris";

    private static bool _promptInFlight;

    public static int PendingCount => LoadUris().Count;

    public static void Enqueue(IEnumerable<string> contentUris)
    {
        var set = new HashSet<string>(LoadUris(), StringComparer.Ordinal);
        foreach (var uri in contentUris)
        {
            if (!string.IsNullOrWhiteSpace(uri))
                set.Add(uri);
        }

        SaveUris(set);
    }

    /// <summary>有 Activity 时尝试弹出系统删除确认；无 Activity 时仅保留队列。</summary>
    public static bool TryLaunchDeleteConfirmation(Activity? activity = null)
    {
        activity ??= MainActivity.Instance;
        if (activity == null || _promptInFlight)
            return false;

        var uris = LoadUris();
        if (uris.Count == 0)
            return false;

        if (Build.VERSION.SdkInt < BuildVersionCodes.R)
            return TryDeleteDirectly(activity, uris);

        var androidUris = uris.Select(static u => global::Android.Net.Uri.Parse(u)!).ToList();
        var pending = MediaStore.CreateDeleteRequest(activity.ContentResolver!, androidUris);
        if (pending == null)
            return TryDeleteDirectly(activity, uris);

        try
        {
            activity.StartIntentSenderForResult(
                pending.IntentSender,
                DeleteRequestCode,
                fillInIntent: null,
                flagsMask: 0,
                flagsValues: 0,
                extraFlags: 0);
            _promptInFlight = true;
            BackupLog.Info($"已请求系统删除确认（{uris.Count} 个文件）");
            return true;
        }
        catch (Exception ex)
        {
            BackupLog.Warn($"无法启动删除确认对话框: {ex.Message}");
            BackupLog.Error("无法启动删除确认对话框", ex);
            return false;
        }
    }

    public static void OnDeleteResult(Result resultCode)
    {
        _promptInFlight = false;
        var count = PendingCount;
        if (count == 0)
            return;

        if (resultCode == Result.Ok)
        {
            Clear();
            BackupLog.Info($"用户已确认删除 {count} 个本地文件");
        }
        else
        {
            BackupLog.Info($"删除确认未完成（{resultCode}），{count} 个文件仍待确认");
        }
    }

    private static bool TryDeleteDirectly(Activity activity, IReadOnlyList<string> uris)
    {
        var resolver = activity.ContentResolver!;
        var removed = 0;
        foreach (var uriStr in uris)
        {
            try
            {
                removed += resolver.Delete(global::Android.Net.Uri.Parse(uriStr)!, null, null);
            }
            catch (RecoverableSecurityException ex)
            {
                try
                {
                    ex.UserAction.ActionIntent.Send();
                    _promptInFlight = true;
                }
                catch (Exception inner)
                {
                    BackupLog.Warn($"RecoverableSecurityException 授权失败: {inner.Message}");
                }

                return false;
            }
            catch (Exception ex)
            {
                BackupLog.Warn($"删除失败 {uriStr}: {ex.Message}");
            }
        }

        if (removed > 0)
            Clear();
        return removed >= uris.Count;
    }

    private static ISharedPreferences Prefs =>
        global::Android.App.Application.Context!
            .GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    private static List<string> LoadUris()
    {
        var raw = Prefs.GetString(PrefKeyPendingUris, null);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void SaveUris(IEnumerable<string> uris)
    {
        var list = uris.Where(static s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList();
        Prefs.Edit()?.PutString(PrefKeyPendingUris, string.Join('\n', list))?.Apply();
    }

    private static void Clear() => Prefs.Edit()?.Remove(PrefKeyPendingUris)?.Apply();
}
