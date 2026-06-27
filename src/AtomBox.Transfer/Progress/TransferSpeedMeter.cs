using AtomBox.Core.Transfers;

namespace AtomBox.Transfer.Progress;

public sealed class TransferSpeedMeter
{
    private long? _lastBytesTransferred;
    private DateTimeOffset? _lastTimestamp;

    public TransferProgress Apply(TransferProgress progress, DateTimeOffset? timestamp = null)
    {
        if (progress.SpeedBytesPerSecond is not null)
        {
            Remember(progress, timestamp ?? DateTimeOffset.UtcNow);
            return progress;
        }

        var now = timestamp ?? DateTimeOffset.UtcNow;
        double? speedBytesPerSecond = null;
        if (_lastBytesTransferred is { } lastBytesTransferred &&
            _lastTimestamp is { } lastTimestamp)
        {
            var elapsedSeconds = (now - lastTimestamp).TotalSeconds;
            var bytesDelta = progress.BytesTransferred - lastBytesTransferred;
            if (elapsedSeconds > 0 && bytesDelta >= 0)
            {
                speedBytesPerSecond = bytesDelta / elapsedSeconds;
            }
        }

        Remember(progress, now);
        return speedBytesPerSecond is null
            ? progress
            : new TransferProgress(progress.BytesTransferred, progress.TotalBytes, speedBytesPerSecond);
    }

    private void Remember(TransferProgress progress, DateTimeOffset timestamp)
    {
        _lastBytesTransferred = progress.BytesTransferred;
        _lastTimestamp = timestamp;
    }
}
