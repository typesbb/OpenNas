using NSynology;
using NSynology.Foto;

namespace OpenNas.Helpers;

public static class AlbumPhotoUpload
{
    public static string GuessMimeType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            ".bmp" => "image/bmp",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            ".wmv" => "video/x-ms-wmv",
            ".m4v" => "video/x-m4v",
            ".3gp" => "video/3gpp",
            _ => "image/jpeg"
        };
    }

    public static async Task<int> UploadFilesAsync(
        Album album,
        IReadOnlyList<FileResult> files,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
            throw new InvalidOperationException("未连接 NAS，请重新登录。");


        var total = files.Count;
        var uploaded = 0;
        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            var fileName = string.IsNullOrWhiteSpace(file.FileName)
                ? $"photo_{DateTime.Now:yyyyMMdd_HHmmss}.jpg"
                : file.FileName;
            progress?.Report((i + 1, total, fileName));

            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                continue;

            var mime = GuessMimeType(fileName);
            var mtime = await UploadMtimeResolver.ResolveAsync(file, bytes, fileName, mime, cancellationToken);
            if (mtime <= 0)
                mtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var result = await SynologyManager.Client.Foto.UploadToAlbumFromBytesAsync(
                bytes,
                fileName,
                mime,
                album.Id,
                bytes.Length,
                mtime,
                cancellationToken: cancellationToken);

            if (!result.VerifiedOnServer)
                throw new InvalidOperationException($"上传失败：{fileName}");

            uploaded++;
        }

        return uploaded;
    }
}
