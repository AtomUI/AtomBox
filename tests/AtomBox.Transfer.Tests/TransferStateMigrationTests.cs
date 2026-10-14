using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Transfer.Queue;
using AtomBox.Transfer.Scheduling;
using AtomBox.Transfer.State;
using AtomBox.Transfer.Workers;
using AtomUI.City.State;

namespace AtomBox.Transfer.Tests;

public sealed class TransferStateMigrationTests
{
    [Fact]
    public async Task RefreshPublishesCurrentQueueAndObserveSeesInitialThenCommittedState()
    {
        var store = new MemoryTransferStore();
        var pending = TransferTaskFactory.Create(TransferStatus.Pending);
        var interrupted = TransferTaskFactory.Create(
            TransferStatus.Interrupted,
            "interrupted",
            StorageErrorCategory.Unknown,
            isRetryable: true);
        await store.SaveAsync(pending);
        await store.SaveAsync(interrupted);
        var (state, projector) = CreateState(store);
        var notifications = new List<StateChangedEventArgs<TransferQueueState>>();
        using var subscription = state.Queue.Observe(notifications.Add);

        var result = await projector.RefreshAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(TransferProjectionStatus.Ready, state.Queue.Value.Status);
        Assert.Equal([pending.Id, interrupted.Id], state.Queue.Value.Tasks.Select(item => item.Task.Id));
        Assert.True(notifications[0].IsInitial);
        Assert.Equal(TransferProjectionStatus.Uninitialized, notifications[0].NewValue.Status);
        Assert.False(notifications[1].IsInitial);
        Assert.Equal(TransferProjectionStatus.Ready, notifications[1].NewValue.Status);
    }

    [Fact]
    public void ConcurrentPersistedUpdatesAreMergedAtomically()
    {
        var store = new MemoryTransferStore();
        var (state, projector) = CreateState(store);
        projector.PublishReady([]);
        var tasks = Enumerable.Range(0, 64)
            .Select(_ => TransferTaskFactory.Create(TransferStatus.Pending))
            .ToArray();

        Parallel.ForEach(tasks, task => projector.PublishPersisted(task, progress: null));

        Assert.Equal(64, state.Queue.Value.Tasks.Count);
        Assert.Equal(
            tasks.Select(task => task.Id).OrderBy(id => id.Value),
            state.Queue.Value.Tasks.Select(snapshot => snapshot.Task.Id).OrderBy(id => id.Value));
    }

    [Fact]
    public async Task RefreshCannotOverwriteAPersistedUpdateThatCompletesWhileItReads()
    {
        var store = new BlockingQueueStore();
        var (state, projector) = CreateState(store);
        var task = TransferTaskFactory.Create(TransferStatus.Pending);

        var refresh = projector.RefreshAsync();
        await store.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var publish = Task.Run(() => projector.PublishPersisted(task, progress: null));
        Assert.False(publish.IsCompleted);

        store.ReleaseRead();
        await Task.WhenAll(refresh, publish);

        Assert.Equal(task.Id, Assert.Single(state.Queue.Value.Tasks).Task.Id);
    }

    [Fact]
    public void PersistedProgressIsPreservedWhenAStatusOnlySaveFollows()
    {
        var store = new MemoryTransferStore();
        var (state, projector) = CreateState(store);
        projector.PublishReady([]);
        var running = TransferTaskFactory.Create(TransferStatus.Running);
        var progress = new TransferProgress(25, 100, 50);

        projector.PublishPersisted(running, progress);
        projector.PublishPersisted(
            running.WithStatus(TransferStatus.Interrupted, DateTimeOffset.UtcNow),
            progress: null);

        var snapshot = Assert.Single(state.Queue.Value.Tasks);
        Assert.Equal(TransferStatus.Interrupted, snapshot.Task.Status);
        Assert.Equal(progress, snapshot.Progress);
    }

    [Theory]
    [InlineData(TransferStatus.Succeeded)]
    [InlineData(TransferStatus.Failed)]
    [InlineData(TransferStatus.Canceled)]
    public void TerminalPersistedStateRemovesTaskFromQueue(TransferStatus terminalStatus)
    {
        var store = new MemoryTransferStore();
        var (state, projector) = CreateState(store);
        var running = TransferTaskFactory.Create(TransferStatus.Running);
        projector.PublishReady([new TransferStateSnapshot(running, null)]);

        projector.PublishPersisted(
            running.WithStatus(terminalStatus, DateTimeOffset.UtcNow),
            progress: null);

        Assert.Empty(state.Queue.Value.Tasks);
    }

    [Fact]
    public async Task FailedPersistenceDoesNotPublishUncommittedTask()
    {
        var error = new StorageError(
            StorageErrorCode.InfrastructureUnavailable,
            "save failed",
            StorageErrorCategory.Infrastructure,
            isRetryable: true);
        var store = new FailingTransferStore(error);
        var (state, projector) = CreateState(store);
        projector.PublishReady([]);
        var scheduler = new TransferTaskScheduler(
            store,
            new TransferQueue(),
            () => throw new InvalidOperationException("Worker is not expected."),
            new TransferCancellationRegistry(),
            new FixedSettingsRepository(),
            projector);

        var result = await scheduler.SubmitAsync(TransferTaskFactory.Create(TransferStatus.Pending));

        Assert.True(result.IsFailure);
        Assert.Empty(state.Queue.Value.Tasks);
        Assert.Equal(TransferProjectionStatus.Ready, state.Queue.Value.Status);
    }

    [Fact]
    public async Task InitializerRepairsRunningTasksBeforePublishingQueue()
    {
        var store = new MemoryTransferStore();
        var running = TransferTaskFactory.Create(TransferStatus.Running);
        await store.SaveAsync(running);
        var (state, projector) = CreateState(store);
        var initializer = new TransferRuntimeInitializer(store, projector);

        var result = await initializer.InitializeAsync();

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(state.Queue.Value.Tasks);
        Assert.Equal(running.Id, snapshot.Task.Id);
        Assert.Equal(TransferStatus.Interrupted, snapshot.Task.Status);
        Assert.Equal(TransferProjectionStatus.Ready, state.Queue.Value.Status);
    }

    private static (AtomBoxTransferState State, TransferStateProjector Projector) CreateState(
        ITransferStateStore store)
    {
        var registry = new ApplicationStateRegistry();
        var state = new AtomBoxTransferState(registry, registry, registry);
        return (state, new TransferStateProjector(state, store));
    }

    private sealed class FixedSettingsRepository : IApplicationSettingsRepository
    {
        public Task<OperationResult<ApplicationSettings>> GetAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<ApplicationSettings>.Success(
                new ApplicationSettings(1, TransferOverwritePolicy.Ask, true)));
        }

        public Task<OperationResult> SaveAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FailingTransferStore : ITransferTaskStore, ITransferStateStore
    {
        private readonly StorageError _error;

        public FailingTransferStore(StorageError error) => _error = error;

        public Task<OperationResult<TransferTask>> GetByIdAsync(
            AtomBox.Core.ValueObjects.TransferTaskId taskId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<TransferTask>.Failure(_error));

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<TransferTask>>.Failure(_error));

        public Task<OperationResult> SaveAsync(
            TransferTask task,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure(_error));

        public Task<OperationResult> DeleteAsync(
            AtomBox.Core.ValueObjects.TransferTaskId taskId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure(_error));

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<TransferStateSnapshot>>.Failure(_error));

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
            int skip,
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<TransferStateSnapshot>>.Failure(_error));

        public Task<OperationResult> UpdateStatusAsync(
            TransferTask task,
            TransferProgress? progress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure(_error));
    }

    private sealed class BlockingQueueStore : ITransferStateStore
    {
        private readonly TaskCompletionSource _releaseRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseRead() => _releaseRead.TrySetResult();

        public async Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await _releaseRead.Task.WaitAsync(cancellationToken);
            return OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success([]);
        }

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
            int skip,
            int take,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperationResult> UpdateStatusAsync(
            TransferTask task,
            TransferProgress? progress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
