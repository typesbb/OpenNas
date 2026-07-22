using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Platform;
#if ANDROID
using Android.Content.Res;
#endif

namespace OpenNas.Helpers;

/// <summary>
/// 按当前主题设置系统状态栏颜色与图标深浅。
/// Shell.ForegroundColor 管的是 MAUI 导航栏，管不到 Android 系统状态栏。
/// </summary>
internal static class SystemBarsTheme
{
    public static void Apply(AppTheme? theme = null)
    {
#if ANDROID || IOS || MACCATALYST
        try
        {
#if ANDROID
            if (Platforms.Android.FullscreenOrientationHelper.InMediaPreview)
                return;
#endif
            var isDark = ResolveIsDark(theme);

            var color = ResolveColor(isDark ? "AppBackgroundDark" : "AppBackground")
                ?? (isDark ? Color.FromArgb("#0F172A") : Color.FromArgb("#F8FAFC"));

            StatusBar.SetColor(color);
            StatusBar.SetStyle(isDark ? StatusBarStyle.LightContent : StatusBarStyle.DarkContent);
        }
        catch
        {
            // 窗口尚未就绪或 OEM 差异
        }
#endif
    }

    /// <summary>
    /// 回前台 / 系统 UiMode 变化时调用：立刻刷一次，并短延迟再刷，躲开 MAUI 主题同步竞态。
    /// </summary>
    public static void ApplyAfterResume()
    {
        Apply();
        _ = ReapplySoonAsync();
    }

    private static async Task ReapplySoonAsync()
    {
        foreach (var delayMs in new[] { 50, 250 })
        {
            await Task.Delay(delayMs);
            MainThread.BeginInvokeOnMainThread(() => Apply());
        }
    }

    private static bool ResolveIsDark(AppTheme? theme)
    {
        var app = Application.Current;

        // 「跟随系统」时以平台当前夜间模式为准，避免后台系统已切浅色但 MAUI 尚未更新。
        if (app?.UserAppTheme == AppTheme.Unspecified)
        {
#if ANDROID
            var uiMode = Platform.CurrentActivity?.Resources?.Configuration?.UiMode;
            if (uiMode != null)
            {
                var night = uiMode & UiMode.NightMask;
                if (night == UiMode.NightYes)
                    return true;
                if (night == UiMode.NightNo)
                    return false;
            }
#endif
        }

        var resolved = theme ?? app?.RequestedTheme ?? AppTheme.Light;
        return resolved == AppTheme.Dark;
    }

    private static Color? ResolveColor(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
            return color;
        return null;
    }
}
