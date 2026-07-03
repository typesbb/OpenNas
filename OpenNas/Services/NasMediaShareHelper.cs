using NSynology.Foto;
using OpenNas.Helpers;

namespace OpenNas.Services;

public static class NasMediaShareHelper
{
    public static async Task<bool> ShareAsync(
        Photo photo,
        IProgress<NasDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = await NasPhotoDownloadService.EnsureLocalFileAsync(photo, progress, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "分享",
                File = new ShareFile(path)
            });
        });

        return true;
    }
}
