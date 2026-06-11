using System.Text;

namespace NSynology.Foto;

internal sealed class MultipartWriteTracker(long totalLength, IProgress<double>? progress)
{
    private long _written;

    public void Add(long delta)
    {
        if (progress is null || totalLength <= 0 || delta <= 0)
            return;

        _written += delta;
        progress.Report(Math.Min(0.99, (double)_written / totalLength));
    }
}

/// <summary>官方相册 multipart 写入与 Content-Length 计算。</summary>
internal static class OfficialAppMultipartWriter
{
    public static string NewBoundary() => Guid.NewGuid().ToString();

    public static long ComputeAlbumUploadLength(
        string boundary,
        string fileName,
        int albumId,
        long mtimeSec,
        long dateSec,
        byte[] thumbXl,
        byte[] thumbSm,
        string rawDataJson,
        long fileBytesLength)
    {
        long total = 0;
        total += FieldPartLength(boundary, "method", "upload");
        total += FieldPartLength(boundary, "api", "SYNO.Foto.Upload.Item");
        total += FieldPartLength(boundary, "version", "5");
        total += FieldPartLength(boundary, "require_thumb_version", "true");
        total += FieldPartLength(boundary, "name", $"\"{fileName}\"");
        total += FieldPartLength(boundary, "mtime", mtimeSec.ToString());
        total += FieldPartLength(boundary, "date", dateSec.ToString());
        total += FieldPartLength(boundary, "folder", "[\"PhotoLibrary\"]");
        total += FieldPartLength(boundary, "album_id", albumId.ToString());
        total += FieldPartLength(boundary, "duplicate", "\"ignore\"");
        total += BinaryPartLength(boundary, "file", fileName, fileBytesLength);
        total += BinaryPartLength(boundary, "thumb_xl", "xl", thumbXl.Length);
        total += FieldPartLength(boundary, "model_version", "3");
        total += FieldPartLength(boundary, "raw_data", rawDataJson);
        total += BinaryPartLength(boundary, "thumb_sm", "sm", thumbSm.Length);
        total += Utf8Length($"--{boundary}--\r\n");
        return total;
    }

    public static async Task WriteAlbumUploadAsync(
        Stream target,
        string boundary,
        string fileName,
        int albumId,
        long mtimeSec,
        long dateSec,
        byte[] thumbXl,
        byte[] thumbSm,
        string rawDataJson,
        UploadStreamFactory openFileStream,
        long fileBytesLength,
        MultipartWriteTracker? tracker,
        CancellationToken cancellationToken = default)
    {
        await WriteFieldAsync(target, boundary, "method", "upload", tracker);
        await WriteFieldAsync(target, boundary, "api", "SYNO.Foto.Upload.Item", tracker);
        await WriteFieldAsync(target, boundary, "version", "5", tracker);
        await WriteFieldAsync(target, boundary, "require_thumb_version", "true", tracker);
        await WriteFieldAsync(target, boundary, "name", $"\"{fileName}\"", tracker);
        await WriteFieldAsync(target, boundary, "mtime", mtimeSec.ToString(), tracker);
        await WriteFieldAsync(target, boundary, "date", dateSec.ToString(), tracker);
        await WriteFieldAsync(target, boundary, "folder", "[\"PhotoLibrary\"]", tracker);
        await WriteFieldAsync(target, boundary, "album_id", albumId.ToString(), tracker);
        await WriteFieldAsync(target, boundary, "duplicate", "\"ignore\"", tracker);

        await using (var fileStream = await openFileStream(cancellationToken))
            await WriteFileStreamAsync(
                target, boundary, "file", fileName, fileStream, fileBytesLength, tracker, cancellationToken);

        WriteFileBytes(target, boundary, "thumb_xl", "xl", thumbXl, tracker);
        await WriteFieldAsync(target, boundary, "model_version", "3", tracker);
        await WriteFieldAsync(target, boundary, "raw_data", rawDataJson, tracker);
        WriteFileBytes(target, boundary, "thumb_sm", "sm", thumbSm, tracker);

        await WriteAsync(target, $"--{boundary}--\r\n", tracker);
    }

    private static long Utf8Length(string text) => Encoding.UTF8.GetByteCount(text);

    private static long FieldPartLength(string boundary, string name, string value) =>
        Utf8Length($"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n");

    private static long BinaryPartLength(string boundary, string name, string fileName, long dataLength) =>
        Utf8Length(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"; filename=\"{fileName}\"\r\nContent-Type: application/octet-stream\r\n\r\n")
        + dataLength
        + 2;

    private static async Task WriteFieldAsync(
        Stream target,
        string boundary,
        string name,
        string value,
        MultipartWriteTracker? tracker)
    {
        await WriteAsync(target, $"--{boundary}\r\n", tracker);
        await WriteAsync(target, $"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n", tracker);
        await WriteAsync(target, value, tracker);
        await WriteAsync(target, "\r\n", tracker);
    }

    private static async Task WriteFileStreamAsync(
        Stream target,
        string boundary,
        string name,
        string fileName,
        Stream data,
        long fileBytesLength,
        MultipartWriteTracker? tracker,
        CancellationToken cancellationToken)
    {
        await WriteAsync(target, $"--{boundary}\r\n", tracker);
        await WriteAsync(
            target,
            $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{fileName}\"\r\n",
            tracker);
        await WriteAsync(target, "Content-Type: application/octet-stream\r\n\r\n", tracker);

        var buffer = new byte[81920];
        long sent = 0;
        while (true)
        {
            var read = await data.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
                break;

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            tracker?.Add(read);
            sent += read;
            if (fileBytesLength > 0 && sent > fileBytesLength)
                throw new InvalidOperationException($"文件实际大小 ({sent}) 超过预期 ({fileBytesLength})。");
        }

        if (fileBytesLength > 0 && sent != fileBytesLength)
            throw new InvalidOperationException($"文件实际大小 ({sent}) 与预期 ({fileBytesLength}) 不一致。");

        await WriteAsync(target, "\r\n", tracker);
    }

    private static void WriteFileBytes(
        Stream target,
        string boundary,
        string name,
        string fileName,
        byte[] data,
        MultipartWriteTracker? tracker)
    {
        Write(target, $"--{boundary}\r\n", tracker);
        Write(
            target,
            $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{fileName}\"\r\n",
            tracker);
        Write(target, "Content-Type: application/octet-stream\r\n\r\n", tracker);
        target.Write(data, 0, data.Length);
        tracker?.Add(data.Length);
        Write(target, "\r\n", tracker);
    }

    private static async Task WriteAsync(Stream target, string text, MultipartWriteTracker? tracker)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await target.WriteAsync(bytes);
        tracker?.Add(bytes.Length);
    }

    private static void Write(Stream target, string text, MultipartWriteTracker? tracker)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        target.Write(bytes, 0, bytes.Length);
        tracker?.Add(bytes.Length);
    }
}
