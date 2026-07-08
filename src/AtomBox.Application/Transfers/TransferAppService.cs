using AtomBox.Core.Errors;
using AtomBox.Core.Fingerprints;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using System.Security.Cryptography;

namespace AtomBox.Application.Transfers;

public sealed class TransferAppService
{
    private const int HistoryPageSize = 50;

    private readonly ITransferTaskScheduler _scheduler;
    private readonly ITransferStateStore _stateStore;
    private readonly ITransferTaskStore _taskStore;
    private readonly IApplicationSettingsRepository? _settings;
    private readonly ILocalTransferFileStore? _localFiles;
    private readonly IFileFingerprintIndexStore? _fingerprints;

    public TransferAppService(
        ITransferTaskScheduler scheduler,
        ITransferStateStore stateStore,
        ITransferTaskStore taskStore,
        IApplicationSettingsRepository? settings = null,
        ILocalTransferFileStore? localFiles = null,
        IFileFingerprintIndexStore? fingerprints = null)
    {
        _scheduler = scheduler;
        _stateStore = stateStore;
        _taskStore = taskStore;
        _settings = settings;
        _localFiles = localFiles;
        _fingerprints = fingerprints;
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
                request.OverwritePolicy,
                target.Fingerprint))
            .ToArray();

        return SubmitTasksAsync(tasks, cancellationToken);
    }

    public async Task<OperationResult<PrepareBatchUploadTasksResult>> PrepareBatchUploadTasksAsync(
        PrepareBatchUploadTasksRequest request,
        IProgress<UploadPreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidatePrepareBatchUploadRequest(request);
        if (validation is not null)
        {
            return OperationResult<PrepareBatchUploadTasksResult>.Failure(validation);
        }

        if (_settings is null || _localFiles is null || _fingerprints is null)
        {
            return OperationResult<PrepareBatchUploadTasksResult>.Success(
                new PrepareBatchUploadTasksResult(false, ToPreparedTargetsWithoutFingerprint(request.Targets)));
        }

        var settingsResult = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (settingsResult.IsFailure)
        {
            return OperationResult<PrepareBatchUploadTasksResult>.Failure(settingsResult.Error!);
        }

        if (!settingsResult.GetValueOrThrow().EnableUploadFingerprintIndex)
        {
            return OperationResult<PrepareBatchUploadTasksResult>.Success(
                new PrepareBatchUploadTasksResult(false, ToPreparedTargetsWithoutFingerprint(request.Targets)));
        }

        var prepared = new List<PreparedUploadTaskTarget>(request.Targets.Count);
        for (var index = 0; index < request.Targets.Count; index++)
        {
            var target = request.Targets[index];
            progress?.Report(new UploadPreparationProgress(
                index + 1,
                request.Targets.Count,
                target.LocalPath.GetFileName()));

            var fingerprintResult = await CalculateFingerprintAsync(target.LocalPath, cancellationToken)
                .ConfigureAwait(false);
            if (fingerprintResult.IsFailure)
            {
                return OperationResult<PrepareBatchUploadTasksResult>.Failure(fingerprintResult.Error!);
            }

            var fingerprint = fingerprintResult.GetValueOrThrow();
            var historicalResult = await _fingerprints
                .FindAsync(
                    new FileFingerprintQuery(
                        fingerprint.HashAlgorithm,
                        fingerprint.HashValue,
                        fingerprint.FileSize,
                        request.StorageAccountId),
                    cancellationToken)
                .ConfigureAwait(false);
            if (historicalResult.IsFailure)
            {
                return OperationResult<PrepareBatchUploadTasksResult>.Failure(historicalResult.Error!);
            }

            prepared.Add(new PreparedUploadTaskTarget(
                target.LocalPath,
                target.RemotePath,
                fingerprint,
                historicalResult.GetValueOrThrow()));
        }

        return OperationResult<PrepareBatchUploadTasksResult>.Success(
            new PrepareBatchUploadTasksResult(true, prepared));
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
        TransferOverwritePolicy overwritePolicy,
        UploadTaskFingerprint? fingerprint = null)
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
            now,
            FingerprintHashAlgorithm: fingerprint?.HashAlgorithm,
            FingerprintHashValue: fingerprint?.HashValue,
            FingerprintFileSize: fingerprint?.FileSize,
            FingerprintCalculatedAt: fingerprint?.CalculatedAt);
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

    private static StorageError? ValidatePrepareBatchUploadRequest(PrepareBatchUploadTasksRequest request)
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

    private async Task<OperationResult<UploadTaskFingerprint>> CalculateFingerprintAsync(
        LocalPath localPath,
        CancellationToken cancellationToken)
    {
        if (_localFiles is null)
        {
            return OperationResult<UploadTaskFingerprint>.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                "Local file store is not available.",
                StorageErrorCategory.Infrastructure));
        }

        var fileResult = await _localFiles.OpenReadAsync(localPath, cancellationToken).ConfigureAwait(false);
        if (fileResult.IsFailure)
        {
            return OperationResult<UploadTaskFingerprint>.Failure(fileResult.Error!);
        }

        await using var file = fileResult.GetValueOrThrow();
        if (file.Length is not { } length)
        {
            return OperationResult<UploadTaskFingerprint>.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                "Local file length is not available.",
                StorageErrorCategory.Infrastructure));
        }

        var hash = await SHA256.HashDataAsync(file.Stream, cancellationToken).ConfigureAwait(false);
        return OperationResult<UploadTaskFingerprint>.Success(
            new UploadTaskFingerprint(
                "sha256",
                Convert.ToHexString(hash).ToLowerInvariant(),
                length,
                DateTimeOffset.UtcNow));
    }

    private static IReadOnlyList<PreparedUploadTaskTarget> ToPreparedTargetsWithoutFingerprint(
        IReadOnlyList<UploadTaskTarget> targets)
    {
        return targets
            .Select(target => new PreparedUploadTaskTarget(
                target.LocalPath,
                target.RemotePath,
                target.Fingerprint,
                Array.Empty<FileFingerprintRecord>()))
            .ToArray();
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
