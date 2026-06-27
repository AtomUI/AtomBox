namespace AtomBox.Core.Settings;

public sealed record ApplicationSettings
{
    public ApplicationSettings(
        int DefaultConcurrency,
        TransferOverwritePolicy DefaultOverwritePolicy,
        bool KeepCompletedTransfers)
    {
        if (DefaultConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultConcurrency), "Default concurrency must be greater than zero.");
        }

        this.DefaultConcurrency = DefaultConcurrency;
        this.DefaultOverwritePolicy = DefaultOverwritePolicy;
        this.KeepCompletedTransfers = KeepCompletedTransfers;
    }

    public int DefaultConcurrency { get; }

    public TransferOverwritePolicy DefaultOverwritePolicy { get; }

    public bool KeepCompletedTransfers { get; }
}
