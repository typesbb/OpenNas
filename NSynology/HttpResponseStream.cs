namespace NSynology;

/// <summary>
/// 持有 <see cref="HttpResponseMessage"/> 直到内容流读完，避免把整包缓冲进 MemoryStream。
/// </summary>
internal sealed class HttpResponseStream : Stream
{
    private readonly HttpResponseMessage _response;
    private readonly Stream _content;
    private bool _disposed;

    public HttpResponseStream(HttpResponseMessage response, Stream content)
    {
        _response = response;
        _content = content;
    }

    public override bool CanRead => _content.CanRead;
    public override bool CanSeek => _content.CanSeek;
    public override bool CanWrite => _content.CanWrite;
    public override long Length => _content.Length;

    public override long Position
    {
        get => _content.Position;
        set => _content.Position = value;
    }

    public override void Flush() => _content.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _content.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        _content.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => _content.Read(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _content.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _content.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        _content.Seek(offset, origin);

    public override void SetLength(long value) => _content.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) =>
        _content.Write(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _content.Dispose();
            _response.Dispose();
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _content.DisposeAsync().ConfigureAwait(false);
        _response.Dispose();
        _disposed = true;
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
