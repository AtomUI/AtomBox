using AtomBox.Core.Results;
using AtomBox.Core.Transfers;

namespace AtomBox.Transfer.State;

internal sealed class ProjectingTransferStateStore : ITransferStateStore
{
    private readonly ITransferStateStore _inner;
    private readonly TransferStateProjector _projector;

    public ProjectingTransferStateStore(
        ITransferStateStore inner,
        TransferStateProjector projector)
    {
        _inner = inner;
        _projector = projector;
    }

    public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(
        CancellationToken cancellationToken = default)
    {
        return _inner.ListQueueAsync(cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        return _inner.ListHistoryAsync(skip, take, cancellationToken);
    }

    public async Task<OperationResult> UpdateStatusAsync(
        TransferTask task,
        TransferProgress? progress,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner
            .UpdateStatusAsync(task, progress, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            _projector.PublishPersisted(task, progress);
        }

        return result;
    }
}
