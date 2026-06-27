using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Transfer.Tests;

public sealed class MemoryTransferStore : ITransferTaskStore, ITransferStateStore
{
    private readonly List<TransferTask> _tasks = [];

    public IReadOnlyList<TransferTask> Tasks => _tasks;

    public List<(TransferTaskId TaskId, TransferProgress? Progress)> ProgressUpdates { get; } = [];

    public Task<OperationResult<TransferTask>> GetByIdAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        var task = _tasks.FirstOrDefault(item => item.Id == taskId);
        return Task.FromResult(task is null
            ? OperationResult<TransferTask>.Failure(StorageError.NotFound("not found"))
            : OperationResult<TransferTask>.Success(task));
    }

    public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IReadOnlyList<TransferTask>>.Success(_tasks.ToArray()));
    }

    public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
    {
        var index = _tasks.FindIndex(item => item.Id == task.Id);
        if (index < 0)
        {
            _tasks.Add(task);
        }
        else
        {
            _tasks[index] = task;
        }

        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
    {
        _tasks.RemoveAll(task => task.Id == taskId);
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(CancellationToken cancellationToken = default)
    {
        var queue = _tasks
            .Where(task => task.Status is TransferStatus.Pending or TransferStatus.Running or TransferStatus.Paused or TransferStatus.Interrupted)
            .Select(task => new TransferStateSnapshot(task, null))
            .ToArray();
        return Task.FromResult(OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(queue));
    }

    public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var history = _tasks
            .Where(task => task.Status is TransferStatus.Succeeded or TransferStatus.Failed or TransferStatus.Canceled or TransferStatus.Interrupted)
            .Skip(skip)
            .Take(take)
            .Select(task => new TransferStateSnapshot(task, null))
            .ToArray();
        return Task.FromResult(OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(history));
    }

    public async Task<OperationResult> UpdateStatusAsync(
        TransferTask task,
        TransferProgress? progress,
        CancellationToken cancellationToken = default)
    {
        ProgressUpdates.Add((task.Id, progress));
        return await SaveAsync(task, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class AnyAccountRepository : IStorageAccountRepository
{
    public Task<OperationResult<StorageAccount>> GetByIdAsync(
        StorageAccountId accountId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(OperationResult<StorageAccount>.Success(
            new StorageAccount(
                accountId,
                StorageProviderCategory.ObjectStorage,
                new StorageProviderId("fake"),
                "Fake Account",
                "fake.example",
                null,
                new CredentialRef("cred-1"),
                now,
                now)));
    }

    public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IReadOnlyList<StorageAccount>>.Success([]));
    }

    public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success());
    }
}

public sealed class FakeProviderFactory : IStorageProviderFactory
{
    public Task<OperationResult<IStorageProvider>> CreateAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IStorageProvider>.Success(new FakeProvider()));
    }
}

public sealed class FixedProviderFactory : IStorageProviderFactory
{
    private readonly IStorageProvider _provider;

    public FixedProviderFactory(IStorageProvider provider)
    {
        _provider = provider;
    }

    public Task<OperationResult<IStorageProvider>> CreateAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IStorageProvider>.Success(_provider));
    }
}

public sealed class FakeProvider : IStorageProvider
{
    public StorageCapabilitySet Capabilities { get; } = new(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download);

    public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
    }

    public Task<OperationResult> DeleteAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success());
    }

    public async Task<OperationResult> UploadAsync(
        RemotePath path,
        Stream content,
        long? contentLength,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[81920];
        long transferred = 0;
        while (true)
        {
            var read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            transferred += read;
        }

        progress?.Report(new TransferProgress(transferred, contentLength, null));
        return OperationResult.Success();
    }

    public async Task<OperationResult> DownloadAsync(
        RemotePath path,
        Stream destination,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[] { 1, 2, 3 };
        await destination.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        progress?.Report(new TransferProgress(payload.Length, payload.Length, null));
        return OperationResult.Success();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class MemoryLocalTransferFileStore : ILocalTransferFileStore
{
    public Task<OperationResult<LocalTransferReadHandle>> OpenReadAsync(
        LocalPath path,
        CancellationToken cancellationToken = default)
    {
        var content = new byte[] { 1, 2, 3, 4 };
        Stream stream = new MemoryStream(content);
        return Task.FromResult(OperationResult<LocalTransferReadHandle>.Success(
            new LocalTransferReadHandle(stream, content.Length)));
    }

    public Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
        LocalPath path,
        CancellationToken cancellationToken = default)
    {
        Stream stream = new MemoryStream();
        return Task.FromResult(OperationResult<LocalTransferWriteHandle>.Success(
            new LocalTransferWriteHandle(stream)));
    }
}

public sealed class FailingUploadProvider : IStorageProvider
{
    public StorageCapabilitySet Capabilities { get; } = new(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download);

    public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
    }

    public Task<OperationResult> DeleteAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> UploadAsync(
        RemotePath path,
        Stream content,
        long? contentLength,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Failure(new StorageError(
            StorageErrorCode.NetworkUnavailable,
            "upload failed",
            StorageErrorCategory.Network,
            isRetryable: true)));
    }

    public Task<OperationResult> DownloadAsync(
        RemotePath path,
        Stream destination,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success());
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class FailingDownloadProvider : IStorageProvider
{
    public StorageCapabilitySet Capabilities { get; } = new(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download);

    public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
    }

    public Task<OperationResult> DeleteAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> UploadAsync(
        RemotePath path,
        Stream content,
        long? contentLength,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> DownloadAsync(
        RemotePath path,
        Stream destination,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Failure(new StorageError(
            StorageErrorCode.NetworkUnavailable,
            "download failed",
            StorageErrorCategory.Network,
            isRetryable: true)));
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class FailingAccountRepository : IStorageAccountRepository
{
    private readonly StorageError _error;

    public FailingAccountRepository(StorageError error)
    {
        _error = error;
    }

    public Task<OperationResult<StorageAccount>> GetByIdAsync(
        StorageAccountId accountId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<StorageAccount>.Failure(_error));
    }

    public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IReadOnlyList<StorageAccount>>.Failure(_error));
    }

    public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Failure(_error));
    }

    public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Failure(_error));
    }

    public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Failure(_error));
    }
}

public sealed class FailingProviderFactory : IStorageProviderFactory
{
    private readonly StorageError _error;

    public FailingProviderFactory(StorageError error)
    {
        _error = error;
    }

    public Task<OperationResult<IStorageProvider>> CreateAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IStorageProvider>.Failure(_error));
    }
}

public static class TransferTaskFactory
{
    public static TransferTask Create(
        TransferStatus status,
        string? statusReason = null,
        StorageErrorCategory? errorCategory = null,
        bool isRetryable = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\upload.txt"),
            new RemotePath("bucket/upload.txt"),
            status,
            new TransferOptions(TransferOverwritePolicy.Ask),
            now,
            now,
            statusReason,
            errorCategory,
            isRetryable);
    }

    public static TransferTask CreateWithDirection(
        TransferDirection direction,
        TransferStatus status,
        string? statusReason = null,
        StorageErrorCategory? errorCategory = null,
        bool isRetryable = false)
    {
        var now = DateTimeOffset.UtcNow;
        var (local, remote) = direction switch
        {
            TransferDirection.Upload => (new LocalPath(@"C:\upload.txt"), new RemotePath("bucket/upload.txt")),
            TransferDirection.Download => (new LocalPath(@"C:\download.txt"), new RemotePath("bucket/download.txt")),
            _ => (new LocalPath(@"C:\file.txt"), new RemotePath("bucket/file.txt"))
        };
        return new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            direction,
            local,
            remote,
            status,
            new TransferOptions(TransferOverwritePolicy.Ask),
            now,
            now,
            statusReason,
            errorCategory,
            isRetryable);
    }

    public static TransferTask CreateWithId(
        TransferTaskId id,
        TransferStatus status,
        string? statusReason = null,
        StorageErrorCategory? errorCategory = null,
        bool isRetryable = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new TransferTask(
            id,
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\upload.txt"),
            new RemotePath("bucket/upload.txt"),
            status,
            new TransferOptions(TransferOverwritePolicy.Ask),
            now,
            now,
            statusReason,
            errorCategory,
            isRetryable);
    }
}
