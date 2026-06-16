using Android.Content.PM;
using OpenNas;

namespace OpenNas.Platforms.Android;

internal static class FullscreenOrientationHelper
{
    public static void SetLandscape(bool landscape)
    {
        var activity = MainActivity.Instance ?? Platform.CurrentActivity;
        if (activity == null)
            return;

        activity.RequestedOrientation = landscape
            ? ScreenOrientation.SensorLandscape
            : ScreenOrientation.Portrait;
    }

    public static void Reset() => SetLandscape(false);
}
