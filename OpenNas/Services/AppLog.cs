using System.Text;

namespace OpenNas.Services;

/// <summary>logcat 诊断 + 用户日志持久化</summary>
internal static class AppLog
{
    private const string Tag = "OpenNas";

    public static void Error(string context, Exception ex)
    {
#if ANDROID
        global::Android.Util.Log.Error(Tag, $"{context}\n{FormatException(ex)}");
#else
        System.Diagnostics.Debug.WriteLine($"{context}\n{FormatException(ex)}");
#endif
        LogRepository.Instance.AppendError(context, ex);
    }

    public static void Warn(string context, Exception? ex = null)
    {
#if ANDROID
        var msg = ex == null ? context : $"{context}\n{FormatException(ex)}";
        global::Android.Util.Log.Warn(Tag, msg);
#endif
        if (ex != null)
            LogRepository.Instance.AppendError(context, ex);
    }

    public static void Debug(string context, Exception? ex = null)
    {
#if ANDROID
        var msg = ex == null ? context : $"{context}\n{FormatException(ex)}";
        global::Android.Util.Log.Debug(Tag, msg);
#else
        var msg = ex == null ? context : $"{context}\n{FormatException(ex)}";
        System.Diagnostics.Debug.WriteLine(msg);
#endif
    }

    public static string FormatException(Exception ex)
    {
        var sb = new StringBuilder();
        for (var cur = ex; cur != null; cur = cur.InnerException)
        {
            sb.AppendLine($"[{cur.GetType().Name}] {cur.Message}");
            if (!string.IsNullOrEmpty(cur.StackTrace))
                sb.AppendLine(cur.StackTrace);
        }
        return sb.ToString().TrimEnd();
    }
}
