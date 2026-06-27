using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Transfers;

public sealed record CreateUploadTasksRequest(
    StorageAccountId StorageAccountId,
    IReadOnlyList<LocalPath> LocalPaths,
    RemotePath RemotePath,
    TransferOverwritePolicy OverwritePolicy);

public sealed record UploadTaskTarget(LocalPath LocalPath, RemotePath RemotePath);

public sealed record CreateBatchUploadTasksRequest(
    StorageAccountId StorageAccountId,
    IReadOnlyList<UploadTaskTarget> Targets,
    TransferOverwritePolicy OverwritePolicy);

public sealed record CreateDownloadTasksRequest(
    StorageAccountId StorageAccountId,
    IReadOnlyList<RemotePath> RemotePaths,
    LocalPath LocalPath,
    TransferOverwritePolicy OverwritePolicy);

public sealed record CreateTransferTasksResult(IReadOnlyList<TransferTask> Tasks);

public sealed record GetTransferQueueRequest;

public sealed record TransferQueueSnapshot(IReadOnlyList<TransferStateSnapshot> Tasks);

public sealed record GetTransferHistoryRequest(int PageIndex = 1);

public sealed record TransferHistoryPage(int PageIndex, IReadOnlyList<TransferStateSnapshot> Tasks);

public sealed record CancelTransferTaskRequest(TransferTaskId TaskId);

public sealed record RetryTransferTaskRequest(TransferTaskId TaskId);

public sealed record ClearTransferHistoryRequest;
