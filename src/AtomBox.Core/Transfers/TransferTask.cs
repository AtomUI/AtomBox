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
        bool IsRetryable = false,
        string? FingerprintHashAlgorithm = null,
        string? FingerprintHashValue = null,
        long? FingerprintFileSize = null,
        DateTimeOffset? FingerprintCalculatedAt = null)
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

        ValidateFingerprintMetadata(
            FingerprintHashAlgorithm,
            FingerprintHashValue,
            FingerprintFileSize);

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
        this.FingerprintHashAlgorithm = string.IsNullOrWhiteSpace(FingerprintHashAlgorithm)
            ? null
            : FingerprintHashAlgorithm.Trim().ToLowerInvariant();
        this.FingerprintHashValue = string.IsNullOrWhiteSpace(FingerprintHashValue)
            ? null
            : FingerprintHashValue.Trim().ToLowerInvariant();
        this.FingerprintFileSize = FingerprintFileSize;
        this.FingerprintCalculatedAt = FingerprintCalculatedAt;
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

    public string? FingerprintHashAlgorithm { get; }

    public string? FingerprintHashValue { get; }

    public long? FingerprintFileSize { get; }

    public DateTimeOffset? FingerprintCalculatedAt { get; }

    public bool HasCompleteFingerprintMetadata =>
        !string.IsNullOrWhiteSpace(FingerprintHashAlgorithm) &&
        !string.IsNullOrWhiteSpace(FingerprintHashValue) &&
        FingerprintFileSize is >= 0;

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
            isRetryable,
            FingerprintHashAlgorithm,
            FingerprintHashValue,
            FingerprintFileSize,
            FingerprintCalculatedAt);
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
            IsRetryable,
            FingerprintHashAlgorithm,
            FingerprintHashValue,
            FingerprintFileSize,
            FingerprintCalculatedAt);
    }

    public TransferTask WithFingerprintMetadata(
        string hashAlgorithm,
        string hashValue,
        long fileSize,
        DateTimeOffset calculatedAt)
    {
        return new TransferTask(
            Id,
            StorageAccountId,
            Direction,
            LocalPath,
            RemotePath,
            Status,
            Options,
            CreatedAt,
            UpdatedAt,
            StatusReason,
            ErrorCategory,
            IsRetryable,
            hashAlgorithm,
            hashValue,
            fileSize,
            calculatedAt);
    }

    public bool CanCancel()
    {
        return Status is TransferStatus.Pending or TransferStatus.Running;
    }

    public bool CanRetry()
    {
        return Status is TransferStatus.Failed or TransferStatus.Interrupted;
    }

    private static void ValidateFingerprintMetadata(
        string? hashAlgorithm,
        string? hashValue,
        long? fileSize)
    {
        var hasAny = !string.IsNullOrWhiteSpace(hashAlgorithm) ||
            !string.IsNullOrWhiteSpace(hashValue) ||
            fileSize is not null;
        if (!hasAny)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(hashAlgorithm))
        {
            throw new ArgumentException("Fingerprint hash algorithm is required when fingerprint metadata is provided.", nameof(hashAlgorithm));
        }

        if (string.IsNullOrWhiteSpace(hashValue))
        {
            throw new ArgumentException("Fingerprint hash value is required when fingerprint metadata is provided.", nameof(hashValue));
        }

        if (fileSize is null or < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSize), "Fingerprint file size cannot be negative.");
        }
    }
}
