using NSynology.Foto;

namespace OpenNas.Services;

public static class FullscreenMediaLauncher
{
    public static Task OpenAsync(Page fromPage, IReadOnlyList<Photo> photos, int index) =>
        fromPage.Navigation.PushModalAsync(new Views.FullscreenMediaPage(photos, index));
}
