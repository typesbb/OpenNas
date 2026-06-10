using System.Net;

namespace NSynology.Foto;

/// <summary>按实际写入 HTTP 流的字节数报告上传进度（非 NAS 轮询 API）。</summary>
internal sealed class ProgressByteArrayContent : HttpContent
{
    private readonly byte[] _bytes;
    private readonly IProgress<double>? _progress;

    public ProgressByteArrayContent(byte[] bytes, IProgress<double>? progress = null)
    {
        _bytes = bytes;
        _progress = progress;
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _bytes.Length;
        return true;
    }

    protected override async Task SerializeToStreamAsync(Stream target, TransportContext? context)
    {
        const int chunkSize = 81920;
        var total = _bytes.Length;
        if (total == 0)
        {
            _progress?.Report(1);
            return;
        }

        var sent = 0;
        for (var offset = 0; offset < total; offset += chunkSize)
        {
            var count = Math.Min(chunkSize, total - offset);
            await target.WriteAsync(_bytes.AsMemory(offset, count));
            sent += count;
            _progress?.Report((double)sent / total);
        }
    }
}
