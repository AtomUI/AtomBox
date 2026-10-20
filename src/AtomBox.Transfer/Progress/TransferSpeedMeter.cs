using AtomBox.Core.Transfers;

namespace AtomBox.Transfer.Progress;

public sealed class TransferSpeedMeter
{
    private static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromSeconds(1);
    private const double DefaultSmoothingFactor = 0.35;

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _sampleInterval;
    private readonly double _smoothingFactor;
    private long? _lastBytesTransferred;
    private MeasurementPoint? _lastMeasurement;
    private double? _lastSpeedBytesPerSecond;

    public TransferSpeedMeter(
        TimeProvider? timeProvider = null,
        TimeSpan? sampleInterval = null,
        double smoothingFactor = DefaultSmoothingFactor)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sampleInterval = sampleInterval ?? DefaultSampleInterval;
        if (_sampleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleInterval),
                "The speed sample interval must be greater than zero.");
        }

        if (smoothingFactor is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(smoothingFactor),
                "The speed smoothing factor must be greater than zero and no greater than one.");
        }

        _smoothingFactor = smoothingFactor;
    }

    public TransferProgress Apply(TransferProgress progress, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var now = GetMeasurementPoint(timestamp);

        if (progress.SpeedBytesPerSecond is not null)
        {
            Remember(progress.BytesTransferred, now);
            _lastSpeedBytesPerSecond = progress.SpeedBytesPerSecond;
            return progress;
        }

        if (_lastBytesTransferred is not { } lastBytesTransferred ||
            _lastMeasurement is not { } lastMeasurement)
        {
            Remember(progress.BytesTransferred, now);
            return progress;
        }

        if (!now.CanMeasureFrom(lastMeasurement) ||
            progress.BytesTransferred < lastBytesTransferred)
        {
            Reset(progress.BytesTransferred, now);
            return progress;
        }

        var elapsed = now.ElapsedSince(lastMeasurement);
        if (elapsed < _sampleInterval)
        {
            return WithLastSpeed(progress);
        }

        var bytesDelta = progress.BytesTransferred - lastBytesTransferred;
        var measuredSpeed = bytesDelta / elapsed.TotalSeconds;
        var smoothedSpeed = _lastSpeedBytesPerSecond is { } lastSpeed
            ? (lastSpeed * (1 - _smoothingFactor)) + (measuredSpeed * _smoothingFactor)
            : measuredSpeed;

        Remember(progress.BytesTransferred, now);
        _lastSpeedBytesPerSecond = smoothedSpeed;
        return new TransferProgress(progress.BytesTransferred, progress.TotalBytes, smoothedSpeed);
    }

    private MeasurementPoint GetMeasurementPoint(DateTimeOffset? timestamp)
    {
        return timestamp is { } explicitTimestamp
            ? new MeasurementPoint(
                explicitTimestamp.UtcDateTime.Ticks,
                TimeSpan.TicksPerSecond,
                MeasurementClock.ExplicitTimestamp)
            : new MeasurementPoint(
                _timeProvider.GetTimestamp(),
                _timeProvider.TimestampFrequency,
                MeasurementClock.MonotonicTimestamp);
    }

    private TransferProgress WithLastSpeed(TransferProgress progress)
    {
        return _lastSpeedBytesPerSecond is { } speed
            ? new TransferProgress(progress.BytesTransferred, progress.TotalBytes, speed)
            : progress;
    }

    private void Reset(long bytesTransferred, MeasurementPoint measurement)
    {
        Remember(bytesTransferred, measurement);
        _lastSpeedBytesPerSecond = null;
    }

    private void Remember(long bytesTransferred, MeasurementPoint measurement)
    {
        _lastBytesTransferred = bytesTransferred;
        _lastMeasurement = measurement;
    }

    private enum MeasurementClock
    {
        MonotonicTimestamp,
        ExplicitTimestamp,
    }

    private readonly record struct MeasurementPoint(
        long Value,
        long Frequency,
        MeasurementClock Clock)
    {
        public bool CanMeasureFrom(MeasurementPoint earlier)
        {
            return Clock == earlier.Clock &&
                   Frequency == earlier.Frequency &&
                   Value >= earlier.Value;
        }

        public TimeSpan ElapsedSince(MeasurementPoint earlier)
        {
            return TimeSpan.FromSeconds((Value - earlier.Value) / (double)Frequency);
        }
    }
}
