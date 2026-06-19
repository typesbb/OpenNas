using NSynology.Foto;
using OpenNas.Helpers;

namespace OpenNas.Controls;

/// <summary>相册网格缩略图：绑定路径零负载，加载推迟到下一帧。</summary>
public class AlbumGridPhotoView : Image
{
    private CancellationTokenSource? _loadCts;

    public AlbumGridPhotoView()
    {
        BindingContextChanged += (_, _) => ScheduleLoad();
    }

    private void ScheduleLoad()
    {
        NasGridImageApplyScheduler.ScheduleLoad(ApplyLoad);
    }

    private void ApplyLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        if (BindingContext is not Photo photo)
        {
            if (Source != null)
                Source = null;
            return;
        }

        var photoId = photo.Id;
        NasThumbnailLoader.TryLoadPhotoThumbnailDirect(
            this,
            photo,
            () => !token.IsCancellationRequested
                  && BindingContext is Photo bound
                  && bound.Id == photoId,
            token);
    }
}
