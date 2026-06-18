namespace OpenNas.Helpers;

public static partial class NasGridThumbnailLoader
{
#if !ANDROID
    private static partial void ClearImage(Image image) => image.Source = null;

    private static partial byte[]? DecodeGridJpeg(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }
#endif
}
