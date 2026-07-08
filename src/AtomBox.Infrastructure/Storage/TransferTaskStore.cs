using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Infrastructure.Storage;

public sealed class TransferTaskStore : ITransferTaskStore
{
    private readonly JsonFileStore<List<TransferTask>> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TransferTaskStore(AtomBoxStoragePaths paths)
    {
        _store = new JsonFileStore<List<TransferTask>>(paths.TransferTasksFile);
    }

    public async Task<OperationResult<TransferTask>> GetByIdAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tasksResult = await ReadTasksAsync(cancellationToken).ConfigureAwait(false);
            if (tasksResult.IsFailure)
            {
                return OperationResult<TransferTask>.Failure(tasksResult.Error!);
            }

            var task = tasksResult.GetValueOrThrow().FirstOrDefault(item => item.Id == taskId);
            return task is null
                ? OperationResult<TransferTask>.Failure(StorageError.NotFound("Transfer task was not found."))
                : OperationResult<TransferTask>.Success(task);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tasksResult = await ReadTasksAsync(cancellationToken).ConfigureAwait(false);
            return tasksResult.IsFailure
                ? OperationResult<IReadOnlyList<TransferTask>>.Failure(tasksResult.Error!)
                : OperationResult<IReadOnlyList<TransferTask>>.Success(tasksResult.GetValueOrThrow());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult> SaveAsync(
        TransferTask task,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tasksResult = await ReadTasksAsync(cancellationToken).ConfigureAwait(false);
            if (tasksResult.IsFailure)
            {
                return OperationResult.Failure(tasksResult.Error!);
            }

            var tasks = tasksResult.GetValueOrThrow();
            var index = tasks.FindIndex(item => item.Id == task.Id);
            if (index < 0)
            {
                tasks.Add(task);
            }
            else if (task.UpdatedAt >= tasks[index].UpdatedAt)
            {
                tasks[index] = task;
            }

            return await _store.WriteAsync(tasks, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult> DeleteAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tasksResult = await ReadTasksAsync(cancellationToken).ConfigureAwait(false);
            if (tasksResult.IsFailure)
            {
                return OperationResult.Failure(tasksResult.Error!);
            }

            var tasks = tasksResult.GetValueOrThrow();
            var removed = tasks.RemoveAll(item => item.Id == taskId);
            return removed == 0
                ? OperationResult.Failure(StorageError.NotFound("Transfer task was not found."))
                : await _store.WriteAsync(tasks, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<OperationResult<List<TransferTask>>> ReadTasksAsync(CancellationToken cancellationToken)
    {
        return _store.ReadAsync([], cancellationToken);
    }
}
