using AtomBox.Core.Errors;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Tests;

public sealed class TransferModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly TransferOptions DefaultOptions = new(TransferOverwritePolicy.Overwrite);

    [Fact]
    public void TransferProgress_ComputesPercentAndRejectsImpossibleValues()
    {
        var progress = new TransferProgress(25, 100, 1024);

        Assert.Equal(25, progress.Percent);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferProgress(-1, 100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferProgress(101, 100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferProgress(0, 100, -1));
    }

    [Fact]
    public void TransferProgress_NegativeBytesTransferred_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferProgress(-1, 100, 0));
    }

    [Fact]
    public void TransferProgress_NegativeTotalBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferProgress(0, -1, 0));
    }

    [Fact]
    public void TransferProgress_BytesExceedsTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferProgress(101, 100, 0));
    }

    [Fact]
    public void TransferProgress_NegativeSpeed_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferProgress(0, 100, -1));
    }

    [Fact]
    public void TransferProgress_Percent_NullWhenTotalNull()
    {
        var progress = new TransferProgress(50, null, null);
        Assert.Null(progress.Percent);
    }

    [Fact]
    public void TransferProgress_Percent_ClampsTo100()
    {
        var progress = new TransferProgress(100, 100, null);
        Assert.Equal(100, progress.Percent);
    }

    [Fact]
    public void TransferProgress_Percent_ZeroWhenTotalZero()
    {
        var progress = new TransferProgress(0, 0, null);
        Assert.Null(progress.Percent);
    }

    [Fact]
    public void TransferStatus_Values_AreStable()
    {
        Assert.Equal(0, (int)TransferStatus.Pending);
        Assert.Equal(1, (int)TransferStatus.Running);
        Assert.Equal(2, (int)TransferStatus.Paused);
        Assert.Equal(3, (int)TransferStatus.Interrupted);
        Assert.Equal(4, (int)TransferStatus.Succeeded);
        Assert.Equal(5, (int)TransferStatus.Failed);
        Assert.Equal(6, (int)TransferStatus.Canceled);
    }

    [Fact]
    public void TransferTask_Constructor_RequiresValidId()
    {
        Assert.Throws<ArgumentException>(() => new TransferTask(
            default,
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\a.txt"),
            new RemotePath("bucket/a.txt"),
            TransferStatus.Pending,
            DefaultOptions,
            Now,
            Now));
    }

    [Fact]
    public void TransferTask_Constructor_RequiresValidStorageAccountId()
    {
        Assert.Throws<ArgumentException>(() => new TransferTask(
            TransferTaskId.New(),
            default,
            TransferDirection.Upload,
            new LocalPath(@"C:\a.txt"),
            new RemotePath("bucket/a.txt"),
            TransferStatus.Pending,
            DefaultOptions,
            Now,
            Now));
    }

    [Fact]
    public void TransferTask_Constructor_RequiresValidLocalPath()
    {
        Assert.Throws<ArgumentException>(() => new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            default,
            new RemotePath("bucket/a.txt"),
            TransferStatus.Pending,
            DefaultOptions,
            Now,
            Now));
    }

    [Fact]
    public void TransferTask_Constructor_RequiresNonNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\a.txt"),
            new RemotePath("bucket/a.txt"),
            TransferStatus.Pending,
            null!,
            Now,
            Now));
    }

    [Fact]
    public void TransferTask_Constructor_UpdatedAtNotEarlierThanCreatedAt()
    {
        Assert.Throws<ArgumentException>(() => new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\a.txt"),
            new RemotePath("bucket/a.txt"),
            TransferStatus.Pending,
            DefaultOptions,
            Now,
            Now.AddSeconds(-1)));
    }

    [Fact]
    public void TransferTask_DefaultStatusReasonNull()
    {
        var task = CreateTask(TransferStatus.Pending);
        Assert.Null(task.StatusReason);
    }

    [Fact]
    public void TransferTask_FingerprintMetadata_IsOptionalAndPreserved()
    {
        var task = new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\temp\a.txt"),
            new RemotePath("bucket/a.txt"),
            TransferStatus.Pending,
            DefaultOptions,
            Now,
            Now,
            FingerprintHashAlgorithm: "SHA256",
            FingerprintHashValue: "ABCDEF",
            FingerprintFileSize: 0,
            FingerprintCalculatedAt: Now);

        Assert.True(task.HasCompleteFingerprintMetadata);
        Assert.Equal("sha256", task.FingerprintHashAlgorithm);
        Assert.Equal("abcdef", task.FingerprintHashValue);

        var updated = task.WithStatus(TransferStatus.Succeeded, Now.AddSeconds(1));
        Assert.Equal("sha256", updated.FingerprintHashAlgorithm);
        Assert.Equal("abcdef", updated.FingerprintHashValue);
        Assert.Equal(0, updated.FingerprintFileSize);
    }

    [Fact]
    public void TransferTask_PartialFingerprintMetadata_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\temp\a.txt"),
            new RemotePath("bucket/a.txt"),
            TransferStatus.Pending,
            DefaultOptions,
            Now,
            Now,
            FingerprintHashAlgorithm: "sha256"));
    }

    [Fact]
    public void TransferTask_StatusReasonIsTrimmed()
    {
        var task = CreateTask(TransferStatus.Failed, "  reason  ", StorageErrorCategory.Unknown);
        Assert.Equal("reason", task.StatusReason);
    }

    [Fact]
    public void Pending_CanCancel_CanRetry()
    {
        var task = CreateTask(TransferStatus.Pending);
        Assert.True(task.CanCancel());
        Assert.False(task.CanRetry());
    }

    [Fact]
    public void Running_CanCancel_CanRetry()
    {
        var task = CreateTask(TransferStatus.Running);
        Assert.True(task.CanCancel());
        Assert.False(task.CanRetry());
    }

    [Fact]
    public void Paused_CanCancel_CanRetry()
    {
        var task = CreateTask(TransferStatus.Paused);
        Assert.False(task.CanCancel());
        Assert.False(task.CanRetry());
    }

    [Fact]
    public void Succeeded_CanCancel_CanRetry()
    {
        var task = CreateTask(TransferStatus.Succeeded);
        Assert.False(task.CanCancel());
        Assert.False(task.CanRetry());
    }

    [Fact]
    public void Failed_CanRetry_CanCancel()
    {
        var task = CreateTask(TransferStatus.Failed);
        Assert.False(task.CanCancel());
        Assert.True(task.CanRetry());
    }

    [Fact]
    public void Canceled_CanCancel_CanRetry()
    {
        var task = CreateTask(TransferStatus.Canceled);
        Assert.False(task.CanCancel());
        Assert.False(task.CanRetry());
    }

    [Fact]
    public void Interrupted_CanRetry_CanCancel()
    {
        var task = CreateTask(TransferStatus.Interrupted);
        Assert.False(task.CanCancel());
        Assert.True(task.CanRetry());
    }

    [Fact]
    public void Pending_To_Running()
    {
        var task = CreateTask(TransferStatus.Pending);
        var updated = task.WithStatus(TransferStatus.Running, Now.AddSeconds(1));
        Assert.Equal(TransferStatus.Running, updated.Status);
        Assert.Equal(Now.AddSeconds(1), updated.UpdatedAt);
    }

    [Fact]
    public void Pending_To_Canceled()
    {
        var task = CreateTask(TransferStatus.Pending);
        var updated = task.WithStatus(TransferStatus.Canceled, Now.AddSeconds(1));
        Assert.Equal(TransferStatus.Canceled, updated.Status);
    }

    [Fact]
    public void Running_To_Succeeded()
    {
        var task = CreateTask(TransferStatus.Running);
        var updated = task.WithStatus(TransferStatus.Succeeded, Now.AddSeconds(2));
        Assert.Equal(TransferStatus.Succeeded, updated.Status);
    }

    [Fact]
    public void Running_To_Failed_PopulatesErrorFields()
    {
        var task = CreateTask(TransferStatus.Running);
        var updated = task.WithStatus(
            TransferStatus.Failed,
            Now.AddSeconds(2),
            "timeout",
            StorageErrorCategory.Network,
            isRetryable: true);
        Assert.Equal(TransferStatus.Failed, updated.Status);
        Assert.Equal("timeout", updated.StatusReason);
        Assert.Equal(StorageErrorCategory.Network, updated.ErrorCategory);
        Assert.True(updated.IsRetryable);
    }

    [Fact]
    public void Running_To_Canceled()
    {
        var task = CreateTask(TransferStatus.Running);
        var updated = task.WithStatus(TransferStatus.Canceled, Now.AddSeconds(1));
        Assert.Equal(TransferStatus.Canceled, updated.Status);
    }

    [Fact]
    public void Running_To_Interrupted()
    {
        var task = CreateTask(TransferStatus.Running);
        var updated = task.WithStatus(
            TransferStatus.Interrupted,
            Now.AddSeconds(1),
            "app closed",
            StorageErrorCategory.Unknown,
            isRetryable: true);
        Assert.Equal(TransferStatus.Interrupted, updated.Status);
        Assert.True(updated.IsRetryable);
    }

    [Fact]
    public void Failed_To_Pending_ResetsErrorInfo()
    {
        var task = CreateTask(TransferStatus.Failed, "error", StorageErrorCategory.Network, isRetryable: true);
        var updated = task.WithStatus(TransferStatus.Pending, Now.AddSeconds(2));

        Assert.Equal(TransferStatus.Pending, updated.Status);
        Assert.Null(updated.StatusReason);
        Assert.Null(updated.ErrorCategory);
        Assert.False(updated.IsRetryable);
    }

    [Fact]
    public void Interrupted_To_Pending()
    {
        var task = CreateTask(TransferStatus.Interrupted, "crash", StorageErrorCategory.Unknown, isRetryable: true);
        var updated = task.WithStatus(TransferStatus.Pending, Now.AddSeconds(1));
        Assert.Equal(TransferStatus.Pending, updated.Status);
    }

    [Fact]
    public void Paused_To_Pending()
    {
        var task = CreateTask(TransferStatus.Paused);
        var updated = task.WithStatus(TransferStatus.Pending, Now.AddSeconds(1));
        Assert.Equal(TransferStatus.Pending, updated.Status);
    }

    [Fact]
    public void WithStatus_OlderUpdatedAt_Throws()
    {
        var task = CreateTask(TransferStatus.Pending);
        Assert.Throws<ArgumentException>(() => task.WithStatus(TransferStatus.Running, Now.AddSeconds(-1)));
    }

    [Fact]
    public void WithStatus_SameUpdatedAt_Allowed()
    {
        var task = CreateTask(TransferStatus.Pending);
        var updated = task.WithStatus(TransferStatus.Running, Now);
        Assert.Equal(TransferStatus.Running, updated.Status);
    }

    [Fact]
    public void CanRetry_RequiresStatusFailedOrInterrupted()
    {
        Assert.False(CreateTask(TransferStatus.Pending).CanRetry());
        Assert.False(CreateTask(TransferStatus.Running).CanRetry());
        Assert.False(CreateTask(TransferStatus.Paused).CanRetry());
        Assert.False(CreateTask(TransferStatus.Succeeded).CanRetry());
        Assert.True(CreateTask(TransferStatus.Failed).CanRetry());
        Assert.True(CreateTask(TransferStatus.Interrupted).CanRetry());
        Assert.False(CreateTask(TransferStatus.Canceled).CanRetry());
    }

    [Fact]
    public void CanCancel_RequiresPendingOrRunning()
    {
        Assert.True(CreateTask(TransferStatus.Pending).CanCancel());
        Assert.True(CreateTask(TransferStatus.Running).CanCancel());
        Assert.False(CreateTask(TransferStatus.Paused).CanCancel());
        Assert.False(CreateTask(TransferStatus.Succeeded).CanCancel());
        Assert.False(CreateTask(TransferStatus.Failed).CanCancel());
        Assert.False(CreateTask(TransferStatus.Interrupted).CanCancel());
        Assert.False(CreateTask(TransferStatus.Canceled).CanCancel());
    }

    [Fact]
    public void TransferTask_DefaultOptions_AreReasonable()
    {
        var options = new TransferOptions(TransferOverwritePolicy.Ask);
        Assert.Equal(TransferOverwritePolicy.Ask, options.OverwritePolicy);
        Assert.Equal(3, options.MaxRetryCount);
    }

    [Fact]
    public void TransferOptions_RejectsNegativeMaxRetryCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferOptions(TransferOverwritePolicy.Overwrite, -1));
    }

    [Fact]
    public void TransferStateSnapshot_TaskRequired()
    {
        Assert.Throws<ArgumentNullException>(() => new TransferStateSnapshot(null!, null));
    }

    [Fact]
    public void TransferStateSnapshot_DelegatesStatusReason()
    {
        var task = CreateTask(TransferStatus.Failed, "disk full", StorageErrorCategory.Infrastructure);
        var snapshot = new TransferStateSnapshot(task, null);
        Assert.Equal("disk full", snapshot.StatusReason);
        Assert.Equal(StorageErrorCategory.Infrastructure, snapshot.ErrorCategory);
    }

    [Fact]
    public void TransferStateSnapshot_CanRetry_UsesBothCanRetryAndIsRetryable()
    {
        var retryableFailed = CreateTask(TransferStatus.Failed, "err", StorageErrorCategory.Network, isRetryable: true);
        Assert.True(new TransferStateSnapshot(retryableFailed, null).CanRetry);

        var nonRetryableFailed = CreateTask(TransferStatus.Failed, "err", StorageErrorCategory.Validation, isRetryable: false);
        Assert.False(new TransferStateSnapshot(nonRetryableFailed, null).CanRetry);

        var succeeded = CreateTask(TransferStatus.Succeeded);
        Assert.False(new TransferStateSnapshot(succeeded, null).CanRetry);
    }

    private static TransferTask CreateTask(
        TransferStatus status,
        string? statusReason = null,
        StorageErrorCategory? errorCategory = null,
        bool isRetryable = false)
    {
        return new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\temp\a.txt"),
            new RemotePath("bucket/a.txt"),
            status,
            new TransferOptions(TransferOverwritePolicy.Overwrite),
            Now,
            Now,
            statusReason,
            errorCategory,
            isRetryable);
    }
}
