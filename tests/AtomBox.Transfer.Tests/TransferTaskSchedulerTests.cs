using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Transfer.Queue;
using AtomBox.Transfer.Scheduling;
using AtomBox.Transfer.Workers;

namespace AtomBox.Transfer.Tests;

public sealed class TransferTaskSchedulerTests
{
    [Fact]
    public async Task SubmitAsync_SavesPendingTaskWithoutExecutingIt()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var task = CreateTask(TransferStatus.Pending);

        var result = await scheduler.SubmitAsync(task);

        Assert.True(result.IsSuccess);
        Assert.Equal(TransferStatus.Pending, store.Tasks.Single().Status);
    }

    [Fact]
    public async Task WakeAsync_ExecutesPendingTasksAndMovesThemToHistoryState()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var pending = CreateTask(TransferStatus.Pending);
        var canceled = CreateTask(TransferStatus.Canceled);
        await scheduler.SubmitAsync(pending);
        await scheduler.SubmitAsync(canceled);

        var result = await scheduler.WakeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(TransferStatus.Succeeded, store.Tasks.Single(task => task.Id == pending.Id).Status);
        Assert.Equal(TransferStatus.Canceled, store.Tasks.Single(task => task.Id == canceled.Id).Status);
        Assert.Contains(store.ProgressUpdates, update => update.TaskId == pending.Id && update.Progress?.Percent == 100);
    }

    [Fact]
    public async Task WakeAsync_CreatesWorkerForEachPendingTask()
    {
        var store = new MemoryTransferStore();
        var workerCreations = 0;
        var scheduler = CreateScheduler(store, () =>
        {
            workerCreations++;
            return CreateWorker(store);
        });

        await scheduler.SubmitAsync(CreateTask(TransferStatus.Pending));
        await scheduler.SubmitAsync(CreateTask(TransferStatus.Pending));

        var result = await scheduler.WakeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, workerCreations);
    }

    [Fact]
    public async Task WakeAsync_RunsPendingTasksUpToConfiguredConcurrency()
    {
        var store = new MemoryTransferStore();
        var gate = new ConcurrentProviderGate(expectedConcurrentStarts: 2);
        var providerFactory = new TrackingProviderFactory(gate);
        var scheduler = CreateScheduler(
            store,
            () => new TransferWorker(
                new AnyAccountRepository(),
                providerFactory,
                new MemoryLocalTransferFileStore(),
                store),
            defaultConcurrency: 2);

        await scheduler.SubmitAsync(CreateTask(TransferStatus.Pending));
        await scheduler.SubmitAsync(CreateTask(TransferStatus.Pending));
        await scheduler.SubmitAsync(CreateTask(TransferStatus.Pending));
        await scheduler.SubmitAsync(CreateTask(TransferStatus.Pending));

        var wakeTask = Task.Run(() => scheduler.WakeAsync());
        await gate.WaitForExpectedConcurrentStartsAsync();

        Assert.Equal(2, gate.MaxActiveUploads);
        Assert.Equal(2, store.Tasks.Count(task => task.Status == TransferStatus.Running));

        gate.ReleaseAll();
        var result = await wakeTask;

        Assert.True(result.IsSuccess);
        Assert.Equal(4, store.Tasks.Count(task => task.Status == TransferStatus.Succeeded));
        Assert.Equal(2, gate.MaxActiveUploads);
    }

    [Fact]
    public async Task WakeAsync_TaskFailureDoesNotPreventOtherPendingTasks()
    {
        var store = new MemoryTransferStore();
        var providerFactory = new FirstUploadFailsProviderFactory();
        var scheduler = CreateScheduler(
            store,
            () => new TransferWorker(
                new AnyAccountRepository(),
                providerFactory,
                new MemoryLocalTransferFileStore(),
                store),
            defaultConcurrency: 3);

        await scheduler.SubmitAsync(CreateTask(TransferStatus.Pending));
        await scheduler.SubmitAsync(CreateTask(TransferStatus.Pending));
        await scheduler.SubmitAsync(CreateTask(TransferStatus.Pending));

        var result = await scheduler.WakeAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Network, result.Error?.Category);
        Assert.Single(store.Tasks, task => task.Status == TransferStatus.Failed);
        Assert.Equal(2, store.Tasks.Count(task => task.Status == TransferStatus.Succeeded));
    }

    [Fact]
    public async Task CancelAsync_OnlyCancelsCancelableTasks()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var pending = CreateTask(TransferStatus.Pending);
        var succeeded = CreateTask(TransferStatus.Succeeded);
        await scheduler.SubmitAsync(pending);
        await scheduler.SubmitAsync(succeeded);

        var cancelPending = await scheduler.CancelAsync(pending.Id);
        var cancelSucceeded = await scheduler.CancelAsync(succeeded.Id);

        Assert.True(cancelPending.IsSuccess);
        Assert.True(cancelSucceeded.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, cancelSucceeded.Error?.Category);
        var canceled = store.Tasks.Single(task => task.Id == pending.Id);
        Assert.Equal(TransferStatus.Canceled, canceled.Status);
        Assert.Equal("用户取消了传输任务。", canceled.StatusReason);
    }

    [Fact]
    public async Task RetryAsync_MovesFailedOrInterruptedTasksBackToPending()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var failed = CreateTask(
            TransferStatus.Failed,
            "network unavailable",
            StorageErrorCategory.Network,
            isRetryable: true);
        await scheduler.SubmitAsync(failed);

        var result = await scheduler.RetryAsync(failed.Id);

        Assert.True(result.IsSuccess);
        var pending = store.Tasks.Single();
        Assert.Equal(TransferStatus.Pending, pending.Status);
        Assert.Null(pending.StatusReason);
        Assert.Null(pending.ErrorCategory);
        Assert.False(pending.IsRetryable);
    }

    [Fact]
    public async Task RuntimeInitializer_MarksRunningTasksAsInterrupted()
    {
        var store = new MemoryTransferStore();
        var running = CreateTask(TransferStatus.Running);
        var pending = CreateTask(TransferStatus.Pending);
        await store.SaveAsync(running);
        await store.SaveAsync(pending);
        var initializer = new TransferRuntimeInitializer(store);

        var result = await initializer.InitializeAsync();

        Assert.True(result.IsSuccess);
        var interrupted = store.Tasks.Single(task => task.Id == running.Id);
        Assert.Equal(TransferStatus.Interrupted, interrupted.Status);
        Assert.Equal(StorageErrorCategory.Unknown, interrupted.ErrorCategory);
        Assert.True(interrupted.IsRetryable);
        Assert.Contains("上次退出", interrupted.StatusReason);
        Assert.Equal(TransferStatus.Pending, store.Tasks.Single(task => task.Id == pending.Id).Status);
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
        var task = CreateTask(TransferStatus.Pending);

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
    public async Task CancelAsync_RunningTask_StatusCanceled()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var running = CreateTask(TransferStatus.Running);
        await scheduler.SubmitAsync(running);

        var result = await scheduler.CancelAsync(running.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(TransferStatus.Canceled, store.Tasks.Single().Status);
    }

    [Fact]
    public async Task CancelAsync_ActiveRunningTask_CancelsWorkerAndKeepsCanceled()
    {
        var store = new MemoryTransferStore();
        var cancellations = new TransferCancellationRegistry();
        var provider = new BlockingUploadProvider();
        var scheduler = new TransferTaskScheduler(
            store,
            new TransferQueue(),
            () => new TransferWorker(
                new AnyAccountRepository(),
                new FixedProviderFactory(provider),
                new MemoryLocalTransferFileStore(),
                store,
                cancellations),
            cancellations,
            new FixedApplicationSettingsRepository(3));
        var pending = CreateTask(TransferStatus.Pending);
        await scheduler.SubmitAsync(pending);

        var wakeTask = Task.Run(() => scheduler.WakeAsync());
        await provider.WaitUntilStartedAsync();

        var cancelResult = await scheduler.CancelAsync(pending.Id);
        var wakeResult = await wakeTask;

        Assert.True(cancelResult.IsSuccess);
        Assert.True(wakeResult.IsSuccess);
        Assert.True(provider.WasCanceled);
        var canceled = store.Tasks.Single(task => task.Id == pending.Id);
        Assert.Equal(TransferStatus.Canceled, canceled.Status);
        Assert.Equal("用户取消了传输任务。", canceled.StatusReason);
    }

    [Fact]
    public async Task CancelAsync_FailedTask_ReturnsConflict()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var failed = CreateTask(TransferStatus.Failed);
        await scheduler.SubmitAsync(failed);

        var result = await scheduler.CancelAsync(failed.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task CancelAsync_InterruptedTask_ReturnsConflict()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var interrupted = CreateTask(TransferStatus.Interrupted);
        await scheduler.SubmitAsync(interrupted);

        var result = await scheduler.CancelAsync(interrupted.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task CancelAsync_CanceledTask_ReturnsConflict()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var canceled = CreateTask(TransferStatus.Canceled);
        await scheduler.SubmitAsync(canceled);

        var result = await scheduler.CancelAsync(canceled.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task CancelAsync_NonExistentTask_ReturnsNotFound()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);

        var result = await scheduler.CancelAsync(TransferTaskId.New());

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task CancelAsync_PausedTask_ReturnsConflict()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var paused = CreateTask(TransferStatus.Paused);
        await scheduler.SubmitAsync(paused);

        var result = await scheduler.CancelAsync(paused.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task RetryAsync_InterruptedTask_BackToPending()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var interrupted = CreateTask(
            TransferStatus.Interrupted,
            "crash",
            StorageErrorCategory.Unknown,
            isRetryable: true);
        await scheduler.SubmitAsync(interrupted);

        var result = await scheduler.RetryAsync(interrupted.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(TransferStatus.Pending, store.Tasks.Single().Status);
    }

    [Fact]
    public async Task RetryAsync_SucceededTask_ReturnsConflict()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var succeeded = CreateTask(TransferStatus.Succeeded);
        await scheduler.SubmitAsync(succeeded);

        var result = await scheduler.RetryAsync(succeeded.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task RetryAsync_PendingTask_ReturnsConflict()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var pending = CreateTask(TransferStatus.Pending);
        await scheduler.SubmitAsync(pending);

        var result = await scheduler.RetryAsync(pending.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task RetryAsync_RunningTask_ReturnsConflict()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var running = CreateTask(TransferStatus.Running);
        await scheduler.SubmitAsync(running);

        var result = await scheduler.RetryAsync(running.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task RetryAsync_CanceledTask_ReturnsConflict()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var canceled = CreateTask(TransferStatus.Canceled);
        await scheduler.SubmitAsync(canceled);

        var result = await scheduler.RetryAsync(canceled.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task RetryAsync_NonExistentTask_ReturnsNotFound()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);

        var result = await scheduler.RetryAsync(TransferTaskId.New());

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task WakeAsync_NoPendingTasks_ReturnsSuccess()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        await store.SaveAsync(CreateTask(TransferStatus.Succeeded));
        await store.SaveAsync(CreateTask(TransferStatus.Running));

        var result = await scheduler.WakeAsync();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task WakeAsync_MixedStatuses_OnlyPendingProcessed()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var allTasks = new[]
        {
            CreateTask(TransferStatus.Pending),
            CreateTask(TransferStatus.Running),
            CreateTask(TransferStatus.Succeeded),
            CreateTask(TransferStatus.Pending),
        };

        // Save all tasks directly, preserving their original statuses
        foreach (var t in allTasks)
        {
            await store.SaveAsync(t);
        }

        var result = await scheduler.WakeAsync();

        Assert.True(result.IsSuccess);
        // The two Pending tasks should now be Succeeded (plus the pre-existing Succeeded)
        Assert.Equal(3, store.Tasks.Count(t => t.Status == TransferStatus.Succeeded));
        // Running task should remain unchanged
        Assert.Single(store.Tasks, t => t.Id == allTasks[1].Id && t.Status == TransferStatus.Running);
    }

    [Fact]
    public async Task WakeAsync_PausedAndInterruptedNotProcessed()
    {
        var store = new MemoryTransferStore();
        var workerCreations = 0;
        var scheduler = CreateScheduler(store, () =>
        {
            workerCreations++;
            return CreateWorker(store);
        });
        await scheduler.SubmitAsync(CreateTask(TransferStatus.Paused));
        await scheduler.SubmitAsync(CreateTask(TransferStatus.Interrupted));

        var result = await scheduler.WakeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, workerCreations);
    }

    [Fact]
    public async Task CancelAsync_SucceededTask_StatusReasonSet()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var succeeded = CreateTask(TransferStatus.Succeeded);
        await scheduler.SubmitAsync(succeeded);

        var result = await scheduler.CancelAsync(succeeded.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task CancelAsync_CanceledTask_StatusReasonSet()
    {
        var store = new MemoryTransferStore();
        var scheduler = CreateScheduler(store);
        var canceled = CreateTask(TransferStatus.Canceled, "already canceled");
        await scheduler.SubmitAsync(canceled);

        var result = await scheduler.CancelAsync(canceled.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    private static TransferTaskScheduler CreateScheduler(MemoryTransferStore store)
    {
        return CreateScheduler(store, () => CreateWorker(store));
    }

    private static TransferTaskScheduler CreateScheduler(
        MemoryTransferStore store,
        Func<TransferWorker> workerFactory,
        int defaultConcurrency = 3)
    {
        return new TransferTaskScheduler(
            store,
            new TransferQueue(),
            workerFactory,
            new TransferCancellationRegistry(),
            new FixedApplicationSettingsRepository(defaultConcurrency));
    }

    private static TransferWorker CreateWorker(MemoryTransferStore store)
    {
        return new TransferWorker(
            new AnyAccountRepository(),
            new FakeProviderFactory(),
            new MemoryLocalTransferFileStore(),
            store);
    }

    private static TransferTask CreateTask(
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

    private sealed class MemoryTransferStore : ITransferTaskStore, ITransferStateStore
    {
        private readonly List<TransferTask> _tasks = [];
        private readonly object _syncRoot = new();

        public IReadOnlyList<TransferTask> Tasks => _tasks;

        public List<(TransferTaskId TaskId, TransferProgress? Progress)> ProgressUpdates { get; } = [];

        public Task<OperationResult<TransferTask>> GetByIdAsync(
            TransferTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                var task = _tasks.FirstOrDefault(item => item.Id == taskId);
                return Task.FromResult(task is null
                    ? OperationResult<TransferTask>.Failure(StorageError.NotFound("not found"))
                    : OperationResult<TransferTask>.Success(task));
            }
        }

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                return Task.FromResult(OperationResult<IReadOnlyList<TransferTask>>.Success(_tasks.ToArray()));
            }
        }

        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
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
            }

            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                _tasks.RemoveAll(task => task.Id == taskId);
            }

            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                var queue = _tasks
                    .Where(task => task.Status is TransferStatus.Pending or TransferStatus.Running or TransferStatus.Paused or TransferStatus.Interrupted)
                    .Select(task => new TransferStateSnapshot(task, null))
                    .ToArray();
                return Task.FromResult(OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(queue));
            }
        }

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                var history = _tasks
                    .Where(task => task.Status is TransferStatus.Succeeded or TransferStatus.Failed or TransferStatus.Canceled or TransferStatus.Interrupted)
                    .Skip(skip)
                    .Take(take)
                    .Select(task => new TransferStateSnapshot(task, null))
                    .ToArray();
                return Task.FromResult(OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(history));
            }
        }

        public async Task<OperationResult> UpdateStatusAsync(
            TransferTask task,
            TransferProgress? progress,
            CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                ProgressUpdates.Add((task.Id, progress));
            }

            return await SaveAsync(task, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class FixedApplicationSettingsRepository : IApplicationSettingsRepository
    {
        private readonly ApplicationSettings _settings;

        public FixedApplicationSettingsRepository(int defaultConcurrency)
        {
            _settings = new ApplicationSettings(defaultConcurrency, TransferOverwritePolicy.Ask, true);
        }

        public Task<OperationResult<ApplicationSettings>> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<ApplicationSettings>.Success(_settings));
        }

        public Task<OperationResult> SaveAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class AnyAccountRepository : IStorageAccountRepository
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

    private sealed class FakeProviderFactory : IStorageProviderFactory
    {
        public Task<OperationResult<IStorageProvider>> CreateAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Success(new FakeProvider()));
        }
    }

    private sealed class FixedProviderFactory : IStorageProviderFactory
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

    private sealed class TrackingProviderFactory : IStorageProviderFactory
    {
        private readonly ConcurrentProviderGate _gate;

        public TrackingProviderFactory(ConcurrentProviderGate gate)
        {
            _gate = gate;
        }

        public Task<OperationResult<IStorageProvider>> CreateAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Success(new TrackingUploadProvider(_gate)));
        }
    }

    private sealed class FirstUploadFailsProviderFactory : IStorageProviderFactory
    {
        private int _createdCount;

        public Task<OperationResult<IStorageProvider>> CreateAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default)
        {
            var created = Interlocked.Increment(ref _createdCount);
            IStorageProvider provider = created == 1
                ? new FailingUploadProvider()
                : new FakeProvider();
            return Task.FromResult(OperationResult<IStorageProvider>.Success(provider));
        }
    }

    private sealed class FakeProvider : IStorageProvider
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

    private sealed class FailingUploadProvider : IStorageProvider
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

    private sealed class ConcurrentProviderGate
    {
        private readonly int _expectedConcurrentStarts;
        private readonly TaskCompletionSource _expectedConcurrentStartsReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeUploads;
        private int _maxActiveUploads;

        public ConcurrentProviderGate(int expectedConcurrentStarts)
        {
            _expectedConcurrentStarts = expectedConcurrentStarts;
        }

        public int MaxActiveUploads => Volatile.Read(ref _maxActiveUploads);

        public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeUploads);
            UpdateMaxActive(active);
            if (active >= _expectedConcurrentStarts)
            {
                _expectedConcurrentStartsReached.TrySetResult();
            }

            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Lease(this);
        }

        public Task WaitForExpectedConcurrentStartsAsync()
        {
            return _expectedConcurrentStartsReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void ReleaseAll()
        {
            _release.TrySetResult();
        }

        private void Exit()
        {
            Interlocked.Decrement(ref _activeUploads);
        }

        private void UpdateMaxActive(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxActiveUploads);
                if (active <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxActiveUploads, active, current) == current)
                {
                    return;
                }
            }
        }

        private sealed class Lease : IDisposable
        {
            private readonly ConcurrentProviderGate _gate;

            public Lease(ConcurrentProviderGate gate)
            {
                _gate = gate;
            }

            public void Dispose()
            {
                _gate.Exit();
            }
        }
    }

    private sealed class TrackingUploadProvider : IStorageProvider
    {
        private readonly ConcurrentProviderGate _gate;

        public TrackingUploadProvider(ConcurrentProviderGate gate)
        {
            _gate = gate;
        }

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
            using var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
            progress?.Report(new TransferProgress(contentLength ?? 0, contentLength, null));
            return OperationResult.Success();
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

    private sealed class BlockingUploadProvider : IStorageProvider
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCanceled { get; private set; }

        public StorageCapabilitySet Capabilities { get; } = new(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download);

        public Task WaitUntilStartedAsync()
        {
            return _started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

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
            _started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                WasCanceled = true;
                throw;
            }

            return OperationResult.Success();
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

    private sealed class MemoryLocalTransferFileStore : ILocalTransferFileStore
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
}
