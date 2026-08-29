using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Transfer.Queue;
using AtomBox.Transfer.Workers;

namespace AtomBox.Transfer.Scheduling;

public sealed class TransferTaskScheduler : ITransferTaskScheduler
{
    private readonly ITransferTaskStore _taskStore;
    private readonly TransferQueue _queue;
    private readonly Func<TransferWorker> _workerFactory;
    private readonly TransferCancellationRegistry _cancellations;
    private readonly IApplicationSettingsRepository _settings;
    private readonly SemaphoreSlim _wakeGate = new(1, 1);

    public TransferTaskScheduler(
        ITransferTaskStore taskStore,
        TransferQueue queue,
        Func<TransferWorker> workerFactory,
        TransferCancellationRegistry cancellations,
        IApplicationSettingsRepository settings)
    {
        _taskStore = taskStore;
        _queue = queue;
        _workerFactory = workerFactory;
        _cancellations = cancellations;
        _settings = settings;
    }

    public Task<OperationResult> SubmitAsync(
        TransferTask task,
        CancellationToken cancellationToken = default)
    {
        return _taskStore.SaveAsync(task, cancellationToken);
    }

    public async Task<OperationResult> CancelAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var taskResult = await _taskStore.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (taskResult.IsFailure)
            {
                return OperationResult.Failure(taskResult.Error!);
            }

            var task = taskResult.GetValueOrThrow();
            if (!task.CanCancel())
            {
                return OperationResult.Failure(CannotCancelError());
            }

            if (task.Status == TransferStatus.Pending)
            {
                var activeCancellationRequested = _cancellations.Cancel(task.Id);
                var targetStatus = activeCancellationRequested
                    ? TransferStatus.Canceling
                    : TransferStatus.Canceled;
                var reason = activeCancellationRequested
                    ? "正在取消传输任务。"
                    : "用户取消了传输任务。";
                var transitionResult = await _taskStore.TryTransitionStatusAsync(
                    task.Id,
                    TransferStatus.Pending,
                    targetStatus,
                    DateTimeOffset.UtcNow,
                    reason,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (transitionResult.IsFailure)
                {
                    return OperationResult.Failure(transitionResult.Error!);
                }

                if (transitionResult.GetValueOrThrow())
                {
                    return OperationResult.Success();
                }

                continue;
            }

            var cancelingResult = await _taskStore.TryTransitionStatusAsync(
                task.Id,
                TransferStatus.Running,
                TransferStatus.Canceling,
                DateTimeOffset.UtcNow,
                "正在取消传输任务。",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (cancelingResult.IsFailure)
            {
                return OperationResult.Failure(cancelingResult.Error!);
            }

            if (!cancelingResult.GetValueOrThrow())
            {
                continue;
            }

            if (_cancellations.Cancel(task.Id))
            {
                return OperationResult.Success();
            }

            var orphanedResult = await _taskStore.TryTransitionStatusAsync(
                task.Id,
                TransferStatus.Canceling,
                TransferStatus.Canceled,
                DateTimeOffset.UtcNow,
                "传输任务已取消，但未找到正在运行的传输，临时文件清理结果未知。",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return orphanedResult.IsFailure
                ? OperationResult.Failure(orphanedResult.Error!)
                : OperationResult.Success();
        }

        return OperationResult.Failure(CannotCancelError());
    }

    private static StorageError CannotCancelError()
    {
        return new StorageError(
            StorageErrorCode.Conflict,
            "Transfer task cannot be canceled in its current state.",
            StorageErrorCategory.Conflict);
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
        if (!task.CanRetry())
        {
            return OperationResult.Failure(new StorageError(
                StorageErrorCode.Conflict,
                "Transfer task cannot be retried in its current state.",
                StorageErrorCategory.Conflict));
        }

        return await _taskStore.SaveAsync(
            task.WithStatus(TransferStatus.Pending, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> WakeAsync(CancellationToken cancellationToken = default)
    {
        await _wakeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var concurrencyResult = await GetConcurrencyAsync(cancellationToken).ConfigureAwait(false);
            if (concurrencyResult.IsFailure)
            {
                return OperationResult.Failure(concurrencyResult.Error!);
            }

            var tasksResult = await _taskStore.ListAsync(cancellationToken).ConfigureAwait(false);
            if (tasksResult.IsFailure)
            {
                return OperationResult.Failure(tasksResult.Error!);
            }

            var pendingTasks = _queue.SelectPending(tasksResult.GetValueOrThrow());
            StorageError? firstFailure = null;
            using var concurrencyGate = new SemaphoreSlim(concurrencyResult.GetValueOrThrow());

            var results = await Task.WhenAll(pendingTasks.Select(task =>
                    ExecutePendingTaskWithConcurrencyAsync(task, concurrencyGate, cancellationToken)))
                .ConfigureAwait(false);
            foreach (var result in results)
            {
                firstFailure ??= result.IsFailure ? result.Error : null;
            }

            return firstFailure is null
                ? OperationResult.Success()
                : OperationResult.Failure(firstFailure);
        }
        finally
        {
            _wakeGate.Release();
        }
    }

    private static async Task<OperationResult> ExecutePendingTaskWithConcurrencyAsync(
        TransferTask task,
        SemaphoreSlim concurrencyGate,
        CancellationToken cancellationToken,
        Func<TransferTask, CancellationToken, Task<OperationResult>> executeAsync)
    {
        await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await executeAsync(task, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            concurrencyGate.Release();
        }
    }

    private Task<OperationResult> ExecutePendingTaskWithConcurrencyAsync(
        TransferTask task,
        SemaphoreSlim concurrencyGate,
        CancellationToken cancellationToken)
    {
        return ExecutePendingTaskWithConcurrencyAsync(
            task,
            concurrencyGate,
            cancellationToken,
            ExecutePendingTaskAsync);
    }

    private async Task<OperationResult<int>> GetConcurrencyAsync(CancellationToken cancellationToken)
    {
        var settingsResult = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (settingsResult.IsFailure)
        {
            return OperationResult<int>.Failure(settingsResult.Error!);
        }

        return OperationResult<int>.Success(Math.Max(1, settingsResult.GetValueOrThrow().DefaultConcurrency));
    }

    private async Task<OperationResult> ExecutePendingTaskAsync(
        TransferTask task,
        CancellationToken cancellationToken)
    {
        var currentTaskResult = await _taskStore.GetByIdAsync(task.Id, cancellationToken).ConfigureAwait(false);
        if (currentTaskResult.IsFailure)
        {
            return OperationResult.Failure(currentTaskResult.Error!);
        }

        var currentTask = currentTaskResult.GetValueOrThrow();
        if (currentTask.Status != TransferStatus.Pending)
        {
            return OperationResult.Success();
        }

        var worker = _workerFactory();
        using var cancellation = _cancellations.Register(currentTask.Id, cancellationToken);
        return await worker.ExecuteAsync(currentTask, cancellation.Token).ConfigureAwait(false);
    }
}
