using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.AppCompat.App;
using AndroidX.Core.View;
using AndroidX.Fragment.App;
using AColor = Android.Graphics.Color;
using AWindow = Android.Views.Window;

namespace OpenNas.Platforms.Android;

/// <summary>
/// 媒体预览系统栏。Modal 在 Android 上是 DialogFragment，必须改 Dialog.Window。
/// 未放大显示状态栏；放大后隐藏。
/// </summary>
internal static class FullscreenOrientationHelper
{
    public static bool InMediaPreview { get; private set; }
    public static bool WantImmersive { get; private set; }

    private static WeakReference<AWindow>? _modalWindow;

    public static void EnterMediaPreview()
    {
        InMediaPreview = true;
        WantImmersive = false;
        ShowBars();
    }

    public static void SetZoomImmersive(bool zoomed)
    {
        if (!InMediaPreview)
            return;

        WantImmersive = zoomed;
        if (zoomed)
            HideBars();
        else
            ShowBars();
    }

    public static void BindModalWindow(AWindow window) =>
        _modalWindow = new WeakReference<AWindow>(window);

    public static void UnbindModalWindow() => _modalWindow = null;

    public static void ReapplyCurrent()
    {
        if (!InMediaPreview)
            return;

        if (WantImmersive)
            HideBars();
        else
            ShowBars();
    }

    public static void SetLandscape(bool landscape)
    {
        var activity = MainActivity.Instance ?? Platform.CurrentActivity;
        if (activity == null)
            return;

        activity.RequestedOrientation = landscape
            ? ScreenOrientation.SensorLandscape
            : ScreenOrientation.Portrait;
        ReapplyCurrent();
    }

    public static void Reset()
    {
        InMediaPreview = false;
        WantImmersive = false;
        UnbindModalWindow();

        var activity = MainActivity.Instance ?? Platform.CurrentActivity;
        if (activity == null)
            return;

        activity.RequestedOrientation = ScreenOrientation.Portrait;
        var window = activity.Window;
        if (window?.DecorView == null)
            return;

        try
        {
            window.ClearFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.Fullscreen);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                WindowCompat.GetInsetsController(window, window.DecorView)
                    ?.Show(WindowInsetsCompat.Type.StatusBars() | WindowInsetsCompat.Type.NavigationBars());
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

    private static AWindow? ResolveWindow()
    {
        if (_modalWindow != null && _modalWindow.TryGetTarget(out var cached) && cached.DecorView != null)
            return cached;

        if ((MainActivity.Instance ?? Platform.CurrentActivity) is AppCompatActivity activity)
        {
            var dialog = FindDialogWindow(activity.SupportFragmentManager);
            if (dialog != null)
            {
                BindModalWindow(dialog);
                return dialog;
            }

            return activity.Window;
        }

        return Platform.CurrentActivity?.Window;
    }

    private static AWindow? FindDialogWindow(FragmentManager? fm)
    {
        var fragments = fm?.Fragments;
        if (fragments == null)
            return null;

        for (var i = fragments.Count - 1; i >= 0; i--)
        {
            var f = fragments[i];
            if (f is not { IsAdded: true })
                continue;

            if (f is DialogFragment { Dialog.Window: { } w })
                return w;

            var nested = FindDialogWindow(f.ChildFragmentManager);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static void HideBars()
    {
        var window = ResolveWindow();
        if (window?.DecorView == null)
            return;

        try
        {
            window.AddFlags(
                WindowManagerFlags.KeepScreenOn
                | WindowManagerFlags.DrawsSystemBarBackgrounds
                | WindowManagerFlags.Fullscreen);
            window.SetStatusBarColor(AColor.Transparent);
            window.SetNavigationBarColor(AColor.Transparent);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                WindowCompat.SetDecorFitsSystemWindows(window, false);
                var controller = WindowCompat.GetInsetsController(window, window.DecorView);
                if (controller != null)
                {
                    controller.SystemBarsBehavior =
                        WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
                    controller.Hide(
                        WindowInsetsCompat.Type.StatusBars()
                        | WindowInsetsCompat.Type.NavigationBars());
                }

                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    var insets = window.InsetsController;
                    insets?.Hide(WindowInsets.Type.SystemBars());
                    if (insets != null)
                        insets.SystemBarsBehavior =
                            (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
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
        }
    }

    private static void ShowBars()
    {
        var window = ResolveWindow();
        if (window?.DecorView == null)
            return;

        try
        {
            window.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.DrawsSystemBarBackgrounds);
            window.ClearFlags(WindowManagerFlags.Fullscreen);
            window.SetStatusBarColor(AColor.Transparent);
            window.SetNavigationBarColor(AColor.Black);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                WindowCompat.SetDecorFitsSystemWindows(window, false);
                var controller = WindowCompat.GetInsetsController(window, window.DecorView);
                controller?.Show(
                    WindowInsetsCompat.Type.StatusBars()
                    | WindowInsetsCompat.Type.NavigationBars());

                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                    window.InsetsController?.Show(WindowInsets.Type.SystemBars());
            }
            else
            {
#pragma warning disable CS0618
                window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
                    SystemUiFlags.LayoutStable
                    | SystemUiFlags.LayoutFullscreen
                    | SystemUiFlags.LayoutHideNavigation);
#pragma warning restore CS0618
            }
        }
        catch
        {
        }
    }
}
