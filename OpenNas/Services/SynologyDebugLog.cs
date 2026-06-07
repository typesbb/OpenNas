#if DEBUG
namespace OpenNas.Services;

/// <summary>将 NSynology HTTP 跟踪写入 Android logcat（标签 NSynology）。</summary>
internal static class SynologyDebugLog
{
    private const string Tag = "NSynology";
    private const int MaxChunk = 3500;

    public static void Write(string message)
    {
#if ANDROID
        if (string.IsNullOrEmpty(message))
            return;

        for (var i = 0; i < message.Length; i += MaxChunk)
        {
            var len = Math.Min(MaxChunk, message.Length - i);
            global::Android.Util.Log.Debug(Tag, message.Substring(i, len));
        }
#else
        System.Diagnostics.Debug.WriteLine(message);
#endif
    }
}
#endif
