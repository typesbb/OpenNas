#if IOS || MACCATALYST
using Foundation;
using Photos;

namespace OpenNas.Services;

public static partial class DeviceMediaSaver
{
    public static partial Task<bool> SaveToGalleryAsync(
        string sourcePath,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            return Task.FromResult(false);

        var tcs = new TaskCompletionSource<bool>();
        var url = NSUrl.FromFilename(sourcePath);
        var isVideo = mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () =>
            {
                if (isVideo)
                    PHAssetCreationRequest.FromVideo(url);
                else
                    PHAssetCreationRequest.FromImage(url);
            },
            (success, error) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                if (success)
                    tcs.TrySetResult(true);
                else
                    tcs.TrySetResult(false);
            });

        return tcs.Task;
    }
}
#endif
