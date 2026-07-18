using NSynology.Foto;

namespace OpenNas.Services;

public static class FullscreenMediaLauncher
{
    public static Task OpenAsync(Page fromPage, IReadOnlyList<Photo> photos, int index)
    {
#if ANDROID
        // 不用 Modal：Android Modal 是独立 Dialog Window，系统栏很难藏干净。
        Platforms.Android.FullscreenOrientationHelper.EnterImmersive();
#endif
        return fromPage.Navigation.PushAsync(new Views.FullscreenMediaPage(photos, index), animated: false);
    }
}
