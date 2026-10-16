using AtomBox.Core.Errors;
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

    Task<OperationResult<TransferTask>> CompareExchangeAsync(
        TransferTask replacement,
        long expectedRevision,
        TransferStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<TransferTask>.Failure(
            StorageError.NotSupported("Conditional transfer task updates are not supported by this store.")));
    }

    Task<OperationResult> DeleteAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default);
}
