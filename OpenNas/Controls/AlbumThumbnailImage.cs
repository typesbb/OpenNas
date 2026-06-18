using NSynology.Foto;
using OpenNas.Helpers;
#if ANDROID
using Android.Widget;
#endif

namespace OpenNas.Controls;

public class AlbumThumbnailImage : Image
{
    public static readonly BindableProperty PhotoProperty = BindableProperty.Create(
        nameof(Photo),
        typeof(Photo),
        typeof(AlbumThumbnailImage),
        propertyChanged: OnPhotoChanged);

    private CancellationTokenSource? _loadCts;
    private int _loadedPhotoId;

    public Photo? Photo
    {
        get => (Photo?)GetValue(PhotoProperty);
        set => SetValue(PhotoProperty, value);
    }

    private static void OnPhotoChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not AlbumThumbnailImage view)
            return;

        view._loadCts?.Cancel();
        view._loadCts?.Dispose();
        view._loadCts = new CancellationTokenSource();
        var token = view._loadCts.Token;

        if (newValue is not Photo photo)
        {
            view._loadedPhotoId = 0;
            view.ClearThumbnail();
            return;
        }

        if (view._loadedPhotoId == photo.Id && view.HasThumbnail())
            return;

        var photoId = photo.Id;
        NasGridThumbnailLoader.TryLoad(
            view,
            photo,
            () =>
            {
                if (token.IsCancellationRequested || view.Photo?.Id != photoId)
                    return false;

                view._loadedPhotoId = photoId;
                return true;
            },
            token);
    }

    private bool HasThumbnail()
    {
#if ANDROID
        if (Handler?.PlatformView is ImageView iv)
            return iv.Drawable != null;
#endif
        return Source != null;
    }

    private void ClearThumbnail()
    {
#if ANDROID
        if (Handler?.PlatformView is ImageView iv)
        {
            iv.SetImageDrawable(null);
            return;
        }
#endif
        Source = null;
    }
}
