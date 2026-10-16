using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Transfer.State;
using AtomUI.City.State;

namespace AtomBox.Transfer.Tests;

public static class TransferStateTestFactory
{
    public static TransferStateProjector CreateProjector(ITransferStateStore? store = null)
    {
        var registry = new ApplicationStateRegistry();
        var state = new AtomBoxTransferState(registry, registry, registry);
        return new TransferStateProjector(state, store ?? new MemoryTransferStore());
    }
}

public sealed class MemoryTransferStore : ITransferTaskStore, ITransferStateStore
{
    private readonly List<TransferTask> _tasks = [];
    private readonly Dictionary<TransferTaskId, TransferProgress> _progress = [];
    private readonly object _gate = new();
    private readonly Dictionary<(TransferTaskId TaskId, TransferStatus Status), TaskCompletionSource<TransferTask>> _statusWaiters = [];

    public IReadOnlyList<TransferTask> Tasks
    {
        get
        {
            lock (_gate)
            {
                return _tasks.ToArray();
            }
        }
    }

    public List<(TransferTaskId TaskId, TransferProgress? Progress)> ProgressUpdates { get; } = [];

    public StorageError? ListFailure { get; set; }

    public StorageError? QueueFailure { get; set; }

    public Task<OperationResult<TransferTask>> GetByIdAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        TransferTask? task;
        lock (_gate)
        {
            task = _tasks.FirstOrDefault(item => item.Id == taskId);
        }
        return Task.FromResult(task is null
            ? OperationResult<TransferTask>.Failure(StorageError.NotFound("not found"))
            : OperationResult<TransferTask>.Success(task));
    }

    public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (ListFailure is not null)
            {
                return Task.FromResult(OperationResult<IReadOnlyList<TransferTask>>.Failure(ListFailure));
            }

            return Task.FromResult(OperationResult<IReadOnlyList<TransferTask>>.Success(_tasks.ToArray()));
        }
    }

    public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
    {
        lock (_gate)
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

            NotifyStatusLocked(task);
        }

        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult<TransferTask>> CompareExchangeAsync(
        TransferTask replacement,
        long expectedRevision,
        TransferStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _tasks.FindIndex(item => item.Id == replacement.Id);
            if (index < 0)
            {
                return Task.FromResult(OperationResult<TransferTask>.Failure(
                    StorageError.NotFound("not found")));
            }

            var current = _tasks[index];
            if (current.Revision != expectedRevision || current.Status != expectedStatus)
            {
                return Task.FromResult(OperationResult<TransferTask>.Failure(new StorageError(
                    StorageErrorCode.Conflict,
                    "conflict",
                    StorageErrorCategory.Conflict)));
            }

            if (replacement.Revision != expectedRevision + 1)
            {
                return Task.FromResult(OperationResult<TransferTask>.Failure(
                    StorageError.Validation("invalid revision")));
            }

            _tasks[index] = replacement;
            NotifyStatusLocked(replacement);
            return Task.FromResult(OperationResult<TransferTask>.Success(replacement));
        }
    }

    public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _tasks.RemoveAll(task => task.Id == taskId);
            _progress.Remove(taskId);
        }
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (QueueFailure is not null)
            {
                return Task.FromResult(OperationResult<IReadOnlyList<TransferStateSnapshot>>.Failure(QueueFailure));
            }

            var queue = _tasks
                .Where(task => task.Status is TransferStatus.Pending or TransferStatus.Running or TransferStatus.Canceling or TransferStatus.Paused or TransferStatus.Interrupted)
                .Select(task => new TransferStateSnapshot(
                    task,
                    _progress.TryGetValue(task.Id, out var progress) ? progress : null))
                .ToArray();
            return Task.FromResult(OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(queue));
        }
    }

    public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var history = _tasks
                .Where(task => task.Status is TransferStatus.Succeeded or TransferStatus.Failed or TransferStatus.Canceled or TransferStatus.Interrupted)
                .Skip(skip)
                .Take(take)
                .Select(task => new TransferStateSnapshot(
                    task,
                    _progress.TryGetValue(task.Id, out var progress) ? progress : null))
                .ToArray();
            return Task.FromResult(OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(history));
        }
    }

    public async Task<OperationResult> UpdateStatusAsync(
        TransferTask task,
        TransferProgress? progress,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ProgressUpdates.Add((task.Id, progress));
            if (progress is not null)
            {
                _progress[task.Id] = progress;
            }
        }
        return await SaveAsync(task, cancellationToken).ConfigureAwait(false);
    }

    public Task<OperationResult> SaveProgressBatchAsync(
        IReadOnlyList<TransferProgressUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            foreach (var update in updates)
            {
                var task = _tasks.FirstOrDefault(item => item.Id == update.TaskId);
                if (task is null || task.Revision != update.ExecutionRevision ||
                    task.Status is not (TransferStatus.Running or TransferStatus.Canceling))
                {
                    continue;
                }

                _progress[update.TaskId] = update.Progress;
                ProgressUpdates.Add((update.TaskId, update.Progress));
            }
        }

        return Task.FromResult(OperationResult.Success());
    }

    public Task<TransferTask> WaitForStatusAsync(
        TransferTaskId taskId,
        TransferStatus status,
        CancellationToken cancellationToken = default)
    {
        Task<TransferTask> task;
        lock (_gate)
        {
            var existing = _tasks.FirstOrDefault(item => item.Id == taskId && item.Status == status);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            var key = (taskId, status);
            if (!_statusWaiters.TryGetValue(key, out var waiter))
            {
                waiter = new TaskCompletionSource<TransferTask>(TaskCreationOptions.RunContinuationsAsynchronously);
                _statusWaiters.Add(key, waiter);
            }

            task = waiter.Task;
        }

        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    private void NotifyStatusLocked(TransferTask task)
    {
        var key = (task.Id, task.Status);
        if (_statusWaiters.Remove(key, out var waiter))
        {
            waiter.TrySetResult(task);
        }
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
