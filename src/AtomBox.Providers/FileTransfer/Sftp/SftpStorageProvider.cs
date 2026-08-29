using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.FileTransfer.Sftp;

public sealed class SftpStorageProvider : IStorageProvider, IRemoteHomePathProvider
{
    private readonly ISftpClientAdapter _client;
    private readonly CredentialMaterialLease _credentialLease;
    private readonly string _rootPath;

    internal SftpStorageProvider(
        ISftpClientAdapter client,
        CredentialMaterialLease credentialLease,
        StorageCapabilitySet capabilities,
        string? rootPath)
    {
        _client = client;
        _credentialLease = credentialLease;
        Capabilities = capabilities;
        _rootPath = NormalizeRootPath(rootPath);
    }

    public StorageCapabilitySet Capabilities { get; }

    public string? HomePath
    {
        get
        {
            EnsureConnected();
            return NormalizeHomePath(_client.WorkingDirectory);
        }
    }

    public async Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            var entries = await _client.ListDirectoryAsync(ToSftpPath(path), cancellationToken).ConfigureAwait(false);
            var items = entries
                .Where(item => item.Name is not "." and not "..")
                .Where(item => item.IsDirectory || item.IsRegularFile)
                .Select(item => ToRemoteItem(path, item))
                .ToArray();

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

            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            var remotePath = ToSftpPath(path);
            if (path.Kind == RemotePathKind.Folder)
            {
                if (!await _client.DirectoryExistsAsync(remotePath, cancellationToken).ConfigureAwait(false))
                {
                    return OperationResult.Failure(StorageError.NotFound("Remote folder was not found."));
                }

                await _client.DeleteDirectoryAsync(remotePath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (!await _client.FileExistsAsync(remotePath, cancellationToken).ConfigureAwait(false))
                {
                    return OperationResult.Failure(StorageError.NotFound("Remote file was not found."));
                }

                await _client.DeleteFileAsync(remotePath, cancellationToken).ConfigureAwait(false);
            }

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
        string? temporaryPath = null;
        var committed = false;
        OperationResult operationResult;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(content);
            if (path.IsRoot)
            {
                return OperationResult.Failure(StorageError.Validation("Upload target path is required."));
            }

            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            var remotePath = ToSftpPath(path);
            await EnsureParentDirectoriesAsync(remotePath, cancellationToken).ConfigureAwait(false);
            temporaryPath = RemoteTransferTemporaryPath.CreateUploadPath(remotePath);
            var progressStream = new ProgressReadStream(content, contentLength, progress);
            await _client.UploadFileAsync(progressStream, temporaryPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await CommitUploadedFileAsync(temporaryPath, remotePath).ConfigureAwait(false);
            committed = true;
            if (contentLength is not null)
            {
                progress?.Report(new TransferProgress(contentLength.Value, contentLength.Value, null));
            }

            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            operationResult = OperationResult.Failure(ProviderErrorMapper.FromException(exception));
        }

        if (temporaryPath is null || committed)
        {
            return operationResult;
        }

        var cleanupResult = await TemporaryTransferCleanup.RunAsync(
            async cleanupToken =>
            {
                await EnsureConnectedAsync(cleanupToken).ConfigureAwait(false);
                if (await _client.FileExistsAsync(temporaryPath, cleanupToken).ConfigureAwait(false))
                {
                    await _client.DeleteFileAsync(temporaryPath, cleanupToken).ConfigureAwait(false);
                }
            },
            temporaryPath).ConfigureAwait(false);
        return TemporaryTransferCleanup.AppendWarning(operationResult, cleanupResult);
    }

    public async Task<OperationResult> CreateFolderAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.IsRoot)
            {
                return OperationResult.Failure(StorageError.Validation("Folder path is required."));
            }

            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            var remotePath = ToSftpPath(path);
            if (await _client.FileExistsAsync(remotePath, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult.Failure(new StorageError(
                    StorageErrorCode.Conflict,
                    "Remote path already exists as a file.",
                    StorageErrorCategory.Conflict));
            }

            await EnsureParentDirectoriesAsync(remotePath, cancellationToken).ConfigureAwait(false);
            if (!await _client.DirectoryExistsAsync(remotePath, cancellationToken).ConfigureAwait(false))
            {
                await _client.CreateDirectoryAsync(remotePath, cancellationToken).ConfigureAwait(false);
            }

            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    public async Task<OperationResult> MoveAsync(
        RemotePath sourcePath,
        RemotePath destinationPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourcePath.IsRoot || destinationPath.IsRoot)
            {
                return OperationResult.Failure(StorageError.Validation("Source and destination paths are required."));
            }

            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            var sourceRemotePath = ToSftpPath(sourcePath);
            var destinationRemotePath = ToSftpPath(destinationPath);
            var isFolder = sourcePath.Kind == RemotePathKind.Folder;
            if (isFolder)
            {
                if (!await _client.DirectoryExistsAsync(sourceRemotePath, cancellationToken).ConfigureAwait(false))
                {
                    return OperationResult.Failure(StorageError.NotFound("Remote folder was not found."));
                }
            }
            else if (!await _client.FileExistsAsync(sourceRemotePath, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult.Failure(StorageError.NotFound("Remote file was not found."));
            }

            if (await _client.FileExistsAsync(destinationRemotePath, cancellationToken).ConfigureAwait(false) ||
                await _client.DirectoryExistsAsync(destinationRemotePath, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult.Failure(new StorageError(
                    StorageErrorCode.Conflict,
                    "Remote destination path already exists.",
                    StorageErrorCategory.Conflict));
            }

            await EnsureParentDirectoriesAsync(destinationRemotePath, cancellationToken).ConfigureAwait(false);
            await _client.MoveAsync(sourceRemotePath, destinationRemotePath, cancellationToken).ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    public Task<OperationResult> RenameAsync(
        RemotePath path,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (path.IsRoot)
        {
            return Task.FromResult(OperationResult.Failure(StorageError.Validation("Root path cannot be renamed.")));
        }

        if (string.IsNullOrWhiteSpace(newName) ||
            newName.Contains('/', StringComparison.Ordinal) ||
            newName.Contains('\\', StringComparison.Ordinal))
        {
            return Task.FromResult(OperationResult.Failure(StorageError.Validation("New remote name must be a single path segment.")));
        }

        var parent = path.GetParent() ?? RemotePath.Root;
        return MoveAsync(path, parent.Combine(newName, path.Kind), cancellationToken);
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

            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            var remotePath = ToSftpPath(path);
            if (await _client.DirectoryExistsAsync(remotePath, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult.Failure(StorageError.Validation("Download source path is a folder."));
            }

            if (!await _client.FileExistsAsync(remotePath, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult.Failure(StorageError.NotFound("Remote file was not found."));
            }

            var progressStream = new ProgressWriteStream(destination, totalBytes: null, progress);
            await _client.DownloadFileAsync(remotePath, progressStream, cancellationToken).ConfigureAwait(false);
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

    private void EnsureConnected()
    {
        if (!_client.IsConnected)
        {
            _client.Connect();
        }
    }

    private Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        return _client.IsConnected
            ? Task.CompletedTask
            : _client.ConnectAsync(cancellationToken);
    }

    private RemoteItem ToRemoteItem(RemotePath currentPath, SftpRemoteEntry entry)
    {
        var kind = entry.IsDirectory ? RemoteItemKind.Folder : RemoteItemKind.File;
        return new RemoteItem(
            entry.Name,
            currentPath.Combine(entry.Name, kind == RemoteItemKind.Folder ? RemotePathKind.Folder : RemotePathKind.ObjectPath),
            kind,
            kind == RemoteItemKind.File ? entry.Length : null,
            new DateTimeOffset(entry.LastWriteTime));
    }

    private string ToSftpPath(RemotePath path)
    {
        if (path.IsRoot)
        {
            return _rootPath;
        }

        var relativePath = path.Value.Trim('/');
        if (_rootPath == ".")
        {
            return relativePath;
        }

        return _rootPath == "/"
            ? $"/{relativePath}"
            : $"{_rootPath}/{relativePath}";
    }

    private async Task EnsureParentDirectoriesAsync(string remotePath, CancellationToken cancellationToken)
    {
        var parent = GetParentPath(remotePath);
        if (string.IsNullOrWhiteSpace(parent) || parent == "/" ||
            await _client.DirectoryExistsAsync(parent, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await EnsureParentDirectoriesAsync(parent, cancellationToken).ConfigureAwait(false);
        await _client.CreateDirectoryAsync(parent, cancellationToken).ConfigureAwait(false);
    }

    private async Task CommitUploadedFileAsync(string temporaryPath, string targetPath)
    {
        string? backupPath = null;
        if (await _client.FileExistsAsync(targetPath, CancellationToken.None).ConfigureAwait(false))
        {
            backupPath = RemoteTransferTemporaryPath.CreateBackupPath(targetPath);
            await _client.MoveAsync(targetPath, backupPath, CancellationToken.None).ConfigureAwait(false);
        }

        try
        {
            await _client.MoveAsync(temporaryPath, targetPath, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            if (backupPath is not null)
            {
                try
                {
                    await _client.MoveAsync(backupPath, targetPath, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the original commit exception. The backup remains recoverable on the server.
                }
            }

            throw;
        }

        if (backupPath is not null)
        {
            _ = await TemporaryTransferCleanup.RunAsync(
                cleanupToken => _client.DeleteFileAsync(backupPath, cleanupToken),
                backupPath).ConfigureAwait(false);
        }
    }

    private static string? GetParentPath(string remotePath)
    {
        var normalized = remotePath.Replace('\\', '/').TrimEnd('/');
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? "/" : normalized[..index];
    }

    private static string NormalizeRootPath(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return "/";
        }

        var normalized = rootPath.Trim().Replace('\\', '/');
        if (normalized == ".")
        {
            return ".";
        }

        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = $"/{normalized}";
        }

        return normalized.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/";
    }

    private static string? NormalizeHomePath(string? homePath)
    {
        if (string.IsNullOrWhiteSpace(homePath))
        {
            return null;
        }

        var normalized = homePath.Trim().Replace('\\', '/');
        if (normalized == ".")
        {
            return normalized;
        }

        normalized = normalized.TrimEnd('/');
        return normalized.StartsWith("/", StringComparison.Ordinal)
            ? normalized
            : $"/{normalized}";
    }
}
