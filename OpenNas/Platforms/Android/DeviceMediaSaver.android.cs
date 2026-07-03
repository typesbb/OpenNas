#if ANDROID
using Android.Content;
using Android.OS;
using Android.Provider;
using AndroidX.Core.Content;

namespace OpenNas.Services;

public static partial class DeviceMediaSaver
{
    public static partial async Task<bool> SaveToGalleryAsync(
        string sourcePath,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            return false;

        var context = Platform.CurrentActivity ?? Platform.AppContext;
        if (context == null)
            return false;

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isVideo = mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
            var collection = isVideo
                ? MediaStore.Video.Media.ExternalContentUri
                : MediaStore.Images.Media.ExternalContentUri;

            var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
            values.Put(MediaStore.IMediaColumns.MimeType, mimeType);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                var relativePath = isVideo
                    ? "Movies/OpenNas"
                    : "Pictures/OpenNas";
                values.Put(MediaStore.IMediaColumns.RelativePath, relativePath);
                values.Put(MediaStore.IMediaColumns.IsPending, 1);
            }

            var resolver = context.ContentResolver;
            var uri = resolver.Insert(collection, values);
            if (uri == null)
                return false;

            try
            {
                using var input = File.OpenRead(sourcePath);
                using var output = resolver.OpenOutputStream(uri);
                if (output == null)
                    return false;

                input.CopyTo(output);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                {
                    values.Clear();
                    values.Put(MediaStore.IMediaColumns.IsPending, 0);
                    resolver.Update(uri, values, null, null);
                }

                return true;
            }
            catch
            {
                try { resolver.Delete(uri, null, null); } catch { }
                return false;
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
#endif
