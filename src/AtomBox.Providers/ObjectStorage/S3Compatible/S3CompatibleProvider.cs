using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage;

namespace AtomBox.Providers.ObjectStorage.S3Compatible;

public class S3CompatibleProvider : IStorageProvider
{
    private readonly IAsyncS3CompatibleClient _client;
    private readonly CredentialMaterialLease _credentialLease;

    internal S3CompatibleProvider(
        IS3CompatibleClient client,
        CredentialMaterialLease credentialLease,
        StorageCapabilitySet capabilities)
        : this(new SynchronousS3CompatibleClientAdapter(client), credentialLease, capabilities)
    {
    }

    internal S3CompatibleProvider(
        IAsyncS3CompatibleClient client,
        CredentialMaterialLease credentialLease,
        StorageCapabilitySet capabilities)
    {
        _client = client;
        _credentialLease = credentialLease;
        Capabilities = capabilities;
    }

    public StorageCapabilitySet Capabilities { get; }

    public TransferExecutionCapabilities TransferCapabilities { get; } = new(
        TransferIoExecutionKind.Blocking,
        TransferIoExecutionKind.Blocking,
        supportsCooperativeCancellation: false,
        supportsActiveAbort: false,
        supportsProgress: true);

    public async Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return path.IsRoot
                ? await ListBucketsAsync(cancellationToken).ConfigureAwait(false)
                : await ListObjectsAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return OperationResult<IReadOnlyList<RemoteItem>>.Failure(
                ProviderErrorMapper.FromException(exception));
        }
    }

    public async Task<OperationResult<RemoteItemPage>> ListPageAsync(
        RemotePath path,
        RemotePageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(pageRequest);
            cancellationToken.ThrowIfCancellationRequested();
            if (path.IsRoot)
            {
                var buckets = await ListBucketsAsync(cancellationToken).ConfigureAwait(false);
                return ProviderPaging.PageInMemory(path, buckets.GetValueOrThrow(), pageRequest);
            }

            if (!ProviderPageCursor.TryGetProviderToken(pageRequest.Cursor, "s3-compatible", out var cursor))
            {
                return OperationResult<RemoteItemPage>.Failure(
                    StorageError.Validation("Remote page cursor is invalid for this provider."));
            }

            var pathResult = S3CompatiblePath.FromRemotePath(path);
            if (pathResult.IsFailure)
            {
                return OperationResult<RemoteItemPage>.Failure(pathResult.Error!);
            }

            var objectPath = pathResult.GetValueOrThrow();
            var prefix = objectPath.ToFolderPrefix();
            var listPrefix = ObjectStorageSearchPrefix.Combine(prefix, pageRequest.SearchPrefix);
            var listing = await _client.ListObjectsAsync(
                objectPath.BucketName,
                listPrefix,
                cursor,
                pageRequest.PageSize,
                cancellationToken).ConfigureAwait(false);
            var items = ToRemoteItems(objectPath.BucketName, prefix, listing);
            var nextToken = listing.NextCursor;

            RemotePageCursor? nextCursor = string.IsNullOrWhiteSpace(nextToken)
                ? null
                : ProviderPageCursor.FromProviderToken("s3-compatible", nextToken);
            return OperationResult<RemoteItemPage>.Success(new RemoteItemPage(
                path,
                items,
                PreviousCursor: null,
                nextCursor,
                pageRequest.PageSize));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return OperationResult<RemoteItemPage>.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    public async Task<OperationResult> DeleteAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pathResult = S3CompatiblePath.FromRemotePath(path);
            if (pathResult.IsFailure)
            {
                return OperationResult.Failure(pathResult.Error!);
            }

            var objectPath = pathResult.GetValueOrThrow();
            if (string.IsNullOrWhiteSpace(objectPath.KeyPrefix))
            {
                return OperationResult.Failure(
                    StorageError.Validation("Bucket root cannot be deleted as an object."));
            }

            var deleteKey = path.Kind == RemotePathKind.Folder
                ? objectPath.ToFolderPrefix()
                : objectPath.KeyPrefix;
            await _client.DeleteObjectAsync(
                objectPath.BucketName,
                deleteKey,
                cancellationToken).ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
            var pathResult = GetObjectPath(path, "Upload target object path is required.");
            if (pathResult.IsFailure)
            {
                return OperationResult.Failure(pathResult.Error!);
            }

            var objectPath = pathResult.GetValueOrThrow();
            if (Capabilities.Supports(StorageCapability.MultipartUpload) &&
                ObjectStorageUploadPolicy.ShouldUseMultipart(contentLength))
            {
                await _client.PutObjectMultipartAsync(
                    objectPath.BucketName,
                    objectPath.KeyPrefix,
                    content,
                    contentLength!.Value,
                    (completed, total) => progress?.Report(new TransferProgress(completed, total, null)),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var progressStream = new ProgressReadStream(content, contentLength, progress);
                await _client.PutObjectAsync(
                    objectPath.BucketName,
                    objectPath.KeyPrefix,
                    progressStream,
                    cancellationToken).ConfigureAwait(false);
                progress?.Report(new TransferProgress(contentLength ?? progressStream.BytesRead, contentLength ?? progressStream.BytesRead, null));
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    public async Task<OperationResult> CreateFolderAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.Kind == RemotePathKind.BucketRoot)
            {
                await _client.CreateBucketAsync(
                    path.Value.Trim('/'),
                    cancellationToken).ConfigureAwait(false);
                return OperationResult.Success();
            }

            var pathResult = GetObjectPath(path, "Folder object path is required.");
            if (pathResult.IsFailure)
            {
                return OperationResult.Failure(pathResult.Error!);
            }

            var objectPath = pathResult.GetValueOrThrow();
            var folderKey = objectPath.ToFolderPrefix();
            using var empty = new MemoryStream([]);
            await _client.PutObjectAsync(
                objectPath.BucketName,
                folderKey,
                empty,
                cancellationToken).ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
            var sourceResult = GetObjectPath(sourcePath, "Move source object path is required.");
            if (sourceResult.IsFailure)
            {
                return OperationResult.Failure(sourceResult.Error!);
            }

            var destinationResult = GetObjectPath(destinationPath, "Move destination object path is required.");
            if (destinationResult.IsFailure)
            {
                return OperationResult.Failure(destinationResult.Error!);
            }

            var source = sourceResult.GetValueOrThrow();
            var destination = destinationResult.GetValueOrThrow();
            if (sourcePath.Kind == RemotePathKind.Folder || destinationPath.Kind == RemotePathKind.Folder)
            {
                await MoveFolderAsync(source, destination, cancellationToken).ConfigureAwait(false);
                return OperationResult.Success();
            }

            await _client.CopyObjectAsync(
                source.BucketName,
                source.KeyPrefix,
                destination.BucketName,
                destination.KeyPrefix,
                cancellationToken).ConfigureAwait(false);
            await _client.DeleteObjectAsync(
                source.BucketName,
                source.KeyPrefix,
                cancellationToken).ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    public async Task<OperationResult> RenameAsync(
        RemotePath path,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (path.IsRoot)
        {
            return OperationResult.Failure(StorageError.Validation("Root path cannot be renamed."));
        }

        if (string.IsNullOrWhiteSpace(newName) ||
            newName.Contains('/', StringComparison.Ordinal) ||
            newName.Contains('\\', StringComparison.Ordinal))
        {
            return OperationResult.Failure(StorageError.Validation("New remote name must be a single path segment."));
        }

        var parent = path.GetParent() ?? RemotePath.Root;
        return await MoveAsync(path, parent.Combine(newName, path.Kind), cancellationToken).ConfigureAwait(false);
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
            var pathResult = GetObjectPath(path, "Download source object path is required.");
            if (pathResult.IsFailure)
            {
                return OperationResult.Failure(pathResult.Error!);
            }

            var objectPath = pathResult.GetValueOrThrow();
            using var remoteObject = await _client.GetObjectAsync(
                objectPath.BucketName,
                objectPath.KeyPrefix,
                cancellationToken).ConfigureAwait(false);
            await CopyToAsync(remoteObject.Content, destination, remoteObject.ContentLength, progress, cancellationToken)
                .ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

    private async Task<OperationResult<IReadOnlyList<RemoteItem>>> ListBucketsAsync(
        CancellationToken cancellationToken)
    {
        var buckets = await _client.ListBucketsAsync(cancellationToken).ConfigureAwait(false);
        var items = buckets
            .Select(bucket => new RemoteItem(
                bucket.Name,
                new RemotePath(bucket.Name, RemotePathKind.BucketRoot),
                RemoteItemKind.Bucket,
                null,
                bucket.CreatedAt))
            .ToArray();
        return OperationResult<IReadOnlyList<RemoteItem>>.Success(items);
    }

    private async Task<OperationResult<IReadOnlyList<RemoteItem>>> ListObjectsAsync(
        RemotePath path,
        CancellationToken cancellationToken)
    {
        var pathResult = S3CompatiblePath.FromRemotePath(path);
        if (pathResult.IsFailure)
        {
            return OperationResult<IReadOnlyList<RemoteItem>>.Failure(pathResult.Error!);
        }

        var objectPath = pathResult.GetValueOrThrow();
        var prefix = objectPath.ToFolderPrefix();
        var listing = await _client.ListObjectsAsync(
            objectPath.BucketName,
            prefix,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return OperationResult<IReadOnlyList<RemoteItem>>.Success(ToRemoteItems(objectPath.BucketName, prefix, listing));
    }

    private IReadOnlyList<RemoteItem> ToRemoteItems(
        string bucketName,
        string prefix,
        S3CompatibleObjectListing listing)
    {
        var items = new List<RemoteItem>();
        foreach (var commonPrefix in listing.CommonPrefixes)
        {
            var normalized = commonPrefix.TrimEnd('/');
            var name = GetName(normalized);
            if (!string.IsNullOrWhiteSpace(name))
            {
                items.Add(new RemoteItem(
                    name,
                    new RemotePath($"{bucketName}/{normalized}", RemotePathKind.Folder),
                    RemoteItemKind.Folder,
                    null,
                    null));
            }
        }

        foreach (var summary in listing.Objects)
        {
            if (summary.Key.EndsWith("/", StringComparison.Ordinal) || summary.Key == prefix)
            {
                continue;
            }

            var name = GetName(summary.Key);
            if (!string.IsNullOrWhiteSpace(name))
            {
                items.Add(new RemoteItem(
                    name,
                    new RemotePath($"{bucketName}/{summary.Key}", RemotePathKind.ObjectPath),
                    RemoteItemKind.File,
                    summary.Size,
                    summary.LastModified,
                    summary.ETag,
                    summary.ContentType));
            }
        }

        return items
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Kind)
            .ToArray();
    }

    private async Task MoveFolderAsync(
        S3CompatiblePath source,
        S3CompatiblePath destination,
        CancellationToken cancellationToken)
    {
        var sourcePrefix = source.ToFolderPrefix();
        var destinationPrefix = destination.ToFolderPrefix();
        if (string.IsNullOrWhiteSpace(sourcePrefix) || string.IsNullOrWhiteSpace(destinationPrefix))
        {
            throw new ArgumentException("Folder move source and destination prefixes are required.");
        }

        using var empty = new MemoryStream([]);
        await _client.PutObjectAsync(
            destination.BucketName,
            destinationPrefix,
            empty,
            cancellationToken).ConfigureAwait(false);
        await MoveFolderPageAsync(
            source.BucketName,
            sourcePrefix,
            destination.BucketName,
            destinationPrefix,
            cursor: null,
            cancellationToken).ConfigureAwait(false);
        await TryDeleteObjectAsync(
            source.BucketName,
            sourcePrefix,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task MoveFolderPageAsync(
        string sourceBucket,
        string sourcePrefix,
        string destinationBucket,
        string destinationPrefix,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var listing = await _client.ListObjectsAsync(
            sourceBucket,
            sourcePrefix,
            cursor,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var commonPrefix in listing.CommonPrefixes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = commonPrefix[sourcePrefix.Length..];
            var nextDestinationPrefix = destinationPrefix + relative;
            using var empty = new MemoryStream([]);
            await _client.PutObjectAsync(
                destinationBucket,
                nextDestinationPrefix,
                empty,
                cancellationToken).ConfigureAwait(false);
            await MoveFolderPageAsync(
                sourceBucket,
                commonPrefix,
                destinationBucket,
                nextDestinationPrefix,
                cursor: null,
                cancellationToken).ConfigureAwait(false);
            await TryDeleteObjectAsync(
                sourceBucket,
                commonPrefix,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var summary in listing.Objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (summary.Key == sourcePrefix)
            {
                continue;
            }

            var destinationKey = destinationPrefix + summary.Key[sourcePrefix.Length..];
            await _client.CopyObjectAsync(
                sourceBucket,
                summary.Key,
                destinationBucket,
                destinationKey,
                cancellationToken).ConfigureAwait(false);
            await _client.DeleteObjectAsync(
                sourceBucket,
                summary.Key,
                cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(listing.NextCursor))
        {
            await MoveFolderPageAsync(
                sourceBucket,
                sourcePrefix,
                destinationBucket,
                destinationPrefix,
                listing.NextCursor,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TryDeleteObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            await _client.DeleteObjectAsync(bucketName, key, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Folder marker deletion is best-effort because many object stores never create marker objects.
        }
    }

    private static OperationResult<S3CompatiblePath> GetObjectPath(RemotePath path, string emptyKeyMessage)
    {
        var pathResult = S3CompatiblePath.FromRemotePath(path);
        if (pathResult.IsFailure)
        {
            return pathResult;
        }

        var objectPath = pathResult.GetValueOrThrow();
        return string.IsNullOrWhiteSpace(objectPath.KeyPrefix)
            ? OperationResult<S3CompatiblePath>.Failure(StorageError.Validation(emptyKeyMessage))
            : OperationResult<S3CompatiblePath>.Success(objectPath);
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

    private static string GetName(string key)
    {
        var trimmed = key.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }
}
