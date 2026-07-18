using NSynology.Foto;
using OpenNas.Controls;
using OpenNas.Services;

namespace OpenNas.Helpers;

/// <summary>
/// 点击网格时抓取当前单元格正在显示的缩略图字节。
/// 优先从 Android ImageView 的 Drawable 拷贝，不依赖 File 路径。
/// </summary>
public static class GridThumbnailCapture
{
    public static byte[]? TryCapture(object? tapSender, Photo photo)
    {
        if (tapSender is Element element)
        {
            var image = FindGridImage(element);
            if (image != null)
            {
#if ANDROID
                var fromNative = Platforms.Android.NativeImageBitmap.TryCaptureJpeg(image);
                if (fromNative is { Length: > 0 })
                {
                    AppLog.Debug($"网格缩略图捕获 native bytes={fromNative.Length} id={photo.Id}");
                    RememberTemp(photo.Id, fromNative);
                    return fromNative;
                }
#endif

                if (image.Source is FileImageSource file
                    && !string.IsNullOrEmpty(file.File)
                    && File.Exists(file.File))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(file.File);
                        if (bytes.Length > 0)
                        {
                            AppLog.Debug($"网格缩略图捕获 file bytes={bytes.Length} id={photo.Id}");
                            NasThumbnailLoader.RememberDisplayedThumbnail(photo.Id, file.File);
                            return bytes;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Debug("读取网格缩略图文件失败", ex);
                    }
                }
            }
        }

        if (NasThumbnailLoader.TryFindCachedThumbnailPath(photo, out var path)
            && File.Exists(path))
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > 0)
                {
                    AppLog.Debug($"网格缩略图捕获 cache bytes={bytes.Length} id={photo.Id}");
                    return bytes;
                }
            }
            catch (Exception ex)
            {
                AppLog.Debug("读取缓存缩略图失败", ex);
            }
        }

        AppLog.Debug($"网格缩略图捕获失败 id={photo.Id}");
        return null;
    }

    private static Image? FindGridImage(Element element)
    {
        if (element is AlbumGridPhotoView gridPhoto)
            return gridPhoto;
        if (element is Image image)
            return image;

        if (element is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is not Element childElement)
                    continue;
                var found = FindGridImage(childElement);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static void RememberTemp(int photoId, byte[] bytes)
    {
        try
        {
            var dir = NasMediaCache.ThumbnailsDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"seed_{photoId}.jpg");
            File.WriteAllBytes(path, bytes);
            NasThumbnailLoader.RememberDisplayedThumbnail(photoId, path);
        }
        catch
        {
            // ignore
        }
    }
}
