using Android.Graphics;
using Android.Media;
using NSynology.Foto;

namespace OpenNas.Platforms.Android;

/// <summary>用系统 API 生成上传缩略图（含视频帧），避免 ImageSharp 在 Android 上阻塞。</summary>
internal static class AndroidUploadThumbnailFactory
{
    private const int XlMaxEdge = 1920;
    private const int SmMaxEdge = 360;

    public static void Register()
    {
        AppThumbnailGenerator.UploadThumbnailFromBytesFactory = CreateAsync;
    }

    private static Task<(byte[] Xl, byte[] Sm)> CreateAsync(
        string mimeType,
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(CreateFromVideoBytes(imageBytes));

        return Task.FromResult(CreateFromImageBytes(imageBytes));
    }

    private static (byte[] Xl, byte[] Sm) CreateFromImageBytes(byte[] data)
    {
        if (!LooksLikeJpeg(data))
            return (AppThumbnailGenerator.MinimalJpeg, AppThumbnailGenerator.MinimalJpeg);

        try
        {
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeByteArray(data, 0, data.Length, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
                return (AppThumbnailGenerator.MinimalJpeg, AppThumbnailGenerator.MinimalJpeg);

            var decodeOpts = new BitmapFactory.Options
            {
                InSampleSize = CalcInSampleSize(bounds.OutWidth, bounds.OutHeight, XlMaxEdge)
            };
            var bitmap = BitmapFactory.DecodeByteArray(data, 0, data.Length, decodeOpts);
            if (bitmap is null)
                return (AppThumbnailGenerator.MinimalJpeg, AppThumbnailGenerator.MinimalJpeg);

            var xl = EncodeScaled(bitmap, XlMaxEdge, 85);
            var sm = EncodeScaled(bitmap, SmMaxEdge, 80);
            bitmap.Recycle();
            return (xl, sm);
        }
        catch
        {
            return (AppThumbnailGenerator.MinimalJpeg, AppThumbnailGenerator.MinimalJpeg);
        }
    }

    private static (byte[] Xl, byte[] Sm) CreateFromVideoBytes(byte[] data)
    {
        if (data.Length == 0)
            return (AppThumbnailGenerator.MinimalJpeg, AppThumbnailGenerator.MinimalJpeg);

        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"opennas_vid_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(tempPath, data);
            using var retriever = new MediaMetadataRetriever();
            retriever.SetDataSource(tempPath);

            var bitmap = retriever.GetFrameAtTime(1_000_000, (int)Option.ClosestSync)
                         ?? retriever.GetFrameAtTime(0, (int)Option.ClosestSync);
            if (bitmap is null)
                return (AppThumbnailGenerator.MinimalJpeg, AppThumbnailGenerator.MinimalJpeg);

            var xl = EncodeScaled(bitmap, XlMaxEdge, 85);
            var sm = EncodeScaled(bitmap, SmMaxEdge, 80);
            bitmap.Recycle();
            return (xl, sm);
        }
        catch
        {
            return (AppThumbnailGenerator.MinimalJpeg, AppThumbnailGenerator.MinimalJpeg);
        }
        finally
        {
            try { System.IO.File.Delete(tempPath); } catch { /* ignore */ }
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
            if (!ReferenceEquals(scaled, source))
                scaled.Recycle();
        }
    }
}
