using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Transfers;

public sealed class TransferAppService
{
    private const int HistoryPageSize = 50;

    private readonly ITransferTaskScheduler _scheduler;
    private readonly ITransferStateStore _stateStore;
    private readonly ITransferTaskStore _taskStore;

    public TransferAppService(
        ITransferTaskScheduler scheduler,
        ITransferStateStore stateStore,
        ITransferTaskStore taskStore)
    {
        _scheduler = scheduler;
        _stateStore = stateStore;
        _taskStore = taskStore;
    }

    public Task<OperationResult<CreateTransferTasksResult>> CreateUploadTasksAsync(
        CreateUploadTasksRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCreateUploadRequest(request);
        if (validation is not null)
        {
            return Task.FromResult(OperationResult<CreateTransferTasksResult>.Failure(validation));
        }

        var tasks = request.LocalPaths
            .Select(localPath => CreateTask(
                request.StorageAccountId,
                TransferDirection.Upload,
                localPath,
                request.RemotePath,
                request.OverwritePolicy))
            .ToArray();

        return SubmitTasksAsync(tasks, cancellationToken);
    }

    public Task<OperationResult<CreateTransferTasksResult>> CreateBatchUploadTasksAsync(
        CreateBatchUploadTasksRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCreateBatchUploadRequest(request);
        if (validation is not null)
        {
            return Task.FromResult(OperationResult<CreateTransferTasksResult>.Failure(validation));
        }

        var tasks = request.Targets
            .Select(target => CreateTask(
                request.StorageAccountId,
                TransferDirection.Upload,
                target.LocalPath,
                target.RemotePath,
                request.OverwritePolicy))
            .ToArray();

        return SubmitTasksAsync(tasks, cancellationToken);
    }

    public Task<OperationResult<CreateTransferTasksResult>> CreateDownloadTasksAsync(
        CreateDownloadTasksRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCreateDownloadRequest(request);
        if (validation is not null)
        {
            return Task.FromResult(OperationResult<CreateTransferTasksResult>.Failure(validation));
        }

        var tasks = request.RemotePaths
            .Select(remotePath => CreateTask(
                request.StorageAccountId,
                TransferDirection.Download,
                request.LocalPath,
                remotePath,
                request.OverwritePolicy))
            .ToArray();

        return SubmitTasksAsync(tasks, cancellationToken);
    }

    public async Task<OperationResult<TransferQueueSnapshot>> GetQueueAsync(
        GetTransferQueueRequest request,
        CancellationToken cancellationToken = default)
    {
        var queueResult = await _stateStore.ListQueueAsync(cancellationToken).ConfigureAwait(false);
        return queueResult.IsFailure
            ? OperationResult<TransferQueueSnapshot>.Failure(queueResult.Error!)
            : OperationResult<TransferQueueSnapshot>.Success(new TransferQueueSnapshot(queueResult.GetValueOrThrow()));
    }

    public async Task<OperationResult<TransferHistoryPage>> GetHistoryAsync(
        GetTransferHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageIndex = Math.Max(request.PageIndex, 1);
        var historyResult = await _stateStore.ListHistoryAsync((pageIndex - 1) * HistoryPageSize, HistoryPageSize, cancellationToken)
            .ConfigureAwait(false);

        return historyResult.IsFailure
            ? OperationResult<TransferHistoryPage>.Failure(historyResult.Error!)
            : OperationResult<TransferHistoryPage>.Success(new TransferHistoryPage(pageIndex, historyResult.GetValueOrThrow()));
    }

    public Task<OperationResult> CancelAsync(
        CancelTransferTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.TaskId.IsEmpty)
        {
            return Task.FromResult(OperationResult.Failure(StorageError.Validation("Transfer task id is required.")));
        }

        return _scheduler.CancelAsync(request.TaskId, cancellationToken);
    }

    public async Task<OperationResult> RetryAsync(
        RetryTransferTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.TaskId.IsEmpty)
        {
            return OperationResult.Failure(StorageError.Validation("Transfer task id is required."));
        }

        var retryResult = await _scheduler.RetryAsync(request.TaskId, cancellationToken).ConfigureAwait(false);
        if (retryResult.IsFailure)
        {
            return retryResult;
        }

        return await _scheduler.WakeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> ClearHistoryAsync(
        ClearTransferHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var historyResult = await _stateStore.ListHistoryAsync(0, int.MaxValue, cancellationToken)
            .ConfigureAwait(false);
        if (historyResult.IsFailure)
        {
            return OperationResult.Failure(historyResult.Error!);
        }

        foreach (var snapshot in historyResult.GetValueOrThrow())
        {
            var deleteResult = await _taskStore.DeleteAsync(snapshot.Task.Id, cancellationToken).ConfigureAwait(false);
            if (deleteResult.IsFailure)
            {
                return deleteResult;
            }
        }

        return OperationResult.Success();
    }

    private async Task<OperationResult<CreateTransferTasksResult>> SubmitTasksAsync(
        IReadOnlyList<TransferTask> tasks,
        CancellationToken cancellationToken)
    {
        foreach (var task in tasks)
        {
            var submitResult = await _scheduler.SubmitAsync(task, cancellationToken).ConfigureAwait(false);
            if (submitResult.IsFailure)
            {
                return OperationResult<CreateTransferTasksResult>.Failure(submitResult.Error!);
            }
        }

        var wakeResult = await _scheduler.WakeAsync(cancellationToken).ConfigureAwait(false);
        if (wakeResult.IsFailure)
        {
            return OperationResult<CreateTransferTasksResult>.Failure(wakeResult.Error!);
        }

        return OperationResult<CreateTransferTasksResult>.Success(new CreateTransferTasksResult(tasks));
    }

    private static TransferTask CreateTask(
        StorageAccountId storageAccountId,
        TransferDirection direction,
        LocalPath localPath,
        RemotePath remotePath,
        TransferOverwritePolicy overwritePolicy)
    {
        var now = DateTimeOffset.UtcNow;
        return new TransferTask(
            TransferTaskId.New(),
            storageAccountId,
            direction,
            localPath,
            remotePath,
            TransferStatus.Pending,
            new TransferOptions(overwritePolicy),
            now,
            now);
    }

    private static StorageError? ValidateCreateUploadRequest(CreateUploadTasksRequest request)
    {
        if (request.StorageAccountId.IsEmpty)
        {
            return StorageError.Validation("Storage account id is required.");
        }

        if (request.LocalPaths is null || request.LocalPaths.Count == 0)
        {
            return StorageError.Validation("At least one local path is required.");
        }

        return request.LocalPaths.Any(path => path.IsEmpty)
            ? StorageError.Validation("Local path cannot be empty.")
            : null;
    }

    private static StorageError? ValidateCreateBatchUploadRequest(CreateBatchUploadTasksRequest request)
    {
        if (request.StorageAccountId.IsEmpty)
        {
            return StorageError.Validation("Storage account id is required.");
        }

        if (request.Targets is null || request.Targets.Count == 0)
        {
            return StorageError.Validation("At least one upload target is required.");
        }

        foreach (var target in request.Targets)
        {
            if (target.LocalPath.IsEmpty)
            {
                return StorageError.Validation("Local path cannot be empty.");
            }

            if (target.RemotePath.IsRoot)
            {
                return StorageError.Validation("Remote upload path is required.");
            }
        }

        return null;
    }

    private static StorageError? ValidateCreateDownloadRequest(CreateDownloadTasksRequest request)
    {
        if (request.StorageAccountId.IsEmpty)
        {
            return StorageError.Validation("Storage account id is required.");
        }

        if (request.LocalPath.IsEmpty)
        {
            return StorageError.Validation("Local path is required.");
        }

        if (request.RemotePaths is null || request.RemotePaths.Count == 0)
        {
            return StorageError.Validation("At least one remote path is required.");
        }

        return request.RemotePaths.Any(path => path.IsRoot)
            ? StorageError.Validation("Remote file path is required.")
            : null;
    }
}
