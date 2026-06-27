using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Infrastructure.Storage;

public sealed class TransferStateStore : ITransferStateStore
{
    private readonly ITransferTaskStore _taskStore;
    private readonly JsonFileStore<List<TransferProgressRecord>> _progressStore;

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
            .Where(task => task.Status is TransferStatus.Pending or TransferStatus.Running or TransferStatus.Paused or TransferStatus.Interrupted)
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
            .Where(task => task.Status is TransferStatus.Succeeded or TransferStatus.Failed or TransferStatus.Canceled or TransferStatus.Interrupted)
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
        var saveTaskResult = await _taskStore.SaveAsync(task, cancellationToken).ConfigureAwait(false);
        if (saveTaskResult.IsFailure || progress is null)
        {
            return saveTaskResult;
        }

        var progressResult = await _progressStore.ReadAsync([], cancellationToken).ConfigureAwait(false);
        if (progressResult.IsFailure)
        {
            return OperationResult.Failure(progressResult.Error!);
        }

        var records = progressResult.GetValueOrThrow();
        var index = records.FindIndex(item => item.TaskId == task.Id.Value);
        var record = new TransferProgressRecord(task.Id.Value, progress);
        if (index < 0)
        {
            records.Add(record);
        }
        else
        {
            records[index] = record;
        }

        return await _progressStore.WriteAsync(records, cancellationToken).ConfigureAwait(false);
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

    private sealed record TransferProgressRecord(Guid TaskId, TransferProgress Progress);
}
