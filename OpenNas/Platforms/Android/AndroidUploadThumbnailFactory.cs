using Android.Graphics;
using NSynology.Foto;

namespace OpenNas.Platforms.Android;

/// <summary>用系统 BitmapFactory 生成上传缩略图，避免 ImageSharp 在 Android 上阻塞。</summary>
internal static class AndroidUploadThumbnailFactory
{
    private const int XlMaxEdge = 1920;
    private const int SmMaxEdge = 360;

    public static void Register()
    {
        OfficialAppThumbnailGenerator.UploadThumbnailFromBytesFactory = CreateAsync;
    }

    private static Task<(byte[] Xl, byte[] Sm)> CreateAsync(
        string mimeType,
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateFromBytes(imageBytes));
    }

    private static (byte[] Xl, byte[] Sm) CreateFromBytes(byte[] data)
    {
        if (!LooksLikeJpeg(data))
            return (OfficialAppThumbnailGenerator.MinimalJpeg, OfficialAppThumbnailGenerator.MinimalJpeg);

        try
        {
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeByteArray(data, 0, data.Length, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
                return (OfficialAppThumbnailGenerator.MinimalJpeg, OfficialAppThumbnailGenerator.MinimalJpeg);

            var decodeOpts = new BitmapFactory.Options
            {
                InSampleSize = CalcInSampleSize(bounds.OutWidth, bounds.OutHeight, XlMaxEdge)
            };
            var bitmap = BitmapFactory.DecodeByteArray(data, 0, data.Length, decodeOpts);
            if (bitmap is null)
                return (OfficialAppThumbnailGenerator.MinimalJpeg, OfficialAppThumbnailGenerator.MinimalJpeg);

            var xl = EncodeScaled(bitmap, XlMaxEdge, 85);
            var sm = EncodeScaled(bitmap, SmMaxEdge, 80);
            bitmap.Recycle();
            return (xl, sm);
        }
        catch
        {
            return (OfficialAppThumbnailGenerator.MinimalJpeg, OfficialAppThumbnailGenerator.MinimalJpeg);
        }
    }

    private static bool LooksLikeJpeg(byte[] data) =>
        data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    private static int CalcInSampleSize(int width, int height, int maxEdge)
    {
        var longest = Math.Max(width, height);
        var sample = 1;
        while (longest / sample > maxEdge * 2)
            sample *= 2;
        return sample;
    }

    private static byte[] EncodeScaled(Bitmap source, int maxEdge, int quality)
    {
        var w = source.Width;
        var h = source.Height;
        var scale = Math.Min((float)maxEdge / w, (float)maxEdge / h);
        if (scale > 1f)
            scale = 1f;

        var nw = Math.Max(1, (int)(w * scale));
        var nh = Math.Max(1, (int)(h * scale));
        var scaled = Bitmap.CreateScaledBitmap(source, nw, nh, true);
        try
        {
            using var ms = new MemoryStream();
            scaled.Compress(Bitmap.CompressFormat.Jpeg!, quality, ms);
            return ms.ToArray();
        }
        finally
        {
            // CreateScaledBitmap 在尺寸不变时可能直接返回 source，不能 recycle 源图。
            if (!ReferenceEquals(scaled, source))
                scaled.Recycle();
        }
    }
}
