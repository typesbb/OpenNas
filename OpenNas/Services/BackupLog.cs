namespace OpenNas.Services;

internal static class BackupLog
{
    private const string Tag = "OpenNasBackup";

    public static void Info(string message)
    {
#if ANDROID
        global::Android.Util.Log.Info(Tag, message);
#endif
        LogRepository.Instance.AppendOperation(message);
    }

    public static void Warn(string message)
    {
#if ANDROID
        global::Android.Util.Log.Warn(Tag, message);
#endif
        LogRepository.Instance.AppendWarning(message);
    }

    public static void Error(string message, Exception? ex = null)
    {
#if ANDROID
        var text = ex == null ? message : $"{message}\n{AppLog.FormatException(ex)}";
        global::Android.Util.Log.Error(Tag, text);
#endif
        var summary = ex == null ? message : $"{message} — {ex.GetType().Name}: {ex.Message}";
        LogRepository.Instance.AppendError(summary, ex);
    }
}
