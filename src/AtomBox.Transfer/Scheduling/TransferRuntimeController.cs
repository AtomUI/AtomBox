using System.Collections.Concurrent;
using System.Threading.Channels;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Transfer.Progress;
using AtomBox.Transfer.State;
using AtomBox.Transfer.Workers;

namespace AtomBox.Transfer.Scheduling;

public sealed class TransferRuntimeController : ITransferTaskScheduler
{
    private readonly ITransferTaskStore _taskStore;
    private readonly ITransferStateStore _stateStore;
    private readonly IApplicationSettingsRepository _settings;
    private readonly Func<TransferOperationExecutor> _executorFactory;
    private readonly TransferStateProjector _projector;
    private readonly AtomBoxTransferState _state;
    private readonly ITransferRuntimeDiagnostics _diagnostics;
    private readonly TransferRuntimeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly object _lifecycleGate = new();
    private readonly ConcurrentDictionary<TransferTaskId, ActiveExecution> _active = [];
    private readonly Channel<bool> _dispatchSignals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
    private readonly Channel<bool> _progressSignals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    private TransferRuntimeLifecycleState _lifecycle = TransferRuntimeLifecycleState.Created;
    private TransferRuntimeHealthState _health = TransferRuntimeHealthState.Healthy;
    private bool _acceptingSubmissions;
    private StorageError? _lastFailure;
    private int _pendingTaskCount;
    private int _inflightSubmissions;
    private TaskCompletionSource<bool>? _submissionsDrained;
    private CancellationTokenSource? _runtimeCancellation;
    private Task? _startTask;
    private Task? _stopTask;
    private Task? _dispatchLoop;
    private Task? _progressLoop;
    private int _dispatchFailureCount;
    private int _progressFailureCount;

    public TransferRuntimeController(
        ITransferTaskStore taskStore,
        ITransferStateStore stateStore,
        IApplicationSettingsRepository settings,
        Func<TransferOperationExecutor> executorFactory,
        TransferStateProjector projector,
        AtomBoxTransferState state,
        ITransferRuntimeDiagnostics? diagnostics = null,
        TransferRuntimeOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _taskStore = taskStore;
        _stateStore = stateStore;
        _settings = settings;
        _executorFactory = executorFactory;
        _projector = projector;
        _state = state;
        _diagnostics = diagnostics ?? NullTransferRuntimeDiagnostics.Instance;
        _options = options ?? new TransferRuntimeOptions();
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public TransferRuntimeSnapshot Current
    {
        get
        {
            lock (_lifecycleGate)
            {
                return CreateSnapshotLocked();
            }
        }
    }

    public Task StartAsync(
        CancellationToken applicationLifetime,
        CancellationToken startupCancellationToken = default)
    {
        Task startTask;
        lock (_lifecycleGate)
        {
            startTask = _lifecycle switch
            {
                TransferRuntimeLifecycleState.Created => StartOnceLocked(
                    applicationLifetime,
                    startupCancellationToken),
                TransferRuntimeLifecycleState.Starting => _startTask!,
                TransferRuntimeLifecycleState.Running => Task.CompletedTask,
                TransferRuntimeLifecycleState.Faulted => _startTask ?? Task.FromException(
                    new InvalidOperationException("Transfer runtime startup previously failed.")),
                _ => Task.FromException(new InvalidOperationException(
                    "A stopped transfer runtime cannot be restarted in the same host.")),
            };
        }

        return startupCancellationToken.CanBeCanceled
            ? startTask.WaitAsync(startupCancellationToken)
            : startTask;
    }

    public Task StopAsync(CancellationToken shutdownCancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            if (_lifecycle == TransferRuntimeLifecycleState.Stopped)
            {
                return Task.CompletedTask;
            }

            if (_lifecycle == TransferRuntimeLifecycleState.Created)
            {
                _lifecycle = TransferRuntimeLifecycleState.Stopped;
                _acceptingSubmissions = false;
                PublishSnapshotNoThrowLocked();
                return Task.CompletedTask;
            }

            if (_lifecycle == TransferRuntimeLifecycleState.Stopping)
            {
                return _stopTask!;
            }

            _lifecycle = TransferRuntimeLifecycleState.Stopping;
            _acceptingSubmissions = false;
            PublishSnapshotNoThrowLocked();
            _stopTask = StopCoreAsync(shutdownCancellationToken);
            return _stopTask;
        }
    }

    public async Task<OperationResult> SubmitAsync(
        TransferTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Status != TransferStatus.Pending)
        {
            return OperationResult.Failure(StorageError.Validation(
                "Only Pending transfer tasks can be submitted."));
        }

        if (!TryBeginSubmission())
        {
            return OperationResult.Failure(new StorageError(
                StorageErrorCode.Conflict,
                "Transfer runtime is not accepting new tasks.",
                StorageErrorCategory.Conflict,
                isRetryable: true));
        }

        try
        {
            var result = await _taskStore.SaveAsync(task, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                _projector.PublishPersisted(task, progress: null);
                SignalDispatch();
            }

            return result;
        }
        finally
        {
            EndSubmission();
        }
    }

    public async Task<OperationResult> CancelAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        var taskResult = await _taskStore.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (taskResult.IsFailure)
        {
            return OperationResult.Failure(taskResult.Error!);
        }

        var task = taskResult.GetValueOrThrow();
        if (task.Status == TransferStatus.Canceled)
        {
            return OperationResult.Success();
        }

        if (task.Status == TransferStatus.Canceling)
        {
            await RequestActiveCancellationAsync(taskId, task, cancellationToken).ConfigureAwait(false);
            return OperationResult.Success();
        }

        if (task.Status == TransferStatus.Pending)
        {
            var canceled = task.WithStatus(
                TransferStatus.Canceled,
                _timeProvider.GetUtcNow(),
                "用户取消了尚未开始的传输任务。");
            var update = await _taskStore.CompareExchangeAsync(
                canceled,
                task.Revision,
                TransferStatus.Pending,
                cancellationToken).ConfigureAwait(false);
            if (update.IsSuccess)
            {
                _projector.PublishPersisted(canceled, progress: null);
            }

            return update.IsSuccess
                ? OperationResult.Success()
                : OperationResult.Failure(update.Error!);
        }

        if (task.Status != TransferStatus.Running)
        {
            return OperationResult.Failure(new StorageError(
                StorageErrorCode.Conflict,
                "Transfer task cannot be canceled in its current state.",
                StorageErrorCategory.Conflict));
        }

        var canceling = task.WithStatus(
            TransferStatus.Canceling,
            _timeProvider.GetUtcNow(),
            "正在取消传输任务。");
        var cancelingResult = await _taskStore.CompareExchangeAsync(
            canceling,
            task.Revision,
            TransferStatus.Running,
            cancellationToken).ConfigureAwait(false);
        if (cancelingResult.IsFailure)
        {
            return OperationResult.Failure(cancelingResult.Error!);
        }

        _projector.PublishPersisted(canceling, progress: null);
        await RequestActiveCancellationAsync(taskId, canceling, cancellationToken).ConfigureAwait(false);
        return OperationResult.Success();
    }

    public async Task<OperationResult> RetryAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        var taskResult = await _taskStore.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (taskResult.IsFailure)
        {
            return OperationResult.Failure(taskResult.Error!);
        }

        var task = taskResult.GetValueOrThrow();
        if (!task.CanRetry() || !task.IsRetryable)
        {
            return OperationResult.Failure(new StorageError(
                StorageErrorCode.Conflict,
                "Transfer task cannot be retried in its current state.",
                StorageErrorCategory.Conflict));
        }

        var pending = task.WithStatus(TransferStatus.Pending, _timeProvider.GetUtcNow());
        var update = await _taskStore.CompareExchangeAsync(
            pending,
            task.Revision,
            task.Status,
            cancellationToken).ConfigureAwait(false);
        if (update.IsSuccess)
        {
            _projector.PublishPersisted(pending, progress: null);
            SignalDispatch();
            return OperationResult.Success();
        }

        return OperationResult.Failure(update.Error!);
    }

    internal Task<OperationResult> WakeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SignalDispatch();
        return Task.FromResult(OperationResult.Success());
    }

    private Task StartOnceLocked(
        CancellationToken applicationLifetime,
        CancellationToken startupCancellationToken)
    {
        _lifecycle = TransferRuntimeLifecycleState.Starting;
        _health = TransferRuntimeHealthState.Healthy;
        _acceptingSubmissions = false;
        PublishSnapshotNoThrowLocked();
        _startTask = StartCoreAsync(applicationLifetime, startupCancellationToken);
        return _startTask;
    }

    private async Task StartCoreAsync(
        CancellationToken applicationLifetime,
        CancellationToken startupCancellationToken)
    {
        try
        {
            _runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationLifetime);
            var tasksResult = await _taskStore.ListAsync(startupCancellationToken).ConfigureAwait(false);
            if (tasksResult.IsFailure)
            {
                throw new TransferRuntimeStartupException(tasksResult.Error!);
            }

            foreach (var task in tasksResult.GetValueOrThrow()
                         .Where(item => item.Status is TransferStatus.Running or TransferStatus.Canceling))
            {
                var interrupted = task.WithStatus(
                    TransferStatus.Interrupted,
                    _timeProvider.GetUtcNow(),
                    "应用上次退出时任务仍在运行，远端最终结果未知。",
                    StorageErrorCategory.Unknown,
                    isRetryable: true);
                var recovery = await _taskStore.CompareExchangeAsync(
                    interrupted,
                    task.Revision,
                    task.Status,
                    startupCancellationToken).ConfigureAwait(false);
                if (recovery.IsFailure && recovery.Error?.Category != StorageErrorCategory.Conflict)
                {
                    throw new TransferRuntimeStartupException(recovery.Error!);
                }
            }

            var refreshResult = await _projector.RefreshAsync(startupCancellationToken).ConfigureAwait(false);
            if (refreshResult.IsFailure)
            {
                throw new TransferRuntimeStartupException(refreshResult.Error!);
            }

            lock (_lifecycleGate)
            {
                if (_lifecycle is TransferRuntimeLifecycleState.Stopping or
                    TransferRuntimeLifecycleState.Stopped)
                {
                    _runtimeCancellation.Cancel();
                    return;
                }

                if (_lifecycle != TransferRuntimeLifecycleState.Starting)
                {
                    throw new InvalidOperationException(
                        $"Transfer runtime cannot complete startup from state '{_lifecycle}'.");
                }

                _dispatchLoop = ObserveLoopAsync(
                    "TRANSFER_RUNTIME_DISPATCH_FAILED",
                    DispatchLoopAsync,
                    _runtimeCancellation.Token);
                _progressLoop = ObserveLoopAsync(
                    "TRANSFER_RUNTIME_PROGRESS_FAILED",
                    ProgressLoopAsync,
                    _runtimeCancellation.Token);
                _lifecycle = TransferRuntimeLifecycleState.Running;
                _health = TransferRuntimeHealthState.Healthy;
                _acceptingSubmissions = true;
                _lastFailure = null;
                PublishSnapshotNoThrowLocked();
            }

            SignalDispatch();
        }
        catch (TransferRuntimeStartupException exception)
        {
            TransitionStartupFault(exception.Error, exception);
            throw;
        }
        catch (Exception exception)
        {
            var error = ToInfrastructureError(exception, "Transfer runtime startup failed.");
            TransitionStartupFault(error, exception);
            throw new TransferRuntimeStartupException(error, exception);
        }
    }

    private async Task StopCoreAsync(CancellationToken shutdownCancellationToken)
    {
        var failures = new List<Exception>();
        try
        {
            Task submissionsDrained;
            lock (_lifecycleGate)
            {
                submissionsDrained = _inflightSubmissions == 0
                    ? Task.CompletedTask
                    : _submissionsDrained!.Task;
            }

            await submissionsDrained.WaitAsync(shutdownCancellationToken).ConfigureAwait(false);
            _runtimeCancellation?.Cancel();
            SignalDispatch();
            SignalProgress();

            await ObserveShutdownTaskAsync(
                _dispatchLoop,
                shutdownCancellationToken,
                failures).ConfigureAwait(false);

            var active = _active.Values.ToArray();
            foreach (var entry in active)
            {
                entry.RequestHostCancellation();
                try
                {
                    _ = await entry.Executor.TryAbortAsync(shutdownCancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            var executions = active
                .Select(entry => entry.ExecutionTask)
                .Where(task => task is not null)
                .Cast<Task>()
                .ToArray();
            if (executions.Length > 0)
            {
                var allExecutions = Task.WhenAll(executions);
                var grace = Task.Delay(
                    _options.ShutdownGracePeriod,
                    _timeProvider,
                    shutdownCancellationToken);
                var completed = await Task.WhenAny(allExecutions, grace).ConfigureAwait(false);
                if (completed == allExecutions)
                {
                    try
                    {
                        await allExecutions.ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }

            foreach (var entry in _active.Values.ToArray())
            {
                entry.Abandon();
                try
                {
                    await FlushEntryAsync(entry, shutdownCancellationToken).ConfigureAwait(false);
                    await PersistInterruptedAsync(
                        entry.TaskId,
                        "应用关闭时传输执行体未能在宽限期内退出。",
                        shutdownCancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            await ObserveShutdownTaskAsync(
                _progressLoop,
                shutdownCancellationToken,
                failures).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            lock (_lifecycleGate)
            {
                _lifecycle = TransferRuntimeLifecycleState.Stopped;
                _acceptingSubmissions = false;
                PublishSnapshotNoThrowLocked();
            }
        }

        if (failures.Count > 0)
        {
            throw new TransferRuntimeShutdownException(failures);
        }
    }

    private static async Task ObserveShutdownTaskAsync(
        Task? task,
        CancellationToken shutdownCancellationToken,
        ICollection<Exception> failures)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(shutdownCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (shutdownCancellationToken.IsCancellationRequested)
        {
            failures.Add(exception);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        while (await _dispatchSignals.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_dispatchSignals.Reader.TryRead(out _))
            {
            }

            await DispatchPendingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var settingsResult = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (settingsResult.IsFailure)
        {
            await HandleInfrastructureFailureAsync(
                RuntimeComponent.Dispatch,
                settingsResult.Error!,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var tasksResult = await _taskStore.ListAsync(cancellationToken).ConfigureAwait(false);
        if (tasksResult.IsFailure)
        {
            await HandleInfrastructureFailureAsync(
                RuntimeComponent.Dispatch,
                tasksResult.Error!,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        MarkComponentHealthy(RuntimeComponent.Dispatch);
        var pending = tasksResult.GetValueOrThrow()
            .Where(task => task.Status == TransferStatus.Pending)
            .OrderBy(task => task.CreatedAt)
            .ThenBy(task => task.Id.Value)
            .ToArray();
        lock (_lifecycleGate)
        {
            _pendingTaskCount = pending.Length;
            PublishSnapshotNoThrowLocked();
        }

        var maximum = Math.Max(1, settingsResult.GetValueOrThrow().DefaultConcurrency);
        var available = Math.Max(0, maximum - _active.Count);
        foreach (var pendingTask in pending.Take(available))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var running = pendingTask.WithStatus(
                TransferStatus.Running,
                _timeProvider.GetUtcNow());
            var claim = await _taskStore.CompareExchangeAsync(
                running,
                pendingTask.Revision,
                TransferStatus.Pending,
                cancellationToken).ConfigureAwait(false);
            if (claim.IsFailure)
            {
                if (claim.Error?.Category == StorageErrorCategory.Conflict)
                {
                    continue;
                }

                await HandleInfrastructureFailureAsync(
                    RuntimeComponent.Dispatch,
                    claim.Error!,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var initialProgress = new TransferProgressUpdate(
                running.Id,
                running.Revision,
                sequence: 0,
                new TransferProgress(0, null, null));
            var entry = new ActiveExecution(
                running,
                _executorFactory(),
                _runtimeCancellation!.Token,
                SignalProgress);
            if (!_active.TryAdd(running.Id, entry))
            {
                entry.Dispose();
                throw new InvalidOperationException(
                    $"Transfer task {running.Id} was claimed more than once by the runtime.");
            }

            OperationResult progressResult;
            try
            {
                progressResult = await _stateStore.SaveProgressBatchAsync(
                    [initialProgress],
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                entry.Abandon();
                _active.TryRemove(running.Id, out _);
                entry.Dispose();
                await PersistInterruptedAsync(
                    running.Id,
                    "任务领取后初始化持久化进度被中断。",
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            if (progressResult.IsFailure)
            {
                entry.Abandon();
                _active.TryRemove(running.Id, out _);
                entry.Dispose();
                await PersistInterruptedAsync(
                    running.Id,
                    "任务领取后无法初始化持久化进度。",
                    CancellationToken.None).ConfigureAwait(false);
                await HandleInfrastructureFailureAsync(
                    RuntimeComponent.Progress,
                    progressResult.Error!,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            entry.ExecutionTask = RunWorkerAsync(entry);
            _projector.PublishPersisted(running, initialProgress.Progress);
            lock (_lifecycleGate)
            {
                _pendingTaskCount = Math.Max(0, _pendingTaskCount - 1);
                PublishSnapshotNoThrowLocked();
            }
        }
    }

    private async Task RunWorkerAsync(ActiveExecution entry)
    {
        await Task.Yield();
        TransferOperationExecution execution;
        try
        {
            try
            {
                execution = await entry.Executor.ExecuteAsync(
                    entry.CurrentTask,
                    entry,
                    entry.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                execution = new TransferOperationExecution(
                    OperationResult.Failure(new StorageError(
                        StorageErrorCode.OperationCanceled,
                        "Transfer provider operation was canceled.",
                        StorageErrorCategory.Canceled,
                        isRetryable: true)),
                    entry.CurrentTask.LocalPath);
            }
            catch (Exception exception)
            {
                WriteDiagnostic(new TransferRuntimeDiagnostic(
                    "TRANSFER_TASK_UNHANDLED_EXCEPTION",
                    "A transfer provider operation threw an unhandled exception.",
                    TransferRuntimeDiagnosticSeverity.Error,
                    exception,
                    entry.TaskId));
                execution = new TransferOperationExecution(
                    OperationResult.Failure(StorageError.Unknown(
                        "Transfer provider operation failed unexpectedly.")),
                    entry.CurrentTask.LocalPath);
            }

            entry.CloseProgress();
            if (entry.IsAbandoned)
            {
                return;
            }

            await FlushEntryAsync(entry, CancellationToken.None).ConfigureAwait(false);
            if (entry.IsAbandoned)
            {
                return;
            }

            await PersistExecutionResultAsync(entry, execution, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TransitionRuntimeFault(
                ToInfrastructureError(exception, "Transfer execution state could not be persisted."),
                exception);
        }
        finally
        {
            entry.CloseProgress();
            _active.TryRemove(entry.TaskId, out _);
            entry.Dispose();
            SignalDispatch();
            lock (_lifecycleGate)
            {
                PublishSnapshotNoThrowLocked();
            }
        }
    }

    private async Task PersistExecutionResultAsync(
        ActiveExecution entry,
        TransferOperationExecution execution,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var currentResult = await _taskStore.GetByIdAsync(entry.TaskId, cancellationToken)
                .ConfigureAwait(false);
            if (currentResult.IsFailure)
            {
                throw new TransferRuntimePersistenceException(currentResult.Error!);
            }

            var current = currentResult.GetValueOrThrow();
            if (current.Status is not (TransferStatus.Running or TransferStatus.Canceling))
            {
                return;
            }

            if (execution.EffectiveLocalPath != current.LocalPath)
            {
                var renamed = current.WithLocalPath(
                    execution.EffectiveLocalPath,
                    _timeProvider.GetUtcNow());
                var renamedResult = await _taskStore.CompareExchangeAsync(
                    renamed,
                    current.Revision,
                    current.Status,
                    cancellationToken).ConfigureAwait(false);
                if (renamedResult.IsFailure)
                {
                    if (renamedResult.Error?.Category == StorageErrorCategory.Conflict)
                    {
                        continue;
                    }

                    throw new TransferRuntimePersistenceException(renamedResult.Error!);
                }

                current = renamed;
                entry.UpdateTask(current);
            }

            TransferTask terminal;
            if (execution.Result.IsSuccess)
            {
                terminal = current.WithStatus(
                    TransferStatus.Succeeded,
                    _timeProvider.GetUtcNow());
            }
            else if (entry.IsUserCancellationRequested && current.Status == TransferStatus.Canceling)
            {
                terminal = current.WithStatus(
                    TransferStatus.Canceled,
                    _timeProvider.GetUtcNow(),
                    "用户取消了传输任务。");
            }
            else if (entry.IsHostCancellationRequested)
            {
                terminal = current.WithStatus(
                    TransferStatus.Interrupted,
                    _timeProvider.GetUtcNow(),
                    "应用关闭中断了传输任务，远端最终状态未知。",
                    StorageErrorCategory.Unknown,
                    isRetryable: true);
            }
            else
            {
                var error = execution.Result.Error!;
                terminal = current.WithStatus(
                    TransferStatus.Failed,
                    _timeProvider.GetUtcNow(),
                    error.Message,
                    error.Category,
                    error.IsRetryable);
            }

            var terminalResult = await _taskStore.CompareExchangeAsync(
                terminal,
                current.Revision,
                current.Status,
                cancellationToken).ConfigureAwait(false);
            if (terminalResult.IsSuccess)
            {
                _projector.PublishPersisted(terminal, entry.LatestProgress);
                return;
            }

            if (terminalResult.Error?.Category != StorageErrorCategory.Conflict)
            {
                throw new TransferRuntimePersistenceException(terminalResult.Error!);
            }
        }

        throw new InvalidOperationException(
            $"Transfer task {entry.TaskId} could not converge after repeated concurrent state changes.");
    }

    private async Task ProgressLoopAsync(CancellationToken cancellationToken)
    {
        while (await _progressSignals.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_progressSignals.Reader.TryRead(out _))
            {
            }

            await Task.Delay(
                _options.ProgressFlushInterval,
                _timeProvider,
                cancellationToken).ConfigureAwait(false);
            await FlushDirtyProgressAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task FlushDirtyProgressAsync(CancellationToken cancellationToken)
    {
        var captured = _active.Values
            .Select(entry => entry.CaptureDirtyProgress())
            .Where(item => item is not null)
            .Cast<CapturedProgress>()
            .ToArray();
        if (captured.Length == 0)
        {
            MarkComponentHealthy(RuntimeComponent.Progress);
            return;
        }

        var updates = captured.Select(item => item.Update).ToArray();
        var result = await _stateStore.SaveProgressBatchAsync(updates, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            await HandleInfrastructureFailureAsync(
                RuntimeComponent.Progress,
                result.Error!,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var capture in captured)
        {
            capture.Entry.MarkProgressFlushed(capture.Update.Sequence);
        }

        MarkComponentHealthy(RuntimeComponent.Progress);
    }

    private async Task FlushEntryAsync(ActiveExecution entry, CancellationToken cancellationToken)
    {
        var captured = entry.CaptureDirtyProgress(force: true);
        if (captured is null)
        {
            return;
        }

        var result = await _stateStore.SaveProgressBatchAsync(
            [captured.Update],
            cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            throw new TransferRuntimePersistenceException(result.Error!);
        }

        entry.MarkProgressFlushed(captured.Update.Sequence);
    }

    private async Task PersistInterruptedAsync(
        TransferTaskId taskId,
        string reason,
        CancellationToken cancellationToken)
    {
        var currentResult = await _taskStore.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (currentResult.IsFailure)
        {
            if (currentResult.Error?.Category == StorageErrorCategory.NotFound)
            {
                return;
            }

            throw new TransferRuntimePersistenceException(currentResult.Error!);
        }

        var current = currentResult.GetValueOrThrow();
        if (current.Status is not (TransferStatus.Running or TransferStatus.Canceling))
        {
            return;
        }

        var interrupted = current.WithStatus(
            TransferStatus.Interrupted,
            _timeProvider.GetUtcNow(),
            reason,
            StorageErrorCategory.Unknown,
            isRetryable: true);
        var result = await _taskStore.CompareExchangeAsync(
            interrupted,
            current.Revision,
            current.Status,
            cancellationToken).ConfigureAwait(false);
        if (result.IsFailure && result.Error?.Category != StorageErrorCategory.Conflict)
        {
            throw new TransferRuntimePersistenceException(result.Error!);
        }

        if (result.IsSuccess)
        {
            _projector.PublishPersisted(interrupted, progress: null);
        }
    }

    private async Task ObserveLoopAsync(
        string diagnosticCode,
        Func<CancellationToken, Task> loop,
        CancellationToken cancellationToken)
    {
        try
        {
            await loop(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            WriteDiagnostic(new TransferRuntimeDiagnostic(
                diagnosticCode,
                exception.Message,
                TransferRuntimeDiagnosticSeverity.Error,
                exception));
            TransitionRuntimeFault(
                ToInfrastructureError(exception, "Transfer runtime background loop failed."),
                exception);
        }
    }

    private async Task HandleInfrastructureFailureAsync(
        RuntimeComponent component,
        StorageError error,
        CancellationToken cancellationToken)
    {
        var count = component == RuntimeComponent.Dispatch
            ? Interlocked.Increment(ref _dispatchFailureCount)
            : Interlocked.Increment(ref _progressFailureCount);
        lock (_lifecycleGate)
        {
            if (_lifecycle == TransferRuntimeLifecycleState.Running)
            {
                _health = TransferRuntimeHealthState.Degraded;
                _acceptingSubmissions = false;
                _lastFailure = error;
                PublishSnapshotNoThrowLocked();
            }
        }

        WriteDiagnostic(new TransferRuntimeDiagnostic(
            "TRANSFER_RUNTIME_DEGRADED",
            error.Message,
            TransferRuntimeDiagnosticSeverity.Warning));

        if (count > _options.MaxConsecutiveInfrastructureFailures)
        {
            throw new TransferRuntimePersistenceException(error);
        }

        await Task.Delay(
            _options.InfrastructureRetryDelay,
            _timeProvider,
            cancellationToken).ConfigureAwait(false);
        if (component == RuntimeComponent.Dispatch)
        {
            SignalDispatch();
        }
        else
        {
            SignalProgress();
        }
    }

    private void MarkComponentHealthy(RuntimeComponent component)
    {
        if (component == RuntimeComponent.Dispatch)
        {
            Interlocked.Exchange(ref _dispatchFailureCount, 0);
        }
        else
        {
            Interlocked.Exchange(ref _progressFailureCount, 0);
        }

        if (Volatile.Read(ref _dispatchFailureCount) != 0 ||
            Volatile.Read(ref _progressFailureCount) != 0)
        {
            return;
        }

        lock (_lifecycleGate)
        {
            if (_lifecycle == TransferRuntimeLifecycleState.Running &&
                _health == TransferRuntimeHealthState.Degraded)
            {
                _health = TransferRuntimeHealthState.Healthy;
                _acceptingSubmissions = true;
                _lastFailure = null;
                PublishSnapshotNoThrowLocked();
            }
        }
    }

    private bool TryBeginSubmission()
    {
        lock (_lifecycleGate)
        {
            if (!_acceptingSubmissions ||
                _lifecycle != TransferRuntimeLifecycleState.Running ||
                _health != TransferRuntimeHealthState.Healthy)
            {
                return false;
            }

            if (_inflightSubmissions == 0)
            {
                _submissionsDrained = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _inflightSubmissions++;
            return true;
        }
    }

    private void EndSubmission()
    {
        TaskCompletionSource<bool>? drained = null;
        lock (_lifecycleGate)
        {
            _inflightSubmissions--;
            if (_inflightSubmissions == 0)
            {
                drained = _submissionsDrained;
                _submissionsDrained = null;
            }
        }

        drained?.TrySetResult(true);
    }

    private async Task RequestActiveCancellationAsync(
        TransferTaskId taskId,
        TransferTask persistedTask,
        CancellationToken cancellationToken)
    {
        if (!_active.TryGetValue(taskId, out var entry))
        {
            return;
        }

        entry.UpdateTask(persistedTask);
        entry.RequestUserCancellation();
        try
        {
            _ = await entry.Executor.TryAbortAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WriteDiagnostic(new TransferRuntimeDiagnostic(
                "TRANSFER_ABORT_FAILED",
                "Provider active abort failed.",
                TransferRuntimeDiagnosticSeverity.Warning,
                exception,
                taskId));
        }
    }

    private void TransitionStartupFault(StorageError error, Exception exception)
    {
        _runtimeCancellation?.Cancel();
        lock (_lifecycleGate)
        {
            _lifecycle = TransferRuntimeLifecycleState.Faulted;
            _health = TransferRuntimeHealthState.Faulted;
            _acceptingSubmissions = false;
            _lastFailure = error;
            PublishSnapshotNoThrowLocked();
        }

        WriteDiagnostic(new TransferRuntimeDiagnostic(
            "TRANSFER_RUNTIME_STARTUP_FAILED",
            error.Message,
            TransferRuntimeDiagnosticSeverity.Error,
            exception));
    }

    private void TransitionRuntimeFault(StorageError error, Exception exception)
    {
        lock (_lifecycleGate)
        {
            if (_lifecycle is TransferRuntimeLifecycleState.Stopping or TransferRuntimeLifecycleState.Stopped)
            {
                return;
            }

            _lifecycle = TransferRuntimeLifecycleState.Faulted;
            _health = TransferRuntimeHealthState.Faulted;
            _acceptingSubmissions = false;
            _lastFailure = error;
            PublishSnapshotNoThrowLocked();
        }

        _runtimeCancellation?.Cancel();
        WriteDiagnostic(new TransferRuntimeDiagnostic(
            "TRANSFER_RUNTIME_FAULTED",
            error.Message,
            TransferRuntimeDiagnosticSeverity.Error,
            exception));
    }

    private TransferRuntimeSnapshot CreateSnapshotLocked()
    {
        return new TransferRuntimeSnapshot(
            _lifecycle,
            _health,
            _acceptingSubmissions,
            _active.Count,
            _pendingTaskCount,
            _lastFailure is null ? null : TransferProjectionFailure.From(_lastFailure),
            _timeProvider.GetUtcNow());
    }

    private void PublishSnapshotNoThrowLocked()
    {
        try
        {
            _state.PublishRuntime(CreateSnapshotLocked());
        }
        catch (Exception exception)
        {
            WriteDiagnostic(new TransferRuntimeDiagnostic(
                "TRANSFER_RUNTIME_STATE_PUBLISH_FAILED",
                "Transfer runtime health state could not be published.",
                TransferRuntimeDiagnosticSeverity.Warning,
                exception));
        }
    }

    private void SignalDispatch()
    {
        _dispatchSignals.Writer.TryWrite(true);
    }

    private void SignalProgress()
    {
        _progressSignals.Writer.TryWrite(true);
    }

    private void WriteDiagnostic(TransferRuntimeDiagnostic diagnostic)
    {
        try
        {
            _diagnostics.Write(diagnostic);
        }
        catch
        {
        }
    }

    private static StorageError ToInfrastructureError(Exception exception, string message)
    {
        return exception is TransferRuntimePersistenceException persistence
            ? persistence.Error
            : new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                message,
                StorageErrorCategory.Infrastructure,
                isRetryable: false);
    }

    private enum RuntimeComponent
    {
        Dispatch,
        Progress,
    }

    private sealed class ActiveExecution : IProgress<TransferProgress>, IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation;
        private readonly Action _signalProgress;
        private readonly TransferSpeedMeter _speedMeter = new();
        private TransferTask _task;
        private TransferProgress? _latest;
        private long _sequence;
        private long _flushedSequence = -1;
        private bool _acceptingProgress = true;
        private bool _abandoned;
        private bool _userCancellation;
        private bool _hostCancellation;

        public ActiveExecution(
            TransferTask task,
            TransferOperationExecutor executor,
            CancellationToken runtimeCancellation,
            Action signalProgress)
        {
            _task = task;
            Executor = executor;
            _signalProgress = signalProgress;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation);
        }

        public TransferTaskId TaskId => _task.Id;

        public TransferOperationExecutor Executor { get; }

        public CancellationToken CancellationToken => _cancellation.Token;

        public Task? ExecutionTask { get; set; }

        public TransferTask CurrentTask
        {
            get
            {
                lock (_gate)
                {
                    return _task;
                }
            }
        }

        public TransferProgress? LatestProgress
        {
            get
            {
                lock (_gate)
                {
                    return _latest;
                }
            }
        }

        public bool IsAbandoned
        {
            get
            {
                lock (_gate)
                {
                    return _abandoned;
                }
            }
        }

        public bool IsUserCancellationRequested
        {
            get
            {
                lock (_gate)
                {
                    return _userCancellation;
                }
            }
        }

        public bool IsHostCancellationRequested
        {
            get
            {
                lock (_gate)
                {
                    return _hostCancellation;
                }
            }
        }

        public void Report(TransferProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate)
            {
                if (!_acceptingProgress || _abandoned)
                {
                    return;
                }

                var measured = _speedMeter.Apply(value);
                if (_latest is not null && measured.BytesTransferred < _latest.BytesTransferred)
                {
                    return;
                }

                _latest = measured;
                _sequence++;
            }

            _signalProgress();
        }

        public void UpdateTask(TransferTask task)
        {
            lock (_gate)
            {
                _task = task;
            }
        }

        public void RequestUserCancellation()
        {
            lock (_gate)
            {
                _userCancellation = true;
            }

            CancelNoThrow();
        }

        public void RequestHostCancellation()
        {
            lock (_gate)
            {
                _hostCancellation = true;
            }

            CancelNoThrow();
        }

        public void CloseProgress()
        {
            lock (_gate)
            {
                _acceptingProgress = false;
            }
        }

        public void Abandon()
        {
            lock (_gate)
            {
                _abandoned = true;
                _acceptingProgress = false;
            }
        }

        public CapturedProgress? CaptureDirtyProgress(bool force = false)
        {
            lock (_gate)
            {
                if (_latest is null || _abandoned || (!force && _sequence <= _flushedSequence))
                {
                    return null;
                }

                return new CapturedProgress(
                    this,
                    new TransferProgressUpdate(
                        _task.Id,
                        _task.Revision,
                        _sequence,
                        _latest));
            }
        }

        public void MarkProgressFlushed(long sequence)
        {
            lock (_gate)
            {
                _flushedSequence = Math.Max(_flushedSequence, sequence);
            }
        }

        public void Dispose()
        {
            _cancellation.Dispose();
        }

        private void CancelNoThrow()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The execution crossed its terminal cleanup boundary concurrently.
            }
        }
    }

    private sealed record CapturedProgress(
        ActiveExecution Entry,
        TransferProgressUpdate Update);
}
