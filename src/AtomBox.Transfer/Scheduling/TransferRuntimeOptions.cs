namespace AtomBox.Transfer.Scheduling;

public sealed record TransferRuntimeOptions
{
    public TimeSpan ProgressFlushInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan InfrastructureRetryDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    public int MaxConsecutiveInfrastructureFailures { get; init; } = 5;

    public TimeSpan ShutdownGracePeriod { get; init; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (ProgressFlushInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ProgressFlushInterval));
        }

        if (InfrastructureRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(InfrastructureRetryDelay));
        }

        if (MaxConsecutiveInfrastructureFailures < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConsecutiveInfrastructureFailures));
        }

        if (ShutdownGracePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ShutdownGracePeriod));
        }
    }
}
