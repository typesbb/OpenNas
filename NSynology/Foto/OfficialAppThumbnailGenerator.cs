using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace NSynology.Foto;

/// <summary>按官方 App 上传要求从原图生成 <c>thumb_xl</c> / <c>thumb_sm</c> JPEG。</summary>
internal static class OfficialAppThumbnailGenerator
{
    private const int XlMaxEdge = 1920;
    private const int SmMaxEdge = 360;
    /// <summary>缩略图解码最多读取的字节数，避免大图/视频拖垮内存。</summary>
    private const int MaxBytesForThumbDecode = 12 * 1024 * 1024;

    /// <summary>强制占位缩略图（测试或绕过 ImageSharp）。</summary>
    public static bool UseMinimalThumbnailsForUpload { get; set; }

    /// <summary>Android 等平台注入：从已读入的图像字节生成 thumb_xl / thumb_sm。</summary>
    public static Func<string, byte[], CancellationToken, Task<(byte[] Xl, byte[] Sm)>>? UploadThumbnailFromBytesFactory { get; set; }

    public static Task<(byte[] Xl, byte[] Sm)> CreateForUploadFromBytesAsync(
        string mimeType,
        byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        if (UseMinimalThumbnailsForUpload || IsVideo(mimeType))
            return Task.FromResult((MinimalJpeg, MinimalJpeg));

        if (UploadThumbnailFromBytesFactory is { } factory)
            return factory(mimeType, imageBytes, cancellationToken);

        return Task.FromResult(CreateFromImage(imageBytes));
    }

    public static Task<(byte[] Xl, byte[] Sm)> CreateForUploadAsync(
        string mimeType,
        UploadStreamFactory openStream,
        CancellationToken cancellationToken = default)
    {
        if (UseMinimalThumbnailsForUpload || IsVideo(mimeType))
            return Task.FromResult((MinimalJpeg, MinimalJpeg));

        if (UploadThumbnailFromBytesFactory is not null)
        {
            return CreateForUploadFromStreamAsBytesAsync(mimeType, openStream, cancellationToken);
        }

        if (OperatingSystem.IsAndroid())
            return Task.FromResult((MinimalJpeg, MinimalJpeg));

        return CreateForUploadFromImageAsync(openStream, cancellationToken);
    }

    private static async Task<(byte[] Xl, byte[] Sm)> CreateForUploadFromStreamAsBytesAsync(
        string mimeType,
        UploadStreamFactory openStream,
        CancellationToken cancellationToken)
    {
        await using var stream = await openStream(cancellationToken);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return await CreateForUploadFromBytesAsync(mimeType, ms.ToArray(), cancellationToken);
    }

    private static async Task<(byte[] Xl, byte[] Sm)> CreateForUploadFromImageAsync(
        UploadStreamFactory openStream,
        CancellationToken cancellationToken)
    {
        await using var stream = await openStream(cancellationToken);
        if (!LooksLikeJpegHeader(stream))
            return (MinimalJpeg, MinimalJpeg);

        try
        {
            var prefix = await ReadPrefixAsync(stream, MaxBytesForThumbDecode, cancellationToken);
            using var image = Image.Load(prefix);
            using var xl = ResizeMax(image, XlMaxEdge);
            using var sm = ResizeMax(image, SmMaxEdge);
            return (EncodeJpeg(xl, 85), EncodeJpeg(sm, 80));
        }
        catch
        {
            return (MinimalJpeg, MinimalJpeg);
        }
    }

    /// <summary>测试/离线：从已缓冲字节生成缩略图。</summary>
    public static (byte[] Xl, byte[] Sm) CreateFromImage(byte[] imageBytes)
    {
        if (!LooksLikeJpeg(imageBytes))
            return (MinimalJpeg, MinimalJpeg);

        try
        {
            using var image = Image.Load(imageBytes);
            using var xl = ResizeMax(image, XlMaxEdge);
            using var sm = ResizeMax(image, SmMaxEdge);
            return (EncodeJpeg(xl, 85), EncodeJpeg(sm, 80));
        }
        catch
        {
            return (MinimalJpeg, MinimalJpeg);
        }
    }

    private static bool IsVideo(string mimeType) =>
        mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeJpegHeader(Stream stream)
    {
        if (!stream.CanRead)
            return false;
        Span<byte> header = stackalloc byte[3];
        var read = stream.Read(header);
        if (stream.CanSeek)
            stream.Position = 0;
        return read == 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
    }

    private static bool LooksLikeJpeg(byte[] data) =>
        data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    private static async Task<byte[]> ReadPrefixAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[64 * 1024];
        var total = 0;
        while (total < maxBytes)
        {
            var toRead = Math.Min(buffer.Length, maxBytes - total);
            var n = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (n == 0)
                break;
            ms.Write(buffer, 0, n);
            total += n;
        }
        return ms.ToArray();
    }

    private static Image ResizeMax(Image source, int maxEdge) =>
        source.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxEdge, maxEdge)
        }));

    private static byte[] EncodeJpeg(Image image, int quality)
    {
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = quality });
        return ms.ToArray();
    }

    internal static readonly byte[] MinimalJpeg =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
        0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
        0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09,
        0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
        0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20,
        0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29,
        0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32,
        0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
        0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x03, 0xFF, 0xC4, 0x00, 0x14, 0x10, 0x01, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00,
        0x7F, 0xFF, 0xD9
    ];
}
