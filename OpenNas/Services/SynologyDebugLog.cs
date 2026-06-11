#if DEBUG
namespace OpenNas.Services;

/// <summary>将 NSynology HTTP 跟踪写入 Android logcat（标签 NSynology，单行摘要）。</summary>
internal static class SynologyDebugLog
{
    private const string Tag = "NSynology";

    public static void Write(string message)
    {
#if ANDROID
        if (string.IsNullOrEmpty(message))
            return;

        global::Android.Util.Log.Debug(Tag, message);
#else
        System.Diagnostics.Debug.WriteLine(message);
#endif
    }
}
#endif
