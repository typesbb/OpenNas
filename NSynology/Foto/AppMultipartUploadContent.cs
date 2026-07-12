using System.Net;
using System.Text;

namespace NSynology.Foto;

/// <summary>流式 multipart 上传，通过预先计算 Content-Length 避免 HttpClient 整包缓冲。</summary>
internal sealed class AppMultipartUploadContent : HttpContent
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
    private readonly long _fileBytesLength;
    private readonly long _totalLength;
    private readonly IProgress<double>? _progress;
    private readonly string _duplicate;

    public AppMultipartUploadContent(
        string fileName,
        int albumId,
        long mtimeSec,
        long dateSec,
        byte[] thumbXl,
        byte[] thumbSm,
        string rawDataJson,
        UploadStreamFactory openFileStream,
        long fileBytesLength,
        IProgress<double>? progress = null,
        string duplicate = AppUploadDuplicate.Ignore)
    {
        if (fileBytesLength < 0)
            throw new ArgumentOutOfRangeException(nameof(fileBytesLength));

        _boundary = AppMultipartWriter.NewBoundary();
        _fileName = fileName;
        _albumId = albumId;
        _mtimeSec = mtimeSec;
        _dateSec = dateSec;
        _thumbXl = thumbXl;
        _thumbSm = thumbSm;
        _rawDataJson = rawDataJson;
        _openFileStream = openFileStream;
        _fileBytesLength = fileBytesLength;
        _progress = progress;
        _duplicate = duplicate;
        _totalLength = AppMultipartWriter.ComputeAlbumUploadLength(
            _boundary,
            fileName,
            albumId,
            mtimeSec,
            dateSec,
            thumbXl,
            thumbSm,
            rawDataJson,
            fileBytesLength,
            duplicate);
        Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={_boundary}");
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _totalLength;
        return true;
    }

    protected override async Task SerializeToStreamAsync(Stream target, TransportContext? context)
    {
        var tracker = _progress is null ? null : new MultipartWriteTracker(_totalLength, _progress);
        await AppMultipartWriter.WriteAlbumUploadAsync(
            target,
            _boundary,
            _fileName,
            _albumId,
            _mtimeSec,
            _dateSec,
            _thumbXl,
            _thumbSm,
            _rawDataJson,
            _openFileStream,
            _fileBytesLength,
            tracker,
            cancellationToken: default,
            duplicate: _duplicate);
        _progress?.Report(1.0);
    }
}
