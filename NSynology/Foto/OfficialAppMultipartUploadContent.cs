using System.Net;
using System.Text;
using NSynology;

namespace NSynology.Foto;

/// <summary>流式 multipart 上传，避免将大文件整段读入内存。</summary>
internal sealed class OfficialAppMultipartUploadContent : HttpContent
{
    private readonly string _boundary;
    private readonly string _fileName;
    private readonly UploadStreamFactory _openFileStream;
    private readonly byte[] _thumbXl;
    private readonly byte[] _thumbSm;
    private readonly string _rawDataJson;
    private readonly int _albumId;
    private readonly long _mtimeSec;
    private readonly long _dateSec;

    public OfficialAppMultipartUploadContent(
        string fileName,
        int albumId,
        long mtimeSec,
        long dateSec,
        byte[] thumbXl,
        byte[] thumbSm,
        string rawDataJson,
        UploadStreamFactory openFileStream)
    {
        _boundary = Guid.NewGuid().ToString();
        _fileName = fileName;
        _albumId = albumId;
        _mtimeSec = mtimeSec;
        _dateSec = dateSec;
        _thumbXl = thumbXl;
        _thumbSm = thumbSm;
        _rawDataJson = rawDataJson;
        _openFileStream = openFileStream;
        Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={_boundary}");
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }

    protected override async Task SerializeToStreamAsync(Stream target, TransportContext? context)
    {
        await WriteFieldAsync(target, "method", "upload");
        await WriteFieldAsync(target, "api", "SYNO.Foto.Upload.Item");
        await WriteFieldAsync(target, "version", "5");
        await WriteFieldAsync(target, "require_thumb_version", "true");
        await WriteFieldAsync(target, "name", $"\"{_fileName}\"");
        await WriteFieldAsync(target, "mtime", _mtimeSec.ToString());
        await WriteFieldAsync(target, "date", _dateSec.ToString());
        await WriteFieldAsync(target, "folder", "[\"PhotoLibrary\"]");
        await WriteFieldAsync(target, "album_id", _albumId.ToString());
        await WriteFieldAsync(target, "duplicate", "\"ignore\"");

        await using (var fileStream = await _openFileStream(CancellationToken.None))
            await WriteFileStreamAsync(target, "file", _fileName, fileStream);

        WriteFileBytes(target, "thumb_xl", "xl", _thumbXl);
        await WriteFieldAsync(target, "model_version", "3");
        await WriteFieldAsync(target, "raw_data", _rawDataJson);
        WriteFileBytes(target, "thumb_sm", "sm", _thumbSm);

        await WriteAsync(target, $"--{_boundary}--\r\n");
    }

    private async Task WriteFieldAsync(Stream target, string name, string value)
    {
        await WriteAsync(target, $"--{_boundary}\r\n");
        await WriteAsync(target, $"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
        await WriteAsync(target, value);
        await WriteAsync(target, "\r\n");
    }

    private async Task WriteFileStreamAsync(Stream target, string name, string fileName, Stream data)
    {
        await WriteAsync(target, $"--{_boundary}\r\n");
        await WriteAsync(target, $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{fileName}\"\r\n");
        await WriteAsync(target, "Content-Type: application/octet-stream\r\n\r\n");
        await data.CopyToAsync(target);
        await WriteAsync(target, "\r\n");
    }

    private void WriteFileBytes(Stream target, string name, string fileName, byte[] data)
    {
        Write(target, $"--{_boundary}\r\n");
        Write(target, $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{fileName}\"\r\n");
        Write(target, "Content-Type: application/octet-stream\r\n\r\n");
        target.Write(data, 0, data.Length);
        Write(target, "\r\n");
    }

    private static async Task WriteAsync(Stream target, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await target.WriteAsync(bytes);
    }

    private static void Write(Stream target, string text) =>
        target.Write(Encoding.UTF8.GetBytes(text));
}
