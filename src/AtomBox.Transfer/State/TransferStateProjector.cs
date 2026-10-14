using AtomBox.Core.Errors;
using AtomBox.Core.Transfers;

namespace AtomBox.Transfer.State;

public sealed class TransferStateProjector : ITransferStateSynchronizer
{
    private readonly AtomBoxTransferState _state;
    private readonly ITransferStateStore _stateStore;
    private readonly SemaphoreSlim _projectionGate = new(1, 1);

    public TransferStateProjector(
        AtomBoxTransferState state,
        ITransferStateStore stateStore)
    {
        _state = state;
        _stateStore = stateStore;
    }

    public async Task<AtomBox.Core.Results.OperationResult> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        await _projectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _stateStore.ListQueueAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                _state.Publish(TransferQueueState.Faulted(result.Error!));
                return AtomBox.Core.Results.OperationResult.Failure(result.Error!);
            }

            _state.Publish(TransferQueueState.Ready(OrderQueue(result.GetValueOrThrow())));
            return AtomBox.Core.Results.OperationResult.Success();
        }
        finally
        {
            _projectionGate.Release();
        }
    }

    internal void PublishReady(IReadOnlyList<TransferStateSnapshot> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        _projectionGate.Wait();
        try
        {
            _state.Publish(TransferQueueState.Ready(OrderQueue(tasks)));
        }
        finally
        {
            _projectionGate.Release();
        }
    }

    internal void PublishFaulted(StorageError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _projectionGate.Wait();
        try
        {
            _state.Publish(TransferQueueState.Faulted(error));
        }
        finally
        {
            _projectionGate.Release();
        }
    }

    internal void PublishPersisted(TransferTask task, TransferProgress? progress)
    {
        ArgumentNullException.ThrowIfNull(task);

        _projectionGate.Wait();
        try
        {
            _state.Update(current =>
            {
                var tasks = current.Status == TransferProjectionStatus.Ready
                    ? current.Tasks.ToList()
                    : [];
                var existingIndex = tasks.FindIndex(snapshot => snapshot.Task.Id == task.Id);

                if (!IsQueueTask(task))
                {
                    if (existingIndex >= 0)
                    {
                        tasks.RemoveAt(existingIndex);
                    }

                    return TransferQueueState.Ready(OrderQueue(tasks));
                }

                var effectiveProgress = progress;
                if (effectiveProgress is null && existingIndex >= 0)
                {
                    effectiveProgress = tasks[existingIndex].Progress;
                }

                var updated = new TransferStateSnapshot(task, effectiveProgress);
                if (existingIndex >= 0)
                {
                    tasks[existingIndex] = updated;
                }
                else
                {
                    tasks.Add(updated);
                }

                return TransferQueueState.Ready(OrderQueue(tasks));
            });
        }
        finally
        {
            _projectionGate.Release();
        }
    }

    private static bool IsQueueTask(TransferTask task)
    {
        return task.Status is TransferStatus.Pending or
            TransferStatus.Running or
            TransferStatus.Paused or
            TransferStatus.Interrupted;
    }

    private static IReadOnlyList<TransferStateSnapshot> OrderQueue(
        IEnumerable<TransferStateSnapshot> tasks)
    {
        return tasks
            .OrderBy(snapshot => snapshot.Task.CreatedAt)
            .ThenBy(snapshot => snapshot.Task.Id.Value)
            .ToArray();
    }
}
