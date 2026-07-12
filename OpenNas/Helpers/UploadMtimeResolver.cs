using OpenNas.Core.Helpers;

namespace OpenNas.Helpers;

public static partial class UploadMtimeResolver
{
    public static Task<long> ResolveAsync(
        FileResult file,
        byte[] fileBytes,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

#if ANDROID
        var resolved = ResolveAndroid(file, fileBytes, fileName, mimeType);
#else
        var resolved = ResolveDefault(file, fileName);
#endif

        if (resolved <= 0)
            resolved = MediaUploadTimeHelper.TryParseFromFileName(fileName);

        return Task.FromResult(resolved);
    }

#if !ANDROID
    private static long ResolveDefault(FileResult file, string fileName)
    {
        var path = file.FullPath;
        if (!string.IsNullOrWhiteSpace(path)
            && !path.StartsWith("content://", StringComparison.OrdinalIgnoreCase)
            && File.Exists(path))
        {
            try
            {
                return MediaUploadTimeHelper.NormalizeToUnixSeconds(
                    new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds());
            }
            catch
            {
                // ignore
            }
        }

        return 0;
    }
#endif
}
