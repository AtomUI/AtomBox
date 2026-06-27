namespace AtomBox.Core.Transfers;

public sealed class LocalTransferReadHandle : IAsyncDisposable
{
    private readonly Stream _stream;

    public LocalTransferReadHandle(Stream stream, long? length)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead)
        {
            throw new ArgumentException("Local transfer read stream must be readable.", nameof(stream));
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Local transfer read length cannot be negative.");
        }

        Stream = stream;
        Length = length;
    }

    public Stream Stream { get; }

    public long? Length { get; }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
