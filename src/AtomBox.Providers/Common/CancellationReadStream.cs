namespace AtomBox.Providers.Common;

internal sealed class CancellationReadStream : Stream
{
    private readonly Stream _inner;
    private readonly CancellationToken _cancellationToken;
    private readonly bool _leaveOpen;
    private readonly TaskCompletionSource _cancellationObserved =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationReadStream(Stream inner, CancellationToken cancellationToken, bool leaveOpen = false)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cancellationToken = cancellationToken;
        _leaveOpen = leaveOpen;
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

    public Task CancellationObserved => _cancellationObserved.Task;

    public override void Flush()
    {
        _inner.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfCancellationRequested();
        var read = _inner.Read(buffer, offset, count);
        ThrowIfCancellationRequested();
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        ThrowIfCancellationRequested();
        var read = _inner.Read(buffer);
        ThrowIfCancellationRequested();
        return read;
    }

    public override int ReadByte()
    {
        ThrowIfCancellationRequested();
        var value = _inner.ReadByte();
        ThrowIfCancellationRequested();
        return value;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        return ReadAsyncCore(buffer, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private async ValueTask<int> ReadAsyncCore(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationToken,
                cancellationToken);
            var read = await _inner.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
            ThrowIfCancellationRequested();
            return read;
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            _cancellationObserved.TrySetResult();
            throw new OperationCanceledException(_cancellationToken);
        }
    }

    private void ThrowIfCancellationRequested()
    {
        if (!_cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _cancellationObserved.TrySetResult();
        throw new OperationCanceledException(_cancellationToken);
    }
}
