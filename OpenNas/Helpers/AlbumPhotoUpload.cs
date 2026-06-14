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
            _ => "image/jpeg"
        };
    }

    public static async Task<int> UploadFilesAsync(
        Album album,
        IEnumerable<FileResult> files,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
            throw new InvalidOperationException("未连接 NAS，请重新登录。");

        await SynologyManager.Client.Foto.WarmupAlbumForBackupAsync(album.Id, cancellationToken);

        var uploaded = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = string.IsNullOrWhiteSpace(file.FileName) ? $"photo_{DateTime.Now:yyyyMMdd_HHmmss}.jpg" : file.FileName;
            status?.Report($"正在上传 {fileName}…");

            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                continue;

            var mime = GuessMimeType(fileName);
            var mtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
