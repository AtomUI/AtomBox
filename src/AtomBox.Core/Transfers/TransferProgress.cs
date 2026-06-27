namespace AtomBox.Core.Transfers;

public sealed record TransferProgress
{
    public TransferProgress(
        long bytesTransferred,
        long? totalBytes,
        double? speedBytesPerSecond)
    {
        if (bytesTransferred < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesTransferred), "Transferred bytes cannot be negative.");
        }

        if (totalBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes), "Total bytes cannot be negative.");
        }

        if (totalBytes is not null && bytesTransferred > totalBytes.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesTransferred), "Transferred bytes cannot exceed total bytes.");
        }

        if (speedBytesPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speedBytesPerSecond), "Transfer speed cannot be negative.");
        }

        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
        SpeedBytesPerSecond = speedBytesPerSecond;
    }

    public long BytesTransferred { get; }

    public long? TotalBytes { get; }

    public double? SpeedBytesPerSecond { get; }

    public double? Percent
    {
        get
        {
            if (TotalBytes is null or <= 0)
            {
                return null;
            }

            return Math.Clamp(BytesTransferred * 100d / TotalBytes.Value, 0d, 100d);
        }
    }
}
