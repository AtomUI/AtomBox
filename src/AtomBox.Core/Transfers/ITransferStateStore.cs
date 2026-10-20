using AtomBox.Core.Results;

namespace AtomBox.Core.Transfers;

public interface ITransferStateStore
{
    Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateStatusAsync(
        TransferTask task,
        TransferProgress? progress,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SaveProgressBatchAsync(
        IReadOnlyList<TransferProgressUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Failure(
            AtomBox.Core.Errors.StorageError.NotSupported(
                "Batch transfer progress updates are not supported by this store.")));
    }
}
