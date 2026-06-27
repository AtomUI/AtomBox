using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.NetDisk.AliyunDrive;

public sealed class AliyunDriveProvider : IStorageProvider
{
    private readonly IAliyunDriveClient _client;
    private readonly CredentialMaterialLease _credentialLease;
    private readonly string _driveId;
    private readonly string _rootFileId;

    internal AliyunDriveProvider(
        IAliyunDriveClient client,
        CredentialMaterialLease credentialLease,
        StorageCapabilitySet capabilities,
        string driveId,
        string rootFileId)
    {
        _client = client;
        _credentialLease = credentialLease;
        Capabilities = capabilities;
        _driveId = driveId;
        _rootFileId = rootFileId;
    }

    public StorageCapabilitySet Capabilities { get; }

    public async Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parentFileId = path.IsRoot ? _rootFileId : path.Value.Trim('/');
            if (string.IsNullOrWhiteSpace(parentFileId))
            {
                return OperationResult<IReadOnlyList<RemoteItem>>.Failure(
                    StorageError.Validation("Parent file id is required."));
            }

            var files = await _client.ListAsync(_driveId, parentFileId, cancellationToken)
                .ConfigureAwait(false);
            var items = files.Select(ToRemoteItem).ToArray();
            return OperationResult<IReadOnlyList<RemoteItem>>.Success(items);
        }
        catch (Exception exception)
        {
            return OperationResult<IReadOnlyList<RemoteItem>>.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    public async Task<OperationResult> DeleteAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.IsRoot || string.Equals(path.Value.Trim('/'), _rootFileId, StringComparison.Ordinal))
            {
                return OperationResult.Failure(StorageError.Validation("Root path cannot be deleted."));
            }

            await _client.DeleteAsync(_driveId, path.Value.Trim('/'), cancellationToken).ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    public async Task<OperationResult> UploadAsync(
        RemotePath path,
        Stream content,
        long? contentLength,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(content);

            var uploadPathResult = AliyunDriveUploadPath.FromRemotePath(path, _rootFileId);
            if (uploadPathResult.IsFailure)
            {
                return OperationResult.Failure(uploadPathResult.Error!);
            }

            var uploadPath = uploadPathResult.GetValueOrThrow();
            var progressAdapter = new Progress<long>(transferred =>
                progress?.Report(new TransferProgress(transferred, contentLength, null)));
            await _client.UploadAsync(
                _driveId,
                uploadPath.ParentFileId,
                uploadPath.Name,
                content,
                contentLength,
                progressAdapter,
                cancellationToken).ConfigureAwait(false);
            if (contentLength is not null)
            {
                progress?.Report(new TransferProgress(contentLength.Value, contentLength.Value, null));
            }

            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    public async Task<OperationResult> DownloadAsync(
        RemotePath path,
        Stream destination,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(destination);
            if (path.IsRoot)
            {
                return OperationResult.Failure(StorageError.Validation("Download source file id is required."));
            }

            using var driveObject = await _client.GetObjectAsync(_driveId, path.Value.Trim('/'), cancellationToken)
                .ConfigureAwait(false);
            await CopyToAsync(
                driveObject.Content,
                destination,
                driveObject.ContentLength,
                progress,
                cancellationToken).ConfigureAwait(false);

            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _credentialLease.DisposeAsync().ConfigureAwait(false);
    }

    private static RemoteItem ToRemoteItem(AliyunDriveFileItem item)
    {
        var kind = item.IsFolder ? RemoteItemKind.Folder : RemoteItemKind.File;
        return new RemoteItem(
            item.Name,
            new RemotePath(item.FileId, kind == RemoteItemKind.Folder ? RemotePathKind.Folder : RemotePathKind.ObjectPath),
            kind,
            kind == RemoteItemKind.File ? item.Size : null,
            item.UpdatedAt,
            eTag: null,
            item.ContentType);
    }

    private static async Task CopyToAsync(
        Stream source,
        Stream destination,
        long? totalBytes,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long transferred = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            transferred += read;
            progress?.Report(new TransferProgress(transferred, totalBytes, null));
        }

        progress?.Report(new TransferProgress(totalBytes ?? transferred, totalBytes ?? transferred, null));
    }
}
