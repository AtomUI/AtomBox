using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Transfer.Workers;

namespace AtomBox.Transfer.Tests;

public sealed class TransferWorkerTests
{
    [Fact]
    public async Task Upload_SingleFile_Success()
    {
        var store = new MemoryTransferStore();
        var worker = CreateWorker(store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsSuccess);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Succeeded, saved.Status);
    }

    [Fact]
    public async Task Download_SingleFile_Success()
    {
        var store = new MemoryTransferStore();
        var worker = CreateWorker(store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Download, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsSuccess);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Succeeded, saved.Status);
    }

    [Fact]
    public async Task Download_RenamedLocalTarget_PersistsActualLocalPath()
    {
        var store = new MemoryTransferStore();
        var actualPath = new LocalPath(@"C:\download (1).txt");
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(new DisposableTrackerProvider()),
            new RenamingLocalTransferFileStore(actualPath),
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Download, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsSuccess);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Succeeded, saved.Status);
        Assert.Equal(actualPath, saved.LocalPath);
    }

    [Fact]
    public async Task Upload_ProviderCreationFailed()
    {
        var store = new MemoryTransferStore();
        var error = new StorageError(StorageErrorCode.ProviderUnavailable, "provider down", StorageErrorCategory.Provider);
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FailingProviderFactory(error),
            new MemoryLocalTransferFileStore(),
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Provider, result.Error?.Category);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Failed, saved.Status);
    }

    [Fact]
    public async Task Download_ProviderCreationFailed()
    {
        var store = new MemoryTransferStore();
        var error = new StorageError(StorageErrorCode.ProviderUnavailable, "provider down", StorageErrorCategory.Provider);
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FailingProviderFactory(error),
            new MemoryLocalTransferFileStore(),
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Download, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Provider, result.Error?.Category);
    }

    [Fact]
    public async Task Upload_ProviderAuthenticationFailed()
    {
        var store = new MemoryTransferStore();
        var error = new StorageError(StorageErrorCode.AuthenticationFailed, "bad credentials", StorageErrorCategory.Authentication);
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FailingProviderFactory(error),
            new MemoryLocalTransferFileStore(),
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Authentication, result.Error?.Category);
    }

    [Fact]
    public async Task Upload_LocalFileNotFound()
    {
        var store = new MemoryTransferStore();
        var localFiles = new FailingLocalTransferFileStore(
            new StorageError(StorageErrorCode.NotFound, "file not found", StorageErrorCategory.NotFound));
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FakeProviderFactory(),
            localFiles,
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Failed, saved.Status);
    }

    [Fact]
    public async Task Upload_RemoteConflict()
    {
        var store = new MemoryTransferStore();
        var error = new StorageError(StorageErrorCode.Conflict, "object exists", StorageErrorCategory.Conflict);
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(new FailingUploadWithErrorProvider(error)),
            new MemoryLocalTransferFileStore(),
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task Upload_NetworkTimeout()
    {
        var store = new MemoryTransferStore();
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(new FailingUploadProvider()),
            new MemoryLocalTransferFileStore(),
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Network, result.Error?.Category);
        Assert.True(result.Error?.IsRetryable);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Failed, saved.Status);
        Assert.True(saved.IsRetryable);
        Assert.Equal("upload failed", saved.StatusReason);
        Assert.Equal(StorageErrorCategory.Network, saved.ErrorCategory);
    }

    [Fact]
    public async Task Download_RemoteFileNotFound()
    {
        var store = new MemoryTransferStore();
        var error = new StorageError(StorageErrorCode.NotFound, "remote missing", StorageErrorCategory.NotFound);
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(new FailingDownloadWithErrorProvider(error)),
            new MemoryLocalTransferFileStore(),
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Download, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task Download_LocalWriteFailed()
    {
        var store = new MemoryTransferStore();
        var localFiles = new FailingLocalTransferFileStore(
            new StorageError(StorageErrorCode.InfrastructureUnavailable, "disk full", StorageErrorCategory.Infrastructure));
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FakeProviderFactory(),
            localFiles,
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Download, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Infrastructure, result.Error?.Category);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Failed, saved.Status);
    }

    [Fact]
    public async Task Upload_CancelledDuringExecution()
    {
        var store = new MemoryTransferStore();
        var worker = CreateWorker(store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await worker.ExecuteAsync(task, cts.Token);

        // Worker returns Success from UpdateStatusAsync after marking interrupted
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Interrupted, saved.Status);
        Assert.True(saved.IsRetryable);
        Assert.Contains("中断", saved.StatusReason);
    }

    [Fact]
    public async Task Download_CancelledDuringExecution()
    {
        var store = new MemoryTransferStore();
        var worker = CreateWorker(store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Download, TransferStatus.Pending);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await worker.ExecuteAsync(task, cts.Token);

        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Interrupted, saved.Status);
    }

    [Fact]
    public async Task Upload_AccountLookupFailed()
    {
        var store = new MemoryTransferStore();
        var error = new StorageError(StorageErrorCode.NotFound, "account not found", StorageErrorCategory.NotFound);
        var worker = new TransferWorker(
            new FailingAccountRepository(error),
            new FakeProviderFactory(),
            new MemoryLocalTransferFileStore(),
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Failed, saved.Status);
    }

    [Fact]
    public async Task Upload_ProgressCallback_ReceivesUpdates()
    {
        var store = new MemoryTransferStore();
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FakeProviderFactory(),
            new MemoryLocalTransferFileStore(),
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsSuccess);
        // Worker persist progress updates through ITransferStateStore
        Assert.Contains(store.ProgressUpdates, u => u.TaskId == task.Id);
        var update = store.ProgressUpdates.Last(u => u.TaskId == task.Id);
        Assert.NotNull(update.Progress);
        Assert.True(update.Progress.BytesTransferred > 0);
    }

    [Fact]
    public async Task Download_ProgressCallback_ReceivesUpdates()
    {
        var store = new MemoryTransferStore();
        var progressReports = new List<TransferProgress>();
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FakeProviderFactory(),
            new MemoryLocalTransferFileStore(),
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Download, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsSuccess);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Succeeded, saved.Status);
    }

    [Fact]
    public async Task Worker_ReportsFinalProgress_OnCompletion()
    {
        var store = new MemoryTransferStore();
        var worker = CreateWorker(store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsSuccess);
        var update = store.ProgressUpdates.LastOrDefault(u => u.TaskId == task.Id);
        Assert.NotNull(update.Progress);
        Assert.True(update.Progress.BytesTransferred > 0);
    }

    [Fact]
    public async Task Worker_WritesRunningProgress_DuringExecution()
    {
        var store = new MemoryTransferStore();
        var worker = CreateWorker(store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        await worker.ExecuteAsync(task);

        // Should have at least 2 progress updates: initial (0, null, null) and final
        Assert.Contains(store.ProgressUpdates, u => u.TaskId == task.Id && u.Progress?.BytesTransferred == 0);
        Assert.Contains(store.ProgressUpdates, u => u.TaskId == task.Id && u.Progress?.BytesTransferred > 0);
    }

    [Fact]
    public async Task Worker_PersistsFailureReasonAndRetryability()
    {
        var store = new MemoryTransferStore();
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(new FailingUploadProvider()),
            new MemoryLocalTransferFileStore(),
            store);
        var task = TransferTaskFactory.Create(TransferStatus.Pending);

        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Network, result.Error?.Category);
        var failed = store.Tasks.Single(saved => saved.Id == task.Id);
        Assert.Equal(TransferStatus.Failed, failed.Status);
        Assert.Equal(StorageErrorCategory.Network, failed.ErrorCategory);
        Assert.True(failed.IsRetryable);
        Assert.Equal("upload failed", failed.StatusReason);
    }

    [Fact]
    public async Task Worker_DoesNotLeakProvider_AfterExecution()
    {
        var store = new MemoryTransferStore();
        var disposableProvider = new DisposableTrackerProvider();
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(disposableProvider),
            new MemoryLocalTransferFileStore(),
            store);
        var task = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        await worker.ExecuteAsync(task);

        Assert.True(disposableProvider.WasDisposed);
    }

    [Fact]
    public async Task Worker_NullTask_Throws()
    {
        var store = new MemoryTransferStore();
        var worker = CreateWorker(store);

        await Assert.ThrowsAsync<ArgumentNullException>(() => worker.ExecuteAsync(null!));
    }

    private static TransferWorker CreateWorker(MemoryTransferStore store)
    {
        return new TransferWorker(
            new AnyAccountRepository(),
            new FakeProviderFactory(),
            new MemoryLocalTransferFileStore(),
            store);
    }

    private sealed class FailingUploadWithErrorProvider : IStorageProvider
    {
        private readonly StorageError _error;

        public FailingUploadWithErrorProvider(StorageError error)
        {
            _error = error;
        }

        public StorageCapabilitySet Capabilities =>
            new(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download);

        public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
            RemotePath path, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));

        public Task<OperationResult> DeleteAsync(
            RemotePath path, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public Task<OperationResult> UploadAsync(
            RemotePath path, Stream content, long? contentLength,
            IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure(_error));

        public Task<OperationResult> DownloadAsync(
            RemotePath path, Stream destination,
            IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingDownloadWithErrorProvider : IStorageProvider
    {
        private readonly StorageError _error;

        public FailingDownloadWithErrorProvider(StorageError error)
        {
            _error = error;
        }

        public StorageCapabilitySet Capabilities =>
            new(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download);

        public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
            RemotePath path, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));

        public Task<OperationResult> DeleteAsync(
            RemotePath path, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public Task<OperationResult> UploadAsync(
            RemotePath path, Stream content, long? contentLength,
            IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DownloadAsync(
            RemotePath path, Stream destination,
            IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure(_error));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DisposableTrackerProvider : IStorageProvider
    {
        public bool WasDisposed { get; private set; }

        public StorageCapabilitySet Capabilities =>
            new(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download);

        public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
            RemotePath path, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));

        public Task<OperationResult> DeleteAsync(
            RemotePath path, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public async Task<OperationResult> UploadAsync(
            RemotePath path, Stream content, long? contentLength,
            IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var buffer = new byte[81920];
            while (await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0) { }
            return OperationResult.Success();
        }

        public Task<OperationResult> DownloadAsync(
            RemotePath path, Stream destination,
            IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingLocalTransferFileStore : ILocalTransferFileStore
    {
        private readonly StorageError _error;

        public FailingLocalTransferFileStore(StorageError error)
        {
            _error = error;
        }

        public Task<OperationResult<LocalTransferReadHandle>> OpenReadAsync(
            LocalPath path, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<LocalTransferReadHandle>.Failure(_error));

        public Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
            LocalPath path, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<LocalTransferWriteHandle>.Failure(_error));
    }

    private sealed class RenamingLocalTransferFileStore : ILocalTransferFileStore
    {
        private readonly LocalPath _actualPath;

        public RenamingLocalTransferFileStore(LocalPath actualPath)
        {
            _actualPath = actualPath;
        }

        public Task<OperationResult<LocalTransferReadHandle>> OpenReadAsync(
            LocalPath path, CancellationToken cancellationToken = default)
        {
            Stream stream = new MemoryStream([1, 2, 3, 4]);
            return Task.FromResult(OperationResult<LocalTransferReadHandle>.Success(
                new LocalTransferReadHandle(stream, 4)));
        }

        public Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
            LocalPath path, CancellationToken cancellationToken = default)
        {
            Stream stream = new MemoryStream();
            return Task.FromResult(OperationResult<LocalTransferWriteHandle>.Success(
                new LocalTransferWriteHandle(stream, _actualPath)));
        }
    }
}
