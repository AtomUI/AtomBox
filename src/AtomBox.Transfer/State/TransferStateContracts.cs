using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomUI.City.State;

namespace AtomBox.Transfer.State;

public enum TransferProjectionStatus
{
    Uninitialized,
    Ready,
    Faulted,
}

public sealed record TransferProjectionFailure(
    StorageErrorCode Code,
    StorageErrorCategory Category,
    bool IsRetryable)
{
    internal static TransferProjectionFailure From(StorageError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new TransferProjectionFailure(error.Code, error.Category, error.IsRetryable);
    }
}

public sealed record TransferQueueState
{
    private TransferQueueState(
        TransferProjectionStatus status,
        IReadOnlyList<TransferStateSnapshot> tasks,
        TransferProjectionFailure? failure)
    {
        Status = status;
        Tasks = Array.AsReadOnly(tasks.ToArray());
        Failure = failure;
    }

    public TransferProjectionStatus Status { get; }

    public IReadOnlyList<TransferStateSnapshot> Tasks { get; }

    public TransferProjectionFailure? Failure { get; }

    public static TransferQueueState Uninitialized { get; } = new(
        TransferProjectionStatus.Uninitialized,
        [],
        failure: null);

    internal static TransferQueueState Ready(IReadOnlyList<TransferStateSnapshot> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new TransferQueueState(TransferProjectionStatus.Ready, tasks, failure: null);
    }

    internal static TransferQueueState Faulted(StorageError error)
    {
        return new TransferQueueState(
            TransferProjectionStatus.Faulted,
            [],
            TransferProjectionFailure.From(error));
    }
}

public interface IAtomBoxTransferState
{
    IReadOnlyState<TransferQueueState> Queue { get; }
}

public interface ITransferStateSynchronizer
{
    Task<OperationResult> RefreshAsync(CancellationToken cancellationToken = default);
}
