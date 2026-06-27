using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Transfers;

public sealed class LocalTransferWriteHandle : IAsyncDisposable
{
    private readonly Stream _stream;

    public LocalTransferWriteHandle(Stream stream)
        : this(stream, default)
    {
    }

    public LocalTransferWriteHandle(Stream stream, LocalPath path)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (!stream.CanWrite)
        {
            throw new ArgumentException("Local transfer write stream must be writable.", nameof(stream));
        }

        Stream = stream;
        Path = path;
    }

    public Stream Stream { get; }

    public LocalPath Path { get; }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
