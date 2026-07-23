using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Core.Fingerprints;

namespace AtomBox.Application.Transfers;

public sealed record CreateUploadTasksRequest(
    StorageAccountId StorageAccountId,
    IReadOnlyList<LocalPath> LocalPaths,
    RemotePath RemotePath,
    TransferOverwritePolicy OverwritePolicy);

public sealed record UploadTaskFingerprint(
    string HashAlgorithm,
    string HashValue,
    long FileSize,
    DateTimeOffset CalculatedAt);

public sealed record UploadTaskTarget(
    LocalPath LocalPath,
    RemotePath RemotePath,
    UploadTaskFingerprint? Fingerprint = null);

public sealed record CreateBatchUploadTasksRequest(
    StorageAccountId StorageAccountId,
    IReadOnlyList<UploadTaskTarget> Targets,
    TransferOverwritePolicy OverwritePolicy);

public sealed record PrepareBatchUploadTasksRequest(
    StorageAccountId StorageAccountId,
    IReadOnlyList<UploadTaskTarget> Targets);

public sealed record UploadPreparationProgress(
    int CurrentIndex,
    int TotalCount,
    string FileName);

public sealed record PreparedUploadTaskTarget(
    LocalPath LocalPath,
    RemotePath RemotePath,
    UploadTaskFingerprint? Fingerprint,
    IReadOnlyList<FileFingerprintRecord> HistoricalRecords);

public sealed record PrepareBatchUploadTasksResult(
    bool IsFingerprintIndexEnabled,
    IReadOnlyList<PreparedUploadTaskTarget> Targets);

public sealed record DownloadTaskTarget(RemotePath RemotePath, LocalPath LocalPath);

public sealed record CreateDownloadTasksRequest(
    StorageAccountId StorageAccountId,
    IReadOnlyList<DownloadTaskTarget> Targets,
    TransferOverwritePolicy OverwritePolicy)
{
    public CreateDownloadTasksRequest(
        StorageAccountId storageAccountId,
        IReadOnlyList<RemotePath> remotePaths,
        LocalPath localPath,
        TransferOverwritePolicy overwritePolicy)
        : this(
            storageAccountId,
            remotePaths is null
                ? null!
                : remotePaths.Select(path => new DownloadTaskTarget(path, localPath)).ToArray(),
            overwritePolicy)
    {
    }
}

public sealed record CreateTransferTasksResult(IReadOnlyList<TransferTask> Tasks);

public sealed record GetTransferQueueRequest;

public sealed record TransferQueueSnapshot(IReadOnlyList<TransferStateSnapshot> Tasks);

public sealed record GetTransferHistoryRequest(int PageIndex = 1);

public sealed record TransferHistoryPage(int PageIndex, IReadOnlyList<TransferStateSnapshot> Tasks);

public sealed record CancelTransferTaskRequest(TransferTaskId TaskId);

public sealed record RetryTransferTaskRequest(TransferTaskId TaskId);

public sealed record ClearTransferHistoryRequest;
