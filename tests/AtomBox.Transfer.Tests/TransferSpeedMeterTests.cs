using AtomBox.Core.Transfers;
using AtomBox.Transfer.Progress;

namespace AtomBox.Transfer.Tests;

public sealed class TransferSpeedMeterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Apply_InitialSample_HasNoSpeed()
    {
        var meter = new TransferSpeedMeter();

        var result = meter.Apply(Progress(0), Start);

        Assert.Null(result.SpeedBytesPerSecond);
    }

    [Fact]
    public void Apply_FullWindow_ComputesBytesPerSecond()
    {
        var meter = new TransferSpeedMeter();
        meter.Apply(Progress(0), Start);

        var result = meter.Apply(Progress(1024 * 1024), Start.AddSeconds(1));

        Assert.Equal(1024 * 1024, result.SpeedBytesPerSecond);
    }

    [Fact]
    public void Apply_DefaultClock_UsesMonotonicTimeProviderTimestamp()
    {
        var timeProvider = new ManualTimeProvider();
        var meter = new TransferSpeedMeter(timeProvider);
        meter.Apply(Progress(0));
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var result = meter.Apply(Progress(2_000_000));

        Assert.Equal(2_000_000, result.SpeedBytesPerSecond);
    }

    [Fact]
    public void Apply_BurstCallbacks_DoNotCreateInstantaneousSpike()
    {
        var meter = new TransferSpeedMeter();
        meter.Apply(Progress(0), Start);

        for (var index = 1; index <= 100; index++)
        {
            var result = meter.Apply(
                Progress(index * 10_000),
                Start.AddMilliseconds(index * 5));
            Assert.Null(result.SpeedBytesPerSecond);
        }

        var sampled = meter.Apply(Progress(1_000_000), Start.AddSeconds(1));

        Assert.Equal(1_000_000, sampled.SpeedBytesPerSecond);
    }

    [Fact]
    public void Apply_ShortInterval_CarriesLastStableSpeed()
    {
        var meter = new TransferSpeedMeter();
        meter.Apply(Progress(0), Start);
        var stable = meter.Apply(Progress(1_000_000), Start.AddSeconds(1));

        var burst = meter.Apply(Progress(1_100_000), Start.AddSeconds(1).AddMilliseconds(1));

        Assert.Equal(stable.SpeedBytesPerSecond, burst.SpeedBytesPerSecond);
    }

    [Fact]
    public void Apply_MultipleWindows_UsesExponentialSmoothing()
    {
        var meter = new TransferSpeedMeter();
        meter.Apply(Progress(0), Start);
        meter.Apply(Progress(1_000_000), Start.AddSeconds(1));

        var result = meter.Apply(Progress(3_000_000), Start.AddSeconds(2));

        Assert.Equal(1_350_000, result.SpeedBytesPerSecond);
    }

    [Fact]
    public void Apply_ReportedSpeed_PreservesProviderMeasurement()
    {
        var meter = new TransferSpeedMeter();
        var providerProgress = new TransferProgress(1_000, 10_000, 42_000);

        var result = meter.Apply(providerProgress, Start);

        Assert.Same(providerProgress, result);
        Assert.Equal(42_000, result.SpeedBytesPerSecond);
    }

    [Fact]
    public void Apply_BytesRegression_ResetsMeasurementWindow()
    {
        var meter = new TransferSpeedMeter();
        meter.Apply(Progress(1_000), Start);
        meter.Apply(Progress(2_000), Start.AddSeconds(1));

        var regressed = meter.Apply(Progress(500), Start.AddSeconds(2));
        var next = meter.Apply(Progress(1_500), Start.AddSeconds(3));

        Assert.Null(regressed.SpeedBytesPerSecond);
        Assert.Equal(1_000, next.SpeedBytesPerSecond);
    }

    private static TransferProgress Progress(long bytesTransferred)
    {
        return new TransferProgress(bytesTransferred, totalBytes: null, speedBytesPerSecond: null);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return _timestamp;
        }

        public void Advance(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
        }
    }
}
