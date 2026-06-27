using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.Common;

public sealed class FakeStorageProvider : IStorageProvider
{
    private readonly ProviderDescriptor _descriptor;

    public FakeStorageProvider(ProviderDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public StorageCapabilitySet Capabilities => _descriptor.Capabilities;

    public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        var items = _descriptor.Category switch
        {
            StorageProviderCategory.ObjectStorage when path.IsRoot => CreateBucketList(),
            StorageProviderCategory.FileTransfer => CreateFolderList(path),
            StorageProviderCategory.NetDisk => CreateFolderList(path),
            _ => CreateObjectList(path)
        };

        return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Success(items));
    }

    public Task<OperationResult> DeleteAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        if (path.IsRoot)
        {
            return Task.FromResult(OperationResult.Failure(StorageError.Validation("Root path cannot be deleted.")));
        }

        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> CreateFolderAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        if (path.IsRoot)
        {
            return Task.FromResult(OperationResult.Failure(StorageError.Validation("Folder path is required.")));
        }

        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> MoveAsync(
        RemotePath sourcePath,
        RemotePath destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (sourcePath.IsRoot || destinationPath.IsRoot)
        {
            return Task.FromResult(OperationResult.Failure(StorageError.Validation("Source and destination paths are required.")));
        }

        return Task.FromResult(OperationResult.Success());
    }

    public async Task<OperationResult> UploadAsync(
        RemotePath path,
        Stream content,
        long? contentLength,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (path.IsRoot)
        {
            return OperationResult.Failure(StorageError.Validation("Upload target path is required."));
        }

        var buffer = new byte[81920];
        long transferred = 0;
        while (true)
        {
            var read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            transferred += read;
            progress?.Report(new TransferProgress(transferred, contentLength, null));
        }

        progress?.Report(new TransferProgress(contentLength ?? transferred, contentLength ?? transferred, null));
        return OperationResult.Success();
    }

    public async Task<OperationResult> DownloadAsync(
        RemotePath path,
        Stream destination,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (path.IsRoot)
        {
            return OperationResult.Failure(StorageError.Validation("Download source path is required."));
        }

        var payload = new byte[] { 65, 116, 111, 109, 66, 111, 120 };
        await destination.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        progress?.Report(new TransferProgress(payload.Length, payload.Length, null));
        return OperationResult.Success();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private static IReadOnlyList<RemoteItem> CreateBucketList()
    {
        return
        [
            new RemoteItem("production-bucket", new RemotePath("production-bucket", RemotePathKind.BucketRoot), RemoteItemKind.Bucket, null, null),
            new RemoteItem("archive-bucket", new RemotePath("archive-bucket", RemotePathKind.BucketRoot), RemoteItemKind.Bucket, null, null)
        ];
    }

    private static IReadOnlyList<RemoteItem> CreateObjectList(RemotePath path)
    {
        return
        [
            new RemoteItem("documents", path.Combine("documents", RemotePathKind.Folder), RemoteItemKind.Folder, null, DateTimeOffset.UtcNow.AddDays(-2)),
            new RemoteItem("sample-object.txt", path.Combine("sample-object.txt"), RemoteItemKind.File, 1280, DateTimeOffset.UtcNow.AddHours(-4), "fake-etag", "text/plain")
        ];
    }

    private static IReadOnlyList<RemoteItem> CreateFolderList(RemotePath path)
    {
        return
        [
            new RemoteItem("documents", path.Combine("documents", RemotePathKind.Folder), RemoteItemKind.Folder, null, DateTimeOffset.UtcNow.AddDays(-2)),
            new RemoteItem("sample-file.txt", path.Combine("sample-file.txt"), RemoteItemKind.File, 2048, DateTimeOffset.UtcNow.AddHours(-3), contentType: "text/plain")
        ];
    }
}
