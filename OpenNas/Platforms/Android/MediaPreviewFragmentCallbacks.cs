#if ANDROID
using Android.OS;
using AndroidX.Fragment.App;

namespace OpenNas.Platforms.Android;

/// <summary>
/// 监听 Modal DialogFragment，绑定其独立 Window 并重放系统栏。
/// </summary>
sealed class MediaPreviewFragmentCallbacks : FragmentManager.FragmentLifecycleCallbacks
{
    public override void OnFragmentStarted(FragmentManager fm, Fragment f) =>
        BindAndReapply(f);

    public override void OnFragmentResumed(FragmentManager fm, Fragment f) =>
        BindAndReapply(f);

    public override void OnFragmentViewCreated(
        FragmentManager fm, Fragment f, global::Android.Views.View v, Bundle? savedInstanceState) =>
        BindAndReapply(f);

    public override void OnFragmentDestroyed(FragmentManager fm, Fragment f)
    {
        if (f is DialogFragment)
            FullscreenOrientationHelper.UnbindModalWindow();
    }

    private static void BindAndReapply(Fragment f)
    {
        if (f is not DialogFragment { Dialog.Window: { } window })
            return;

        FullscreenOrientationHelper.BindModalWindow(window);
        FullscreenOrientationHelper.ReapplyCurrent();
    }
}
#endif
