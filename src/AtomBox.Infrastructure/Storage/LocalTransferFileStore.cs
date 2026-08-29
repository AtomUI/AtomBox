using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Infrastructure.Storage;

public sealed class LocalTransferFileStore : ILocalTransferFileStore
{
    public Task<OperationResult<LocalTransferReadHandle>> OpenReadAsync(
        LocalPath path,
        CancellationToken cancellationToken = default)
    {
        if (path.IsEmpty)
        {
            return Task.FromResult(OperationResult<LocalTransferReadHandle>.Failure(
                StorageError.Validation("Local read path is required.")));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = new FileStream(
                path.Value,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 64,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Task.FromResult(OperationResult<LocalTransferReadHandle>.Success(
                new LocalTransferReadHandle(stream, stream.Length)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult<LocalTransferReadHandle>.Failure(MapException(exception)));
        }
    }

    public Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
        LocalPath path,
        CancellationToken cancellationToken = default)
    {
        return OpenWriteAsync(path, TransferOverwritePolicy.Skip, cancellationToken);
    }

    public Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
        LocalPath path,
        TransferOverwritePolicy overwritePolicy,
        CancellationToken cancellationToken = default)
    {
        if (path.IsEmpty)
        {
            return Task.FromResult(OperationResult<LocalTransferWriteHandle>.Failure(
                StorageError.Validation("Local write path is required.")));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(path.Value);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var targetPath = ResolveWritePath(path.Value, overwritePolicy);
            if (targetPath is null)
            {
                return Task.FromResult(OperationResult<LocalTransferWriteHandle>.Failure(new StorageError(
                    StorageErrorCode.Conflict,
                    "Local target file already exists.",
                    StorageErrorCategory.Conflict)));
            }

            var temporaryPath = CreateTemporaryPath(directory);
            var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 64,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Task.FromResult(OperationResult<LocalTransferWriteHandle>.Success(
                new LocalTransferWriteHandle(
                    stream,
                    new LocalPath(targetPath),
                    commitAsync: token => CommitWriteAsync(temporaryPath, targetPath, overwritePolicy, token),
                    abortAsync: token => AbortWriteAsync(temporaryPath, token))));
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult<LocalTransferWriteHandle>.Failure(MapException(exception)));
        }
    }

    private static string? ResolveWritePath(string path, TransferOverwritePolicy overwritePolicy)
    {
        if (!File.Exists(path) || overwritePolicy == TransferOverwritePolicy.Overwrite)
        {
            return path;
        }

        if (overwritePolicy is TransferOverwritePolicy.Skip)
        {
            return null;
        }

        return ResolveRenamedPath(path);
    }

    private static string ResolveRenamedPath(string path)
    {
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 1; index < 10000; index++)
        {
            var candidate = Path.Combine(
                string.IsNullOrWhiteSpace(directory) ? string.Empty : directory,
                $"{fileName} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? string.Empty : directory,
            $"{fileName} ({Guid.NewGuid():N}){extension}");
    }

    private static string CreateTemporaryPath(string? directory)
    {
        var root = string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
        string candidate;
        do
        {
            candidate = Path.Combine(root, $".atombox-download-{Guid.NewGuid():N}.part");
        }
        while (File.Exists(candidate));

        return candidate;
    }

    private static Task<OperationResult> CommitWriteAsync(
        string temporaryPath,
        string targetPath,
        TransferOverwritePolicy overwritePolicy,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var overwrite = overwritePolicy == TransferOverwritePolicy.Overwrite;
            if (!overwrite && File.Exists(targetPath))
            {
                return Task.FromResult(OperationResult.Failure(new StorageError(
                    StorageErrorCode.Conflict,
                    "Local target file already exists.",
                    StorageErrorCategory.Conflict)));
            }

            File.Move(temporaryPath, targetPath, overwrite);
            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult.Failure(MapException(exception)));
        }
    }

    private static Task<OperationResult> AbortWriteAsync(
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult.Failure(MapException(exception)));
        }
    }

    private static StorageError MapException(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => new StorageError(
                StorageErrorCode.OperationCanceled,
                "Local file operation was canceled.",
                StorageErrorCategory.Canceled,
                isRetryable: true),
            FileNotFoundException or DirectoryNotFoundException => StorageError.NotFound("Local file was not found."),
            UnauthorizedAccessException => new StorageError(
                StorageErrorCode.AuthorizationFailed,
                "Local file access was denied.",
                StorageErrorCategory.Authorization),
            IOException => new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                "Local file operation failed.",
                StorageErrorCategory.Infrastructure,
                isRetryable: true),
            ArgumentException or NotSupportedException => StorageError.Validation("Local path is invalid."),
            _ => StorageError.Unknown("Unexpected local file error.")
        };
    }
}
