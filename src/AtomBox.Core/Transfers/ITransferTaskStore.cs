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

    async Task<OperationResult<bool>> TryTransitionStatusAsync(
        TransferTaskId taskId,
        TransferStatus expectedStatus,
        TransferStatus targetStatus,
        DateTimeOffset updatedAt,
        string? statusReason = null,
        StorageErrorCategory? errorCategory = null,
        bool isRetryable = false,
        CancellationToken cancellationToken = default)
    {
        var currentResult = await GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (currentResult.IsFailure)
        {
            return OperationResult<bool>.Failure(currentResult.Error!);
        }

        var current = currentResult.GetValueOrThrow();
        if (current.Status != expectedStatus)
        {
            return OperationResult<bool>.Success(false);
        }

        var saveResult = await SaveAsync(
            current.WithStatus(targetStatus, updatedAt, statusReason, errorCategory, isRetryable),
            cancellationToken).ConfigureAwait(false);
        return saveResult.IsFailure
            ? OperationResult<bool>.Failure(saveResult.Error!)
            : OperationResult<bool>.Success(true);
    }

    Task<OperationResult> DeleteAsync(
        TransferTaskId taskId,
        CancellationToken cancellationToken = default);
}
