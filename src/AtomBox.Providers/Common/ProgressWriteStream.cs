using AtomBox.Core.Transfers;

namespace AtomBox.Providers.Common;

internal sealed class ProgressWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly long? _totalBytes;
    private readonly IProgress<TransferProgress>? _progress;
    private long _bytesWritten;

    public ProgressWriteStream(Stream inner, long? totalBytes, IProgress<TransferProgress>? progress)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _totalBytes = totalBytes;
        _progress = progress;
    }

    public override bool CanRead => false;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => _inner.CanWrite;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush()
    {
        _inner.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _inner.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Progress write stream does not support reading.");
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _inner.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        Report(count);
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        Report(buffer.Length);
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        await _inner
            .WriteAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false);
        Report(count);
    }

    private void Report(int written)
    {
        if (written <= 0)
        {
            return;
        }

        _bytesWritten += written;
        _progress?.Report(new TransferProgress(_bytesWritten, _totalBytes, null));
    }
}
