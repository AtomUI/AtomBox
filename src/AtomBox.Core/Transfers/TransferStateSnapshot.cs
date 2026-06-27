using AtomBox.Core.Errors;

namespace AtomBox.Core.Transfers;

public sealed record TransferStateSnapshot
{
    public TransferStateSnapshot(
        TransferTask task,
        TransferProgress? progress)
    {
        Task = task ?? throw new ArgumentNullException(nameof(task));
        Progress = progress;
    }

    public TransferTask Task { get; }

    public TransferProgress? Progress { get; }

    public string? StatusReason => Task.StatusReason;

    public StorageErrorCategory? ErrorCategory => Task.ErrorCategory;

    public bool CanRetry => Task.CanRetry() && Task.IsRetryable;
}
