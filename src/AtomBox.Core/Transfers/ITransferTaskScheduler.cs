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

    Task<OperationResult> WakeAsync(
        CancellationToken cancellationToken = default);
}
