using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using OpenNas;
using AColor = Android.Graphics.Color;

namespace OpenNas.Platforms.Android;

/// <summary>
/// 媒体预览沉浸式。Android 会在窗口重新获得焦点时重置 SystemUi，
/// 必须在 <see cref="MainActivity.OnWindowFocusChanged"/> 里再次调用 <see cref="Apply"/>.
/// </summary>
internal static class FullscreenOrientationHelper
{
    public static bool WantImmersive { get; private set; }

    public static void EnterImmersive()
    {
        WantImmersive = true;
        Apply();
        _ = ReapplySoonAsync();
    }

    public static void ExitImmersive()
    {
        WantImmersive = false;
        Clear();
    }

    public static void Apply()
    {
        if (!WantImmersive)
            return;

        var activity = MainActivity.Instance ?? Platform.CurrentActivity;
        var window = activity?.Window;
        if (window?.DecorView == null)
            return;

        try
        {
            window.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.DrawsSystemBarBackgrounds);
            window.SetStatusBarColor(AColor.Transparent);
            window.SetNavigationBarColor(AColor.Transparent);

            if (OperatingSystem.IsAndroidVersionAtLeast(28))
            {
                var lp = window.Attributes;
                if (lp != null)
                {
                    lp.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
                    window.Attributes = lp;
                }
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                WindowCompat.SetDecorFitsSystemWindows(window, false);
                var controller = WindowCompat.GetInsetsController(window, window.DecorView);
                if (controller != null)
                {
                    controller.Hide(WindowInsetsCompat.Type.StatusBars() | WindowInsetsCompat.Type.NavigationBars());
                    controller.SystemBarsBehavior =
                        WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
                }
            }
            else
            {
#pragma warning disable CS0618
                window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
                    SystemUiFlags.LayoutStable
                    | SystemUiFlags.LayoutHideNavigation
                    | SystemUiFlags.LayoutFullscreen
                    | SystemUiFlags.HideNavigation
                    | SystemUiFlags.Fullscreen
                    | SystemUiFlags.ImmersiveSticky);
#pragma warning restore CS0618
            }
        }
        catch
        {
            // OEM 差异
        }
    }

    public static void Clear()
    {
        var activity = MainActivity.Instance ?? Platform.CurrentActivity;
        var window = activity?.Window;
        if (window?.DecorView == null)
            return;

        try
        {
            window.ClearFlags(WindowManagerFlags.KeepScreenOn);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                var controller = WindowCompat.GetInsetsController(window, window.DecorView);
                controller?.Show(WindowInsetsCompat.Type.StatusBars() | WindowInsetsCompat.Type.NavigationBars());
                WindowCompat.SetDecorFitsSystemWindows(window, true);
            }
            else
            {
#pragma warning disable CS0618
                window.DecorView.SystemUiVisibility = (StatusBarVisibility)SystemUiFlags.Visible;
#pragma warning restore CS0618
            }
        }
        catch
        {
        }
    }

    public static void SetLandscape(bool landscape)
    {
        var activity = MainActivity.Instance ?? Platform.CurrentActivity;
        if (activity == null)
            return;

        activity.RequestedOrientation = landscape
            ? ScreenOrientation.SensorLandscape
            : ScreenOrientation.Portrait;
        Apply();
    }

    public static void Reset()
    {
        ExitImmersive();
        var activity = MainActivity.Instance ?? Platform.CurrentActivity;
        if (activity != null)
            activity.RequestedOrientation = ScreenOrientation.Portrait;
    }

    private static async Task ReapplySoonAsync()
    {
        foreach (var delay in new[] { 16, 50, 120, 300 })
        {
            await Task.Delay(delay);
            if (!WantImmersive)
                return;

            MainThread.BeginInvokeOnMainThread(Apply);
        }
    }
}
