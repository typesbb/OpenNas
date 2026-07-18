using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Widget;
using OpenNas.Services;

namespace OpenNas.Platforms.Android;

/// <summary>
/// 直接操作原生 ImageView。预览大图请只用这些方法，不要再设 Image.Source。
/// </summary>
internal static class NativeImageBitmap
{
    public static byte[]? TryCaptureJpeg(Image image)
    {
        if (image.Handler?.PlatformView is not ImageView imageView)
            return null;

        try
        {
            var drawable = imageView.Drawable;
            if (drawable == null)
                return null;

            Bitmap? bitmap = null;
            var owned = false;

            if (drawable is BitmapDrawable { Bitmap: { } bdBitmap }
                && !bdBitmap.IsRecycled)
            {
                bitmap = bdBitmap;
            }
            else
            {
                var w = Math.Max(imageView.Width, drawable.IntrinsicWidth);
                var h = Math.Max(imageView.Height, drawable.IntrinsicHeight);
                if (w <= 0 || h <= 0)
                    return null;

                bitmap = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!);
                owned = true;
                using var canvas = new Canvas(bitmap);
                drawable.SetBounds(0, 0, w, h);
                drawable.Draw(canvas);
            }

            if (bitmap == null || bitmap.IsRecycled)
                return null;

            using var ms = new MemoryStream();
            if (!bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 92, ms))
                return null;

            if (owned)
                bitmap.Recycle();

            var bytes = ms.ToArray();
            return bytes.Length > 0 ? bytes : null;
        }
        catch (Exception ex)
        {
            AppLog.Debug("捕获 ImageView 位图失败", ex);
            return null;
        }
    }

    public static bool HasDrawable(Image image)
    {
        try
        {
            if (image.Handler?.PlatformView is not ImageView imageView)
                return false;
            var drawable = imageView.Drawable;
            return drawable != null
                   && drawable.IntrinsicWidth > 0
                   && drawable.IntrinsicHeight > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>缩略图/原图：只 SetImageBitmap，绝不碰 Image.Source。</summary>
    public static void SetBitmapFromBytes(Image image, byte[] bytes)
    {
        if (bytes.Length == 0)
            return;

        if (TryDecodeBytes(image, bytes))
            return;

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (image.Handler?.PlatformView is not ImageView)
                return;
            image.HandlerChanged -= OnHandlerChanged;
            MainThread.BeginInvokeOnMainThread(() => TryDecodeBytes(image, bytes));
        }

        image.HandlerChanged += OnHandlerChanged;
        MainThread.BeginInvokeOnMainThread(() => TryDecodeBytes(image, bytes));
    }

    public static void SetBitmapFromFile(Image image, string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        if (TryDecodeFile(image, path))
            return;

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (image.Handler?.PlatformView is not ImageView)
                return;
            image.HandlerChanged -= OnHandlerChanged;
            MainThread.BeginInvokeOnMainThread(() => TryDecodeFile(image, path));
        }

        image.HandlerChanged += OnHandlerChanged;
        MainThread.BeginInvokeOnMainThread(() => TryDecodeFile(image, path));
    }

    private static bool TryDecodeBytes(Image image, byte[] bytes)
    {
        if (image.Handler?.PlatformView is not ImageView imageView)
            return false;

        try
        {
            var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
            if (bitmap == null)
                return false;
            imageView.SetImageBitmap(bitmap);
            imageView.SetScaleType(ImageView.ScaleType.FitCenter);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Debug("SetImageBitmap(bytes) 失败", ex);
            return false;
        }
    }

    private static bool TryDecodeFile(Image image, string path)
    {
        if (image.Handler?.PlatformView is not ImageView imageView)
            return false;

        try
        {
            var opts = new BitmapFactory.Options { InPreferredConfig = Bitmap.Config.Argb8888 };
            var bitmap = BitmapFactory.DecodeFile(path, opts);
            if (bitmap == null)
                return false;
            imageView.SetImageBitmap(bitmap);
            imageView.SetScaleType(ImageView.ScaleType.FitCenter);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Debug("SetImageBitmap(file) 失败", ex);
            return false;
        }
    }
}
