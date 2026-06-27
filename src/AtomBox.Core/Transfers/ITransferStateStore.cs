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
}
