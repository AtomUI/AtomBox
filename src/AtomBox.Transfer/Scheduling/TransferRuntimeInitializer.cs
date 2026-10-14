using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Transfer.State;

namespace AtomBox.Transfer.Scheduling;

public sealed class TransferRuntimeInitializer
{
    private readonly ITransferTaskStore _taskStore;
    private readonly TransferStateProjector _stateProjector;

    public TransferRuntimeInitializer(
        ITransferTaskStore taskStore,
        TransferStateProjector stateProjector)
    {
        _taskStore = taskStore;
        _stateProjector = stateProjector;
    }

    public async Task<OperationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var tasksResult = await _taskStore.ListAsync(cancellationToken).ConfigureAwait(false);
        if (tasksResult.IsFailure)
        {
            _stateProjector.PublishFaulted(tasksResult.Error!);
            return OperationResult.Failure(tasksResult.Error!);
        }

        foreach (var task in tasksResult.GetValueOrThrow().Where(task => task.Status == TransferStatus.Running))
        {
            var interrupted = task.WithStatus(
                TransferStatus.Interrupted,
                DateTimeOffset.UtcNow,
                "应用上次退出时任务仍在运行，最终传输结果未知。",
                AtomBox.Core.Errors.StorageErrorCategory.Unknown,
                isRetryable: true);

            var saveResult = await _taskStore.SaveAsync(interrupted, cancellationToken).ConfigureAwait(false);
            if (saveResult.IsFailure)
            {
                _stateProjector.PublishFaulted(saveResult.Error!);
                return saveResult;
            }
        }

        var refreshResult = await _stateProjector.RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (refreshResult.IsFailure)
        {
            return refreshResult;
        }

        return OperationResult.Success();
    }
}
