using System.Text;

namespace NSynology.Foto;

/// <summary>按 SAZ 3017 手工拼装 multipart（避免 <see cref="MultipartFormDataContent"/> 额外 Content-Type）。</summary>
internal static class OfficialAppMultipartBuilder
{
    public static (byte[] Body, string Boundary) BuildAlbumUpload(
        byte[] fileBytes,
        string fileName,
        int albumId,
        long mtimeSec,
        long dateSec,
        byte[] thumbXl,
        byte[] thumbSm,
        string rawDataJson)
    {
        var boundary = Guid.NewGuid().ToString();
        using var ms = new MemoryStream();

        WriteField(ms, boundary, "method", "upload");
        WriteField(ms, boundary, "api", "SYNO.Foto.Upload.Item");
        WriteField(ms, boundary, "version", "5");
        WriteField(ms, boundary, "require_thumb_version", "true");
        WriteField(ms, boundary, "name", $"\"{fileName}\"");
        WriteField(ms, boundary, "mtime", mtimeSec.ToString());
        WriteField(ms, boundary, "date", dateSec.ToString());
        WriteField(ms, boundary, "folder", "[\"PhotoLibrary\"]");
        WriteField(ms, boundary, "album_id", albumId.ToString());
        WriteField(ms, boundary, "duplicate", "\"ignore\"");

        WriteFile(ms, boundary, "file", fileName, fileBytes);
        WriteFile(ms, boundary, "thumb_xl", "xl", thumbXl);
        WriteField(ms, boundary, "model_version", "3");
        WriteField(ms, boundary, "raw_data", rawDataJson);
        WriteFile(ms, boundary, "thumb_sm", "sm", thumbSm);

        Write(ms, $"--{boundary}--\r\n");
        return (ms.ToArray(), boundary);
    }

    private static void WriteField(Stream ms, string boundary, string name, string value)
    {
        Write(ms, $"--{boundary}\r\n");
        Write(ms, $"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
        Write(ms, value);
        Write(ms, "\r\n");
    }

    private static void WriteFile(Stream ms, string boundary, string name, string fileName, byte[] data)
    {
        Write(ms, $"--{boundary}\r\n");
        Write(ms, $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{fileName}\"\r\n");
        Write(ms, "Content-Type: application/octet-stream\r\n\r\n");
        ms.Write(data, 0, data.Length);
        Write(ms, "\r\n");
    }

    private static void Write(Stream ms, string text) =>
        ms.Write(Encoding.UTF8.GetBytes(text));
}
