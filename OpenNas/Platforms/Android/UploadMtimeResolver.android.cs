using System.Globalization;
using Android.Content;
using Android.Media;
using Android.Provider;
using OpenNas.Core.Helpers;

namespace OpenNas.Helpers;

public static partial class UploadMtimeResolver
{
    private static long ResolveAndroid(FileResult file, byte[] fileBytes, string fileName, string mimeType)
    {
        var fromUri = TryGetFromContentUri(file.FullPath);
        if (fromUri > 0)
            return fromUri;

        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            var fromVideo = TryGetFromVideoBytes(fileBytes);
            if (fromVideo > 0)
                return fromVideo;
        }
        else
        {
            var fromExif = TryGetFromImageBytes(fileBytes);
            if (fromExif > 0)
                return fromExif;
        }

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

    private static long TryGetFromContentUri(string? uriString)
    {
        if (string.IsNullOrWhiteSpace(uriString)
            || !uriString.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        try
        {
            var ctx = Platform.CurrentActivity ?? Android.App.Application.Context;
            var resolver = ctx?.ContentResolver;
            if (resolver is null)
                return 0;

            var uri = Android.Net.Uri.Parse(uriString);
            string[] projection =
            {
                MediaStore.IMediaColumns.DateTaken,
                MediaStore.IMediaColumns.DateModified
            };

            using var cursor = resolver.Query(uri, projection, null, null, null);
            if (cursor?.MoveToFirst() != true)
                return 0;

            var takenCol = cursor.GetColumnIndex(MediaStore.IMediaColumns.DateTaken);
            var modCol = cursor.GetColumnIndex(MediaStore.IMediaColumns.DateModified);
            var taken = takenCol >= 0 ? cursor.GetLong(takenCol) : 0L;
            var modified = modCol >= 0 ? cursor.GetLong(modCol) : 0L;
            return MediaUploadTimeHelper.ResolveUploadMtimeSeconds(taken, modified);
        }
        catch
        {
            return 0;
        }
    }

    private static long TryGetFromImageBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
            return 0;

        try
        {
            using var stream = new MemoryStream(bytes);
            var exif = new ExifInterface(stream);
            var raw = exif.GetAttribute(ExifInterface.TagDatetimeOriginal)
                      ?? exif.GetAttribute(ExifInterface.TagDatetime);
            return ParseExifDate(raw);
        }
        catch
        {
            return 0;
        }
    }

    private static long TryGetFromVideoBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
            return 0;

        var tempPath = Path.Combine(Path.GetTempPath(), $"opennas_mtime_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            using var retriever = new MediaMetadataRetriever();
            retriever.SetDataSource(tempPath);
            var raw = retriever.ExtractMetadata(MetadataKey.Date);
            return ParseVideoMetadataDate(raw);
        }
        catch
        {
            return 0;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* ignore */ }
        }
    }

    private static long ParseExifDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        if (DateTime.TryParseExact(
                raw,
                "yyyy:MM:dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var dt))
        {
            return new DateTimeOffset(dt).ToUnixTimeSeconds();
        }

        return 0;
    }

    private static long ParseVideoMetadataDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        var formats = new[]
        {
            "yyyyMMdd'T'HHmmss",
            "yyyyMMdd'T'HHmmss.fff",
            "yyyyMMdd'T'HHmmss.fff'Z'",
            "yyyy:MM:dd HH:mm:ss"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(
                    raw,
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var dt))
            {
                return new DateTimeOffset(dt).ToUnixTimeSeconds();
            }
        }

        return 0;
    }
}
