namespace OpenNas.Services;

public static partial class DeviceMediaSaver
{
    public static partial Task<bool> SaveToGalleryAsync(
        string sourcePath,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken);

    private static string GetUniquePath(string destinationPath)
    {
        if (!File.Exists(destinationPath))
            return destinationPath;

        var directory = Path.GetDirectoryName(destinationPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(destinationPath);
        var ext = Path.GetExtension(destinationPath);

        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{name}_{DateTime.UtcNow.Ticks}{ext}");
    }
}
