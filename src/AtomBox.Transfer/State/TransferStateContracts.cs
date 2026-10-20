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
        TransferProjectionFailure? failure,
        bool isStale,
        DateTimeOffset? lastSuccessfulRefreshAt)
    {
        Status = status;
        Tasks = Array.AsReadOnly(tasks.ToArray());
        Failure = failure;
        IsStale = isStale;
        LastSuccessfulRefreshAt = lastSuccessfulRefreshAt;
    }

    public TransferProjectionStatus Status { get; }

    public IReadOnlyList<TransferStateSnapshot> Tasks { get; }

    public TransferProjectionFailure? Failure { get; }

    public bool IsStale { get; }

    public DateTimeOffset? LastSuccessfulRefreshAt { get; }

    public static TransferQueueState Uninitialized { get; } = new(
        TransferProjectionStatus.Uninitialized,
        [],
        failure: null,
        isStale: false,
        lastSuccessfulRefreshAt: null);

    internal static TransferQueueState Ready(IReadOnlyList<TransferStateSnapshot> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return new TransferQueueState(
            TransferProjectionStatus.Ready,
            tasks,
            failure: null,
            isStale: false,
            lastSuccessfulRefreshAt: DateTimeOffset.UtcNow);
    }

    internal static TransferQueueState Faulted(StorageError error, TransferQueueState? lastKnown = null)
    {
        return new TransferQueueState(
            TransferProjectionStatus.Faulted,
            lastKnown?.Tasks ?? [],
            TransferProjectionFailure.From(error),
            isStale: lastKnown?.LastSuccessfulRefreshAt is not null,
            lastKnown?.LastSuccessfulRefreshAt);
    }
}

public enum TransferRuntimeLifecycleState
{
    Created = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4,
    Faulted = 5,
}

public enum TransferRuntimeHealthState
{
    Healthy = 0,
    Degraded = 1,
    Faulted = 2,
}

public sealed record TransferRuntimeSnapshot(
    TransferRuntimeLifecycleState Lifecycle,
    TransferRuntimeHealthState Health,
    bool IsAcceptingSubmissions,
    int ActiveTaskCount,
    int PendingTaskCount,
    TransferProjectionFailure? LastFailure,
    DateTimeOffset UpdatedAt)
{
    public static TransferRuntimeSnapshot Initial { get; } = new(
        TransferRuntimeLifecycleState.Created,
        TransferRuntimeHealthState.Healthy,
        IsAcceptingSubmissions: false,
        ActiveTaskCount: 0,
        PendingTaskCount: 0,
        LastFailure: null,
        UpdatedAt: DateTimeOffset.MinValue);
}

public interface IAtomBoxTransferState
{
    IReadOnlyState<TransferQueueState> Queue { get; }

    IReadOnlyState<TransferRuntimeSnapshot> Runtime { get; }
}

public interface ITransferStateSynchronizer
{
    Task<OperationResult> RefreshAsync(CancellationToken cancellationToken = default);
}
