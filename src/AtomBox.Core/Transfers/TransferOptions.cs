using AtomBox.Core.Settings;

namespace AtomBox.Core.Transfers;

public sealed record TransferOptions
{
    public TransferOptions(
        TransferOverwritePolicy overwritePolicy,
        int maxRetryCount = 3)
    {
        if (maxRetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetryCount), "Max retry count cannot be negative.");
        }

        OverwritePolicy = overwritePolicy;
        MaxRetryCount = maxRetryCount;
    }

    public TransferOverwritePolicy OverwritePolicy { get; }

    public int MaxRetryCount { get; }
}
