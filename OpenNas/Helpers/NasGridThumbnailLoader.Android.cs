#if ANDROID
using Android.Graphics;
using Android.Widget;
using OpenNas.Platforms.Android;

namespace OpenNas.Helpers;

public static partial class NasGridThumbnailLoader
{
    private static partial void ClearImage(Image image)
    {
        if (image.Handler?.PlatformView is ImageView iv)
            iv.SetImageDrawable(null);
        else
            image.Source = null;
    }

    private static partial byte[]? DecodeGridJpeg(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
                return null;

            var sample = CalcSampleSize(bounds.OutWidth, bounds.OutHeight, GridMaxEdgePx);
            var opts = new BitmapFactory.Options { InSampleSize = sample };
            using var bitmap = BitmapFactory.DecodeFile(path, opts);
            if (bitmap is null)
                return null;

            using var ms = new MemoryStream();
            bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 78, ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static partial object? DecodeGridBitmap(string key, byte[] bytes) =>
        AlbumGridBitmapCache.Decode(key, bytes);

    private static partial void SetAndroidBitmap(Image image, object bitmap)
    {
        if (bitmap is not Bitmap bmp)
            return;

        if (image.Handler?.PlatformView is ImageView iv)
            iv.SetImageBitmap(bmp);
    }

    private static int CalcSampleSize(int width, int height, int maxEdge)
    {
        var longest = Math.Max(width, height);
        var sample = 1;
        while (longest / sample > maxEdge)
            sample *= 2;
        return sample;
    }
}
#endif
