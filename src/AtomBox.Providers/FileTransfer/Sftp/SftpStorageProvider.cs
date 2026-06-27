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

    public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureConnected();

            var items = _client.ListDirectory(ToSftpPath(path))
                .Where(item => item.Name is not "." and not "..")
                .Where(item => item.IsDirectory || item.IsRegularFile)
                .Select(item => ToRemoteItem(path, item))
                .ToArray();

            return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Success(items));
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Failure(
                ProviderErrorMapper.FromException(exception)));
        }
    }

    public Task<OperationResult> DeleteAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.IsRoot)
            {
                return Task.FromResult(OperationResult.Failure(StorageError.Validation("Root path cannot be deleted.")));
            }

            EnsureConnected();
            var remotePath = ToSftpPath(path);
            if (path.Kind == RemotePathKind.Folder)
            {
                if (!_client.DirectoryExists(remotePath))
                {
                    return Task.FromResult(OperationResult.Failure(StorageError.NotFound("Remote folder was not found.")));
                }

                _client.DeleteDirectory(remotePath);
            }
            else
            {
                if (!_client.FileExists(remotePath))
                {
                    return Task.FromResult(OperationResult.Failure(StorageError.NotFound("Remote file was not found.")));
                }

                _client.DeleteFile(remotePath);
            }

            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult.Failure(ProviderErrorMapper.FromException(exception)));
        }
    }

    public Task<OperationResult> UploadAsync(
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
            if (path.IsRoot)
            {
                return Task.FromResult(OperationResult.Failure(StorageError.Validation("Upload target path is required.")));
            }

            EnsureConnected();
            var remotePath = ToSftpPath(path);
            EnsureParentDirectories(remotePath);
            _client.UploadFile(content, remotePath, bytes =>
                progress?.Report(new TransferProgress((long)bytes, contentLength, null)));
            if (contentLength is not null)
            {
                progress?.Report(new TransferProgress(contentLength.Value, contentLength.Value, null));
            }

            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult.Failure(ProviderErrorMapper.FromException(exception)));
        }
    }

    public Task<OperationResult> CreateFolderAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.IsRoot)
            {
                return Task.FromResult(OperationResult.Failure(StorageError.Validation("Folder path is required.")));
            }

            EnsureConnected();
            var remotePath = ToSftpPath(path);
            if (_client.FileExists(remotePath))
            {
                return Task.FromResult(OperationResult.Failure(new StorageError(
                    StorageErrorCode.Conflict,
                    "Remote path already exists as a file.",
                    StorageErrorCategory.Conflict)));
            }

            EnsureParentDirectories(remotePath);
            if (!_client.DirectoryExists(remotePath))
            {
                _client.CreateDirectory(remotePath);
            }

            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult.Failure(ProviderErrorMapper.FromException(exception)));
        }
    }

    public Task<OperationResult> MoveAsync(
        RemotePath sourcePath,
        RemotePath destinationPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourcePath.IsRoot || destinationPath.IsRoot)
            {
                return Task.FromResult(OperationResult.Failure(StorageError.Validation("Source and destination paths are required.")));
            }

            EnsureConnected();
            var sourceRemotePath = ToSftpPath(sourcePath);
            var destinationRemotePath = ToSftpPath(destinationPath);
            var isFolder = sourcePath.Kind == RemotePathKind.Folder;
            if (isFolder)
            {
                if (!_client.DirectoryExists(sourceRemotePath))
                {
                    return Task.FromResult(OperationResult.Failure(StorageError.NotFound("Remote folder was not found.")));
                }
            }
            else if (!_client.FileExists(sourceRemotePath))
            {
                return Task.FromResult(OperationResult.Failure(StorageError.NotFound("Remote file was not found.")));
            }

            if (_client.FileExists(destinationRemotePath) || _client.DirectoryExists(destinationRemotePath))
            {
                return Task.FromResult(OperationResult.Failure(new StorageError(
                    StorageErrorCode.Conflict,
                    "Remote destination path already exists.",
                    StorageErrorCategory.Conflict)));
            }

            EnsureParentDirectories(destinationRemotePath);
            _client.Move(sourceRemotePath, destinationRemotePath);
            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult.Failure(ProviderErrorMapper.FromException(exception)));
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

    public Task<OperationResult> DownloadAsync(
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
                return Task.FromResult(OperationResult.Failure(StorageError.Validation("Download source path is required.")));
            }

            EnsureConnected();
            var remotePath = ToSftpPath(path);
            if (_client.DirectoryExists(remotePath))
            {
                return Task.FromResult(OperationResult.Failure(StorageError.Validation("Download source path is a folder.")));
            }

            if (!_client.FileExists(remotePath))
            {
                return Task.FromResult(OperationResult.Failure(StorageError.NotFound("Remote file was not found.")));
            }

            _client.DownloadFile(remotePath, destination, bytes =>
                progress?.Report(new TransferProgress((long)bytes, null, null)));
            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult.Failure(ProviderErrorMapper.FromException(exception)));
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

    private void EnsureParentDirectories(string remotePath)
    {
        var parent = GetParentPath(remotePath);
        if (string.IsNullOrWhiteSpace(parent) || parent == "/" || _client.DirectoryExists(parent))
        {
            return;
        }

        EnsureParentDirectories(parent);
        _client.CreateDirectory(parent);
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
