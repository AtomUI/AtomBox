using AtomBox.Core.ValueObjects;
using AtomBox.Core.Errors;

namespace AtomBox.Core.Transfers;

public sealed record TransferTask
{
    public TransferTask(
        TransferTaskId Id,
        StorageAccountId StorageAccountId,
        TransferDirection Direction,
        LocalPath LocalPath,
        RemotePath RemotePath,
        TransferStatus Status,
        TransferOptions Options,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? StatusReason = null,
        StorageErrorCategory? ErrorCategory = null,
        bool IsRetryable = false)
    {
        ArgumentNullException.ThrowIfNull(Options);

        if (Id.IsEmpty)
        {
            throw new ArgumentException("Transfer task id cannot be empty.", nameof(Id));
        }

        if (StorageAccountId.IsEmpty)
        {
            throw new ArgumentException("Storage account id cannot be empty.", nameof(StorageAccountId));
        }

        if (LocalPath.IsEmpty)
        {
            throw new ArgumentException("Local path cannot be empty.", nameof(LocalPath));
        }

        if (UpdatedAt < CreatedAt)
        {
            throw new ArgumentException("Updated time cannot be earlier than created time.", nameof(UpdatedAt));
        }

        this.Id = Id;
        this.StorageAccountId = StorageAccountId;
        this.Direction = Direction;
        this.LocalPath = LocalPath;
        this.RemotePath = RemotePath;
        this.Status = Status;
        this.Options = Options;
        this.CreatedAt = CreatedAt;
        this.UpdatedAt = UpdatedAt;
        this.StatusReason = string.IsNullOrWhiteSpace(StatusReason) ? null : StatusReason.Trim();
        this.ErrorCategory = ErrorCategory;
        this.IsRetryable = IsRetryable;
    }

    public TransferTaskId Id { get; }

    public StorageAccountId StorageAccountId { get; }

    public TransferDirection Direction { get; }

    public LocalPath LocalPath { get; }

    public RemotePath RemotePath { get; }

    public TransferStatus Status { get; }

    public TransferOptions Options { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public string? StatusReason { get; }

    public StorageErrorCategory? ErrorCategory { get; }

    public bool IsRetryable { get; }

    public TransferTask WithStatus(
        TransferStatus status,
        DateTimeOffset updatedAt,
        string? statusReason = null,
        StorageErrorCategory? errorCategory = null,
        bool isRetryable = false)
    {
        return new TransferTask(
            Id,
            StorageAccountId,
            Direction,
            LocalPath,
            RemotePath,
            status,
            Options,
            CreatedAt,
            updatedAt,
            statusReason,
            errorCategory,
            isRetryable);
    }

    public TransferTask WithLocalPath(LocalPath localPath, DateTimeOffset updatedAt)
    {
        return new TransferTask(
            Id,
            StorageAccountId,
            Direction,
            localPath,
            RemotePath,
            Status,
            Options,
            CreatedAt,
            updatedAt,
            StatusReason,
            ErrorCategory,
            IsRetryable);
    }

    public bool CanCancel()
    {
        return Status is TransferStatus.Pending or TransferStatus.Running;
    }

    public bool CanRetry()
    {
        return Status is TransferStatus.Failed or TransferStatus.Interrupted;
    }
}
