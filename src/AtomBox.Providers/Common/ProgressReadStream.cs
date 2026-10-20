using AtomBox.Core.Transfers;

namespace AtomBox.Providers.Common;

internal sealed class ProgressReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long? _totalBytes;
    private readonly IProgress<TransferProgress>? _progress;
    private readonly CancellationToken _cancellationToken;
    private long _bytesRead;

    public ProgressReadStream(
        Stream inner,
        long? totalBytes,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _totalBytes = totalBytes;
        _progress = progress;
        _cancellationToken = cancellationToken;
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
        _cancellationToken.ThrowIfCancellationRequested();
        var read = _inner.Read(buffer, offset, count);
        _cancellationToken.ThrowIfCancellationRequested();
        Report(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var read = _inner.Read(buffer);
        _cancellationToken.ThrowIfCancellationRequested();
        Report(read);
        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        int read;
        if (!_cancellationToken.CanBeCanceled)
        {
            read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        else if (!cancellationToken.CanBeCanceled || cancellationToken == _cancellationToken)
        {
            read = await _inner.ReadAsync(buffer, _cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationToken,
                cancellationToken);
            read = await _inner.ReadAsync(buffer, linkedCancellation.Token).ConfigureAwait(false);
        }

        _cancellationToken.ThrowIfCancellationRequested();
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
