#if WINDOWS
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

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "OpenNas");
            Directory.CreateDirectory(dir);
            var destination = GetUniquePath(Path.Combine(dir, fileName));
            File.Copy(sourcePath, destination, overwrite: false);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
#endif
