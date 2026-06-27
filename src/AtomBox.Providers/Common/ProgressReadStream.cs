using AtomBox.Core.Transfers;

namespace AtomBox.Providers.Common;

internal sealed class ProgressReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long? _totalBytes;
    private readonly IProgress<TransferProgress>? _progress;
    private long _bytesRead;

    public ProgressReadStream(Stream inner, long? totalBytes, IProgress<TransferProgress>? progress)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _totalBytes = totalBytes;
        _progress = progress;
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public long BytesRead => _bytesRead;

    public override void Flush()
    {
        _inner.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Report(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Report(read);
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException("Progress read stream does not support writing.");
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Progress read stream does not support writing.");
    }

    private void Report(int read)
    {
        if (read <= 0)
        {
            return;
        }

        _bytesRead += read;
        _progress?.Report(new TransferProgress(_bytesRead, _totalBytes, null));
    }
}
