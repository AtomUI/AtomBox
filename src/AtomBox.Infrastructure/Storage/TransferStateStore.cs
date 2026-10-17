using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Infrastructure.Storage;

public sealed class TransferStateStore : ITransferStateStore
{
    private readonly ITransferTaskStore _taskStore;
    private readonly JsonFileStore<List<TransferProgressRecord>> _progressStore;
    private readonly SemaphoreSlim _progressGate = new(1, 1);

    public TransferStateStore(
        ITransferTaskStore taskStore,
        AtomBoxStoragePaths paths)
    {
        _taskStore = taskStore;
        _progressStore = new JsonFileStore<List<TransferProgressRecord>>(paths.TransferProgressFile);
    }

    public async Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(
        CancellationToken cancellationToken = default)
    {
        var tasksResult = await _taskStore.ListAsync(cancellationToken).ConfigureAwait(false);
        if (tasksResult.IsFailure)
        {
            return OperationResult<IReadOnlyList<TransferStateSnapshot>>.Failure(tasksResult.Error!);
        }

        var progressResult = await ReadProgressAsync(cancellationToken).ConfigureAwait(false);
        if (progressResult.IsFailure)
        {
            return OperationResult<IReadOnlyList<TransferStateSnapshot>>.Failure(progressResult.Error!);
        }

        var progressByTask = progressResult.GetValueOrThrow();
        var queue = tasksResult.GetValueOrThrow()
            .Where(task => task.Status is TransferStatus.Pending or TransferStatus.Running or TransferStatus.Canceling or TransferStatus.Paused or TransferStatus.Interrupted)
            .OrderBy(task => task.CreatedAt)
            .Select(task => new TransferStateSnapshot(task, GetProgress(progressByTask, task)))
            .ToArray();

        return OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(queue);
    }

    public async Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var tasksResult = await _taskStore.ListAsync(cancellationToken).ConfigureAwait(false);
        if (tasksResult.IsFailure)
        {
            return OperationResult<IReadOnlyList<TransferStateSnapshot>>.Failure(tasksResult.Error!);
        }

        var progressResult = await ReadProgressAsync(cancellationToken).ConfigureAwait(false);
        if (progressResult.IsFailure)
        {
            return OperationResult<IReadOnlyList<TransferStateSnapshot>>.Failure(progressResult.Error!);
        }

        var progressByTask = progressResult.GetValueOrThrow();
        var history = tasksResult.GetValueOrThrow()
            .Where(task => task.Status is TransferStatus.Succeeded or TransferStatus.Failed or TransferStatus.Canceled or TransferStatus.Abandoned)
            .OrderByDescending(task => task.UpdatedAt)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Max(take, 0))
            .Select(task => new TransferStateSnapshot(task, GetProgress(progressByTask, task)))
            .ToArray();

        return OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(history);
    }

    public async Task<OperationResult> UpdateStatusAsync(
        TransferTask task,
        TransferProgress? progress,
        CancellationToken cancellationToken = default)
    {
        if (progress is not null)
        {
            var currentResult = await _taskStore.GetByIdAsync(task.Id, cancellationToken).ConfigureAwait(false);
            if (currentResult.IsSuccess)
            {
                var current = currentResult.GetValueOrThrow();
                if (current.Status is TransferStatus.Running or TransferStatus.Canceling &&
                    task.Revision == current.Revision + 1)
                {
                    var finalProgressResult = await SaveProgressBatchAsync(
                        [new TransferProgressUpdate(current.Id, current.Revision, long.MaxValue, progress)],
                        cancellationToken).ConfigureAwait(false);
                    if (finalProgressResult.IsFailure)
                    {
                        return finalProgressResult;
                    }
                }
            }
        }

        var saveTaskResult = await _taskStore.SaveAsync(task, cancellationToken).ConfigureAwait(false);
        if (saveTaskResult.IsFailure || progress is null)
        {
            return saveTaskResult;
        }

        if (task.Status is not (TransferStatus.Running or TransferStatus.Canceling))
        {
            return saveTaskResult;
        }

        return await SaveProgressBatchAsync(
            [new TransferProgressUpdate(task.Id, task.Revision, 0, progress)],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> SaveProgressBatchAsync(
        IReadOnlyList<TransferProgressUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            return OperationResult.Success();
        }

        await _progressGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var progressResult = await _progressStore.ReadAsync([], cancellationToken).ConfigureAwait(false);
            if (progressResult.IsFailure)
            {
                return OperationResult.Failure(progressResult.Error!);
            }

            var records = progressResult.GetValueOrThrow();
            foreach (var update in updates
                         .GroupBy(item => item.TaskId)
                         .Select(group => group
                             .OrderByDescending(item => item.ExecutionRevision)
                             .ThenByDescending(item => item.Sequence)
                             .First()))
            {
                var taskResult = await _taskStore.GetByIdAsync(update.TaskId, cancellationToken)
                    .ConfigureAwait(false);
                if (taskResult.IsFailure)
                {
                    if (taskResult.Error?.Category == AtomBox.Core.Errors.StorageErrorCategory.NotFound)
                    {
                        continue;
                    }

                    return OperationResult.Failure(taskResult.Error!);
                }

                var task = taskResult.GetValueOrThrow();
                if (task.Revision != update.ExecutionRevision ||
                    task.Status is not (TransferStatus.Running or TransferStatus.Canceling))
                {
                    continue;
                }

                var index = records.FindIndex(item => item.TaskId == update.TaskId.Value);
                var record = new TransferProgressRecord(
                    update.TaskId.Value,
                    update.ExecutionRevision,
                    update.Sequence,
                    update.Progress);
                if (index < 0)
                {
                    records.Add(record);
                    continue;
                }

                var current = records[index];
                if (update.ExecutionRevision > current.ExecutionRevision ||
                    update.ExecutionRevision == current.ExecutionRevision && update.Sequence >= current.Sequence)
                {
                    records[index] = record;
                }
            }

            return await _progressStore.WriteAsync(records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _progressGate.Release();
        }
    }

    private async Task<OperationResult<IReadOnlyDictionary<Guid, TransferProgress>>> ReadProgressAsync(
        CancellationToken cancellationToken)
    {
        var progressResult = await _progressStore.ReadAsync([], cancellationToken).ConfigureAwait(false);
        if (progressResult.IsFailure)
        {
            return OperationResult<IReadOnlyDictionary<Guid, TransferProgress>>.Failure(progressResult.Error!);
        }

        var progressByTask = progressResult.GetValueOrThrow()
            .GroupBy(item => item.TaskId)
            .ToDictionary(group => group.Key, group => group.Last().Progress);

        return OperationResult<IReadOnlyDictionary<Guid, TransferProgress>>.Success(progressByTask);
    }

    private static TransferProgress? GetProgress(
        IReadOnlyDictionary<Guid, TransferProgress> progressByTask,
        TransferTask task)
    {
        return progressByTask.TryGetValue(task.Id.Value, out var progress)
            ? progress
            : null;
    }

    private sealed record TransferProgressRecord(
        Guid TaskId,
        long ExecutionRevision,
        long Sequence,
        TransferProgress Progress);
}
