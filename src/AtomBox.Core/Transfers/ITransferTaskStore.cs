using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Transfers;

public interface ITransferTaskStore
{
    Task<OperationResult<TransferTask>> GetByIdAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(
        TransferTask task,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default);
}
