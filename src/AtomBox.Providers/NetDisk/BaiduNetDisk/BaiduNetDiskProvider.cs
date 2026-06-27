using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.NetDisk.BaiduNetDisk;

public sealed class BaiduNetDiskProvider : IStorageProvider
{
    private readonly IBaiduNetDiskClient _client;
    private readonly CredentialMaterialLease _credentialLease;
    private readonly string _rootPath;

    internal BaiduNetDiskProvider(
        IBaiduNetDiskClient client,
        CredentialMaterialLease credentialLease,
        StorageCapabilitySet capabilities,
        string rootPath)
    {
        _client = client;
        _credentialLease = credentialLease;
        Capabilities = capabilities;
        _rootPath = BaiduNetDiskPath.NormalizeDirectoryRoot(rootPath);
    }

    public StorageCapabilitySet Capabilities { get; }

    public async Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryPath = BaiduNetDiskPath.ResolvePath(path, _rootPath);
            var files = await _client.ListAsync(directoryPath, cancellationToken).ConfigureAwait(false);
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
            if (path.IsRoot)
            {
                return OperationResult.Failure(StorageError.Validation("Root path cannot be deleted."));
            }

            await _client.DeleteAsync(BaiduNetDiskPath.ResolvePath(path, _rootPath), cancellationToken)
                .ConfigureAwait(false);
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

            var resolvedPathResult = BaiduNetDiskPath.ResolveUploadPath(path, _rootPath);
            if (resolvedPathResult.IsFailure)
            {
                return OperationResult.Failure(resolvedPathResult.Error!);
            }

            var progressAdapter = new Progress<long>(transferred =>
                progress?.Report(new TransferProgress(transferred, contentLength, null)));
            await _client.UploadAsync(
                resolvedPathResult.GetValueOrThrow(),
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
                return OperationResult.Failure(StorageError.Validation("Download source path is required."));
            }

            using var remoteObject = await _client.GetObjectAsync(
                BaiduNetDiskPath.ResolvePath(path, _rootPath),
                cancellationToken).ConfigureAwait(false);
            await CopyToAsync(
                remoteObject.Content,
                destination,
                remoteObject.ContentLength,
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

    private static RemoteItem ToRemoteItem(BaiduNetDiskFileItem item)
    {
        var kind = item.IsFolder ? RemoteItemKind.Folder : RemoteItemKind.File;
        return new RemoteItem(
            item.Name,
            BaiduNetDiskPath.ToRemotePath(item.Path, item.IsFolder),
            kind,
            kind == RemoteItemKind.File ? item.Size : null,
            item.UpdatedAt);
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
