using Android.Content.PM;
using Android.OS;
using Android.Util;
using OpenNas;
using AApp = Android.App;
using ARational = Android.Util.Rational;

namespace OpenNas.Platforms.Android;

internal static class VideoPictureInPictureHelper
{
    public static bool IsSupported
    {
        get
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.N)
                return false;

            var activity = MainActivity.Instance ?? Platform.CurrentActivity;
            return activity?.PackageManager?.HasSystemFeature(PackageManager.FeaturePictureInPicture) == true;
        }
    }

    public static bool TryEnter()
    {
        var activity = MainActivity.Instance ?? Platform.CurrentActivity;
        if (activity == null)
            return false;

        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var builder = new AApp.PictureInPictureParams.Builder();
                builder.SetAspectRatio(new ARational(16, 9));
                if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                    builder.SetAutoEnterEnabled(false);

                return activity.EnterPictureInPictureMode(builder.Build());
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
            {
#pragma warning disable CA1422
                activity.EnterPictureInPictureMode();
#pragma warning restore CA1422
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Warn("OpenNas", $"进入画中画失败: {ex.Message}");
            return false;
        }
    }
}