using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Transfers;

public interface ITransferTaskScheduler
{
    Task<OperationResult> SubmitAsync(
        TransferTask task,
        CancellationToken cancellationToken = default);

    Task<OperationResult> CancelAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RetryAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> AbandonAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Failure(
            AtomBox.Core.Errors.StorageError.NotSupported(
                "Abandoning interrupted transfer tasks is not supported by this scheduler.")));
    }
}
