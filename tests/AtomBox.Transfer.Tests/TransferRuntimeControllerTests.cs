using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Transfer.Scheduling;
using AtomBox.Transfer.State;
using AtomBox.Transfer.Workers;
using AtomUI.City.State;

namespace AtomBox.Transfer.Tests;

public sealed class TransferRuntimeControllerTests
{
    [Fact]
    public async Task StartAndStop_AreIdempotent_AndStoppedRuntimeCannotRestart()
    {
        var context = CreateContext(new FakeProviderFactory());

        await Task.WhenAll(
            context.Runtime.StartAsync(CancellationToken.None),
            context.Runtime.StartAsync(CancellationToken.None));

        Assert.Equal(TransferRuntimeLifecycleState.Running, context.Runtime.Current.Lifecycle);
        Assert.Equal(TransferRuntimeHealthState.Healthy, context.Runtime.Current.Health);
        Assert.True(context.Runtime.Current.IsAcceptingSubmissions);

        await Task.WhenAll(
            context.Runtime.StopAsync(),
            context.Runtime.StopAsync());

        Assert.Equal(TransferRuntimeLifecycleState.Stopped, context.Runtime.Current.Lifecycle);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Runtime.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartupStoreFailure_PreservesStructuredErrorAndClosesAdmission()
    {
        var context = CreateContext(new FakeProviderFactory());
        var failure = InfrastructureFailure();
        context.Store.ListFailure = failure;

        var exception = await Assert.ThrowsAsync<TransferRuntimeStartupException>(() =>
            context.Runtime.StartAsync(CancellationToken.None));

        Assert.Same(failure, exception.Error);
        Assert.Equal(TransferRuntimeLifecycleState.Faulted, context.Runtime.Current.Lifecycle);
        Assert.Equal(TransferRuntimeHealthState.Faulted, context.Runtime.Current.Health);
        Assert.False(context.Runtime.Current.IsAcceptingSubmissions);
        await context.Runtime.StopAsync();
    }

    [Fact]
    public async Task Submit_PersistsPendingThenRuntimeCompletesTask()
    {
        var context = CreateContext(new FakeProviderFactory());
        await context.Runtime.StartAsync(CancellationToken.None);
        var task = TransferTaskFactory.Create(TransferStatus.Pending);

        var submit = await context.Runtime.SubmitAsync(task);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var completed = await context.Store.WaitForStatusAsync(
            task.Id,
            TransferStatus.Succeeded,
            timeout.Token);

        Assert.True(submit.IsSuccess);
        Assert.True(completed.Revision >= 2);
        Assert.Equal(TransferRuntimeHealthState.Healthy, context.Runtime.Current.Health);
        await context.Runtime.StopAsync();
    }

    [Fact]
    public async Task PersistedRunningProgress_IsPublishedToLiveState()
    {
        var provider = new ProgressBlockingProvider();
        var context = CreateContext(new FixedProviderFactory(provider));
        await context.Runtime.StartAsync(CancellationToken.None);
        var task = TransferTaskFactory.Create(TransferStatus.Pending);
        await context.Runtime.SubmitAsync(task);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await provider.ProgressReported.Task.WaitAsync(timeout.Token);
        var snapshot = await WaitForQueueTaskAsync(
            context.State,
            task.Id,
            item => item.Progress?.SpeedBytesPerSecond == ProgressBlockingProvider.ReportedSpeed,
            timeout.Token);

        Assert.Equal(TransferStatus.Running, snapshot.Task.Status);
        Assert.Equal(ProgressBlockingProvider.ReportedBytes, snapshot.Progress?.BytesTransferred);
        Assert.Equal(ProgressBlockingProvider.ReportedSpeed, snapshot.Progress?.SpeedBytesPerSecond);

        provider.Release.TrySetResult(true);
        await context.Store.WaitForStatusAsync(task.Id, TransferStatus.Succeeded, timeout.Token);
        await context.Runtime.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task OneTaskFailure_DoesNotFaultRuntimeOrBlockNextTask()
    {
        var factory = new SequencedProviderFactory(
            () => new FailingUploadProvider(),
            () => new FakeProvider());
        var context = CreateContext(factory, concurrency: 1);
        await context.Runtime.StartAsync(CancellationToken.None);
        var failedTask = TransferTaskFactory.Create(TransferStatus.Pending);
        var successfulTask = TransferTaskFactory.Create(TransferStatus.Pending);

        await context.Runtime.SubmitAsync(failedTask);
        await context.Runtime.SubmitAsync(successfulTask);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await context.Store.WaitForStatusAsync(failedTask.Id, TransferStatus.Failed, timeout.Token);
        await context.Store.WaitForStatusAsync(successfulTask.Id, TransferStatus.Succeeded, timeout.Token);

        Assert.Equal(TransferRuntimeLifecycleState.Running, context.Runtime.Current.Lifecycle);
        Assert.Equal(TransferRuntimeHealthState.Healthy, context.Runtime.Current.Health);
        await context.Runtime.StopAsync();
    }

    [Fact]
    public async Task CancelRunningTask_UsesCancelingUntilProviderExits()
    {
        var provider = new CooperativeBlockingProvider();
        var context = CreateContext(new FixedProviderFactory(provider));
        await context.Runtime.StartAsync(CancellationToken.None);
        var task = TransferTaskFactory.Create(TransferStatus.Pending);
        await context.Runtime.SubmitAsync(task);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await provider.Started.Task.WaitAsync(timeout.Token);

        var cancel = await context.Runtime.CancelAsync(task.Id, timeout.Token);
        var canceled = await context.Store.WaitForStatusAsync(
            task.Id,
            TransferStatus.Canceled,
            timeout.Token);

        Assert.True(cancel.IsSuccess);
        Assert.Equal(TransferStatus.Canceled, canceled.Status);
        Assert.Contains(
            context.Store.Tasks,
            saved => saved.Id == task.Id && saved.Revision >= 3);
        await context.Runtime.StopAsync();
    }

    [Fact]
    public async Task Stop_DoesNotWaitForeverForNonCooperativeProvider_AndLateSuccessIsFenced()
    {
        var provider = new NonCooperativeBlockingProvider();
        var context = CreateContext(
            new FixedProviderFactory(provider),
            shutdownGracePeriod: TimeSpan.FromMilliseconds(50));
        await context.Runtime.StartAsync(CancellationToken.None);
        var task = TransferTaskFactory.Create(TransferStatus.Pending);
        await context.Runtime.SubmitAsync(task);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await provider.Started.Task.WaitAsync(timeout.Token);

        await context.Runtime.StopAsync(timeout.Token);
        var interrupted = (await context.Store.GetByIdAsync(task.Id)).GetValueOrThrow();
        Assert.Equal(TransferStatus.Interrupted, interrupted.Status);

        provider.Release.TrySetResult(true);
        await provider.Completed.Task.WaitAsync(timeout.Token);
        var afterLateSuccess = (await context.Store.GetByIdAsync(task.Id)).GetValueOrThrow();
        Assert.Equal(TransferStatus.Interrupted, afterLateSuccess.Status);
        Assert.Equal(interrupted.Revision, afterLateSuccess.Revision);
    }

    [Fact]
    public async Task TransientDispatchFailure_DegradesThenRecoversWithoutLosingQueue()
    {
        var context = CreateContext(new FakeProviderFactory());
        var pending = TransferTaskFactory.Create(TransferStatus.Pending);
        await context.Store.SaveAsync(pending);
        await context.Runtime.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await context.Store.WaitForStatusAsync(pending.Id, TransferStatus.Succeeded, timeout.Token);

        context.Store.ListFailure = InfrastructureFailure();
        await context.Runtime.WakeAsync(timeout.Token);
        await WaitForRuntimeAsync(
            context.State,
            snapshot => snapshot.Health == TransferRuntimeHealthState.Degraded,
            timeout.Token);

        context.Store.ListFailure = null;
        await context.Runtime.WakeAsync(timeout.Token);
        var recovered = await WaitForRuntimeAsync(
            context.State,
            snapshot => snapshot.Health == TransferRuntimeHealthState.Healthy,
            timeout.Token);

        Assert.True(recovered.IsAcceptingSubmissions);
        await context.Runtime.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task RepeatedDispatchFailure_FaultsRuntimeAndClosesAdmission()
    {
        var context = CreateContext(new FakeProviderFactory());
        await context.Runtime.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        context.Store.ListFailure = InfrastructureFailure();
        await context.Runtime.WakeAsync(timeout.Token);

        var faulted = await WaitForRuntimeAsync(
            context.State,
            snapshot => snapshot.Lifecycle == TransferRuntimeLifecycleState.Faulted,
            timeout.Token);

        Assert.Equal(TransferRuntimeHealthState.Faulted, faulted.Health);
        Assert.False(faulted.IsAcceptingSubmissions);
        await context.Runtime.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task AbandonInterruptedTask_MovesTaskFromQueueToHistory()
    {
        var providerFactory = new FixedProviderFactory(new FakeProvider());
        var context = CreateContext(providerFactory);
        var interrupted = TransferTaskFactory.Create(
            TransferStatus.Interrupted,
            "The previous execution ended without a confirmed remote result.",
            StorageErrorCategory.Unknown,
            isRetryable: true);
        await context.Store.SaveAsync(interrupted);
        await context.Runtime.StartAsync(CancellationToken.None);

        Assert.Contains(context.State.Queue.Value.Tasks, snapshot => snapshot.Task.Id == interrupted.Id);

        var result = await context.Runtime.AbandonAsync(interrupted.Id);
        var persisted = (await context.Store.GetByIdAsync(interrupted.Id)).GetValueOrThrow();
        var history = (await context.Store.ListHistoryAsync(0, 10)).GetValueOrThrow();

        Assert.True(result.IsSuccess);
        Assert.Equal(TransferStatus.Abandoned, persisted.Status);
        Assert.False(persisted.IsRetryable);
        Assert.DoesNotContain(context.State.Queue.Value.Tasks, snapshot => snapshot.Task.Id == interrupted.Id);
        Assert.Contains(history, snapshot => snapshot.Task.Id == interrupted.Id);
        Assert.Equal(0, providerFactory.CreateCalls);
        await context.Runtime.StopAsync();
    }

    [Fact]
    public async Task RetryAndAbandonInterruptedTask_AreMutuallyExclusive()
    {
        var context = CreateContext(new FakeProviderFactory());
        var interrupted = TransferTaskFactory.Create(
            TransferStatus.Interrupted,
            "The previous execution ended without a confirmed remote result.",
            StorageErrorCategory.Unknown,
            isRetryable: true);
        await context.Store.SaveAsync(interrupted);

        var results = await Task.WhenAll(
            context.Runtime.RetryAsync(interrupted.Id),
            context.Runtime.AbandonAsync(interrupted.Id));
        var persisted = (await context.Store.GetByIdAsync(interrupted.Id)).GetValueOrThrow();

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.IsFailure);
        Assert.Contains(persisted.Status, new[] { TransferStatus.Pending, TransferStatus.Abandoned });
    }

    [Fact]
    public async Task AbandonNonInterruptedTask_ReturnsConflictWithoutChangingTask()
    {
        var context = CreateContext(new FakeProviderFactory());
        var failed = TransferTaskFactory.Create(
            TransferStatus.Failed,
            "failed",
            StorageErrorCategory.Unknown,
            isRetryable: true);
        await context.Store.SaveAsync(failed);

        var result = await context.Runtime.AbandonAsync(failed.Id);
        var persisted = (await context.Store.GetByIdAsync(failed.Id)).GetValueOrThrow();

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
        Assert.Equal(TransferStatus.Failed, persisted.Status);
        Assert.Equal(failed.Revision, persisted.Revision);
    }

    [Fact]
    public async Task QueueRefreshFailure_PreservesLastSuccessfulSnapshotAsStale()
    {
        var store = new MemoryTransferStore();
        var pending = TransferTaskFactory.Create(TransferStatus.Pending);
        await store.SaveAsync(pending);
        var registry = new ApplicationStateRegistry();
        var state = new AtomBoxTransferState(registry, registry, registry);
        var projector = new TransferStateProjector(state, store);
        Assert.True((await projector.RefreshAsync()).IsSuccess);

        store.QueueFailure = InfrastructureFailure();
        var failed = await projector.RefreshAsync();

        Assert.True(failed.IsFailure);
        Assert.True(state.Queue.Value.IsStale);
        Assert.Equal(pending.Id, Assert.Single(state.Queue.Value.Tasks).Task.Id);
        Assert.NotNull(state.Queue.Value.LastSuccessfulRefreshAt);
    }

    private static RuntimeContext CreateContext(
        IStorageProviderFactory providerFactory,
        int concurrency = 2,
        TimeSpan? shutdownGracePeriod = null)
    {
        var store = new MemoryTransferStore();
        var registry = new ApplicationStateRegistry();
        var state = new AtomBoxTransferState(registry, registry, registry);
        var projector = new TransferStateProjector(state, store);
        var settings = new FixedSettingsRepository(concurrency);
        Func<TransferOperationExecutor> executorFactory = () => new TransferOperationExecutor(
            new AnyAccountRepository(),
            providerFactory,
            new MemoryLocalTransferFileStore());
        var runtime = new TransferRuntimeController(
            store,
            store,
            settings,
            executorFactory,
            projector,
            state,
            options: new TransferRuntimeOptions
            {
                ProgressFlushInterval = TimeSpan.FromMilliseconds(10),
                InfrastructureRetryDelay = TimeSpan.FromMilliseconds(10),
                MaxConsecutiveInfrastructureFailures = 2,
                ShutdownGracePeriod = shutdownGracePeriod ?? TimeSpan.FromMilliseconds(250),
            });
        return new RuntimeContext(runtime, store, state);
    }

    private static StorageError InfrastructureFailure()
    {
        return new StorageError(
            StorageErrorCode.InfrastructureUnavailable,
            "temporary store failure",
            StorageErrorCategory.Infrastructure,
            isRetryable: true);
    }

    private static async Task<TransferRuntimeSnapshot> WaitForRuntimeAsync(
        AtomBoxTransferState state,
        Func<TransferRuntimeSnapshot, bool> predicate,
        CancellationToken cancellationToken)
    {
        if (predicate(state.Runtime.Value))
        {
            return state.Runtime.Value;
        }

        var completion = new TaskCompletionSource<TransferRuntimeSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = state.Runtime.Observe(change =>
        {
            if (predicate(change.NewValue))
            {
                completion.TrySetResult(change.NewValue);
            }
        });
        return await completion.Task.WaitAsync(cancellationToken);
    }

    private static async Task<TransferStateSnapshot> WaitForQueueTaskAsync(
        AtomBoxTransferState state,
        TransferTaskId taskId,
        Func<TransferStateSnapshot, bool> predicate,
        CancellationToken cancellationToken)
    {
        var existing = state.Queue.Value.Tasks.FirstOrDefault(item => item.Task.Id == taskId);
        if (existing is not null && predicate(existing))
        {
            return existing;
        }

        var completion = new TaskCompletionSource<TransferStateSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = state.Queue.Observe(change =>
        {
            var candidate = change.NewValue.Tasks.FirstOrDefault(item => item.Task.Id == taskId);
            if (candidate is not null && predicate(candidate))
            {
                completion.TrySetResult(candidate);
            }
        });
        return await completion.Task.WaitAsync(cancellationToken);
    }

    private sealed record RuntimeContext(
        TransferRuntimeController Runtime,
        MemoryTransferStore Store,
        AtomBoxTransferState State);

    private sealed class FixedSettingsRepository : IApplicationSettingsRepository
    {
        private readonly ApplicationSettings _settings;

        public FixedSettingsRepository(int concurrency)
        {
            _settings = new ApplicationSettings(concurrency, TransferOverwritePolicy.Ask, true);
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

    private sealed class SequencedProviderFactory : IStorageProviderFactory
    {
        private readonly Queue<Func<IStorageProvider>> _providers;
        private readonly object _gate = new();

        public SequencedProviderFactory(params Func<IStorageProvider>[] providers)
        {
            _providers = new Queue<Func<IStorageProvider>>(providers);
        }

        public Task<OperationResult<IStorageProvider>> CreateAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(OperationResult<IStorageProvider>.Success(_providers.Dequeue()()));
            }
        }
    }

    private abstract class BlockingProviderBase : IStorageProvider
    {
        public TaskCompletionSource<bool> Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public StorageCapabilitySet Capabilities { get; } = new(
            StorageCapability.Upload | StorageCapability.Download);

        public abstract TransferExecutionCapabilities TransferCapabilities { get; }

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

        public abstract Task<OperationResult> UploadAsync(
            RemotePath path,
            Stream content,
            long? contentLength,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default);

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

    private sealed class CooperativeBlockingProvider : BlockingProviderBase
    {
        public override TransferExecutionCapabilities TransferCapabilities { get; } = new(
            TransferIoExecutionKind.Asynchronous,
            TransferIoExecutionKind.Asynchronous,
            supportsCooperativeCancellation: true,
            supportsActiveAbort: false,
            supportsProgress: true);

        public override async Task<OperationResult> UploadAsync(
            RemotePath path,
            Stream content,
            long? contentLength,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return OperationResult.Success();
        }
    }

    private sealed class ProgressBlockingProvider : BlockingProviderBase
    {
        public const long ReportedBytes = 512;
        public const double ReportedSpeed = 4096;

        public TaskCompletionSource<bool> ProgressReported { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override TransferExecutionCapabilities TransferCapabilities { get; } = new(
            TransferIoExecutionKind.Asynchronous,
            TransferIoExecutionKind.Asynchronous,
            supportsCooperativeCancellation: true,
            supportsActiveAbort: false,
            supportsProgress: true);

        public override async Task<OperationResult> UploadAsync(
            RemotePath path,
            Stream content,
            long? contentLength,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            progress?.Report(new TransferProgress(ReportedBytes, 1024, ReportedSpeed));
            ProgressReported.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return OperationResult.Success();
        }
    }

    private sealed class NonCooperativeBlockingProvider : BlockingProviderBase
    {
        public TaskCompletionSource<bool> Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Completed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override TransferExecutionCapabilities TransferCapabilities { get; } = new(
            TransferIoExecutionKind.Asynchronous,
            TransferIoExecutionKind.Asynchronous,
            supportsCooperativeCancellation: false,
            supportsActiveAbort: false,
            supportsProgress: false);

        public override async Task<OperationResult> UploadAsync(
            RemotePath path,
            Stream content,
            long? contentLength,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Release.Task.ConfigureAwait(false);
            Completed.TrySetResult(true);
            return OperationResult.Success();
        }
    }
}
