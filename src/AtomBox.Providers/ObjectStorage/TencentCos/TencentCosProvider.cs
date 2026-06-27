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

namespace AtomBox.Providers.ObjectStorage.TencentCos;

public sealed class TencentCosProvider : IStorageProvider
{
    private readonly ITencentCosClient _client;
    private readonly CredentialMaterialLease _credentialLease;

    internal TencentCosProvider(
        ITencentCosClient client,
        CredentialMaterialLease credentialLease,
        StorageCapabilitySet capabilities)
    {
        _client = client;
        _credentialLease = credentialLease;
        Capabilities = capabilities;
    }

    public StorageCapabilitySet Capabilities { get; }

    public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var items = path.IsRoot
                ? ListBuckets()
                : ListObjects(path);

            return Task.FromResult(items);
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Failure(
                ProviderErrorMapper.FromException(exception)));
        }
    }

    public Task<OperationResult<RemoteItemPage>> ListPageAsync(
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
                return Task.FromResult(ProviderPaging.PageInMemory(path, ListBuckets().GetValueOrThrow(), pageRequest));
            }

            if (!ProviderPageCursor.TryGetProviderToken(pageRequest.Cursor, "tencent-cos", out var cursor))
            {
                return Task.FromResult(OperationResult<RemoteItemPage>.Failure(
                    StorageError.Validation("Remote page cursor is invalid for this provider.")));
            }

            var pathResult = TencentCosPath.FromRemotePath(path);
            if (pathResult.IsFailure)
            {
                return Task.FromResult(OperationResult<RemoteItemPage>.Failure(pathResult.Error!));
            }

            var cosPath = pathResult.GetValueOrThrow();
            var prefix = cosPath.ToFolderPrefix();
            var listPrefix = ObjectStorageSearchPrefix.Combine(prefix, pageRequest.SearchPrefix);
            var items = new List<RemoteItem>(pageRequest.PageSize);
            string? nextToken = cursor;
            do
            {
                var listing = _client.ListObjects(cosPath.BucketName, listPrefix, nextToken, pageRequest.PageSize);
                items.AddRange(ToRemoteItems(cosPath.BucketName, prefix, listing));
                nextToken = listing.NextCursor;
            }
            while (items.Count < pageRequest.PageSize && !string.IsNullOrWhiteSpace(nextToken));

            RemotePageCursor? nextCursor = string.IsNullOrWhiteSpace(nextToken)
                ? null
                : ProviderPageCursor.FromProviderToken("tencent-cos", nextToken);
            return Task.FromResult(OperationResult<RemoteItemPage>.Success(new RemoteItemPage(
                path,
                items.Take(pageRequest.PageSize).ToArray(),
                null,
                nextCursor,
                pageRequest.PageSize)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult<RemoteItemPage>.Failure(ProviderErrorMapper.FromException(exception)));
        }
    }

    public Task<OperationResult> DeleteAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pathResult = TencentCosPath.FromRemotePath(path);
            if (pathResult.IsFailure)
            {
                return Task.FromResult(OperationResult.Failure(pathResult.Error!));
            }

            var cosPath = pathResult.GetValueOrThrow();
            if (string.IsNullOrWhiteSpace(cosPath.KeyPrefix))
            {
                return Task.FromResult(OperationResult.Failure(
                    StorageError.Validation("Bucket root cannot be deleted as an object.")));
            }

            _client.DeleteObject(cosPath.BucketName, cosPath.KeyPrefix);
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

            var pathResult = GetObjectPath(path, "Upload target object path is required.");
            if (pathResult.IsFailure)
            {
                return Task.FromResult(OperationResult.Failure(pathResult.Error!));
            }

            var cosPath = pathResult.GetValueOrThrow();
            if (Capabilities.Supports(StorageCapability.MultipartUpload) &&
                ObjectStorageUploadPolicy.ShouldUseMultipart(contentLength))
            {
                _client.PutObjectMultipart(
                    cosPath.BucketName,
                    cosPath.KeyPrefix,
                    content,
                    contentLength!.Value,
                    (completed, total) => progress?.Report(new TransferProgress(completed, total, null)));
            }
            else
            {
                _client.PutObject(cosPath.BucketName, cosPath.KeyPrefix, content, (completed, total) =>
                    progress?.Report(new TransferProgress(completed, total > 0 ? total : contentLength, null)));
                progress?.Report(new TransferProgress(contentLength ?? 0, contentLength ?? 0, null));
            }

            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult.Failure(ProviderErrorMapper.FromException(exception)));
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

            var pathResult = GetObjectPath(path, "Download source object path is required.");
            if (pathResult.IsFailure)
            {
                return OperationResult.Failure(pathResult.Error!);
            }

            var cosPath = pathResult.GetValueOrThrow();
            using var cosObject = _client.GetObject(cosPath.BucketName, cosPath.KeyPrefix);
            await CopyToAsync(
                cosObject.Content,
                destination,
                cosObject.ContentLength,
                progress,
                cancellationToken).ConfigureAwait(false);

            return OperationResult.Success();
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    public Task<OperationResult> CreateFolderAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.Kind == RemotePathKind.BucketRoot)
            {
                _client.CreateBucket(path.Value.Trim('/'));
                return Task.FromResult(OperationResult.Success());
            }

            var pathResult = GetObjectPath(path, "Folder object path is required.");
            if (pathResult.IsFailure)
            {
                return Task.FromResult(OperationResult.Failure(pathResult.Error!));
            }

            var cosPath = pathResult.GetValueOrThrow();
            using var marker = CreateFolderMarkerContent();
            _client.PutObject(cosPath.BucketName, cosPath.ToFolderPrefix(), marker);
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
            var sourceResult = GetObjectPath(sourcePath, "Move source object path is required.");
            if (sourceResult.IsFailure)
            {
                return Task.FromResult(OperationResult.Failure(sourceResult.Error!));
            }

            var destinationResult = GetObjectPath(destinationPath, "Move destination object path is required.");
            if (destinationResult.IsFailure)
            {
                return Task.FromResult(OperationResult.Failure(destinationResult.Error!));
            }

            var source = sourceResult.GetValueOrThrow();
            var destination = destinationResult.GetValueOrThrow();
            if (sourcePath.Kind == RemotePathKind.Folder || destinationPath.Kind == RemotePathKind.Folder)
            {
                MoveFolder(source, destination);
            }
            else
            {
                _client.CopyObject(source.BucketName, source.KeyPrefix, destination.BucketName, destination.KeyPrefix);
                _client.DeleteObject(source.BucketName, source.KeyPrefix);
            }

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

    public ValueTask DisposeAsync()
    {
        return _credentialLease.DisposeAsync();
    }

    private OperationResult<IReadOnlyList<RemoteItem>> ListBuckets()
    {
        var items = _client.ListBuckets()
            .Select(bucket => new RemoteItem(
                bucket.Name,
                new RemotePath(bucket.Name, RemotePathKind.BucketRoot),
                RemoteItemKind.Bucket,
                size: null,
                bucket.CreatedAt))
            .ToArray();

        return OperationResult<IReadOnlyList<RemoteItem>>.Success(items);
    }

    private OperationResult<IReadOnlyList<RemoteItem>> ListObjects(RemotePath path)
    {
        var pathResult = TencentCosPath.FromRemotePath(path);
        if (pathResult.IsFailure)
        {
            return OperationResult<IReadOnlyList<RemoteItem>>.Failure(pathResult.Error!);
        }

        var cosPath = pathResult.GetValueOrThrow();
        var prefix = cosPath.ToFolderPrefix();
        var listing = _client.ListObjects(cosPath.BucketName, prefix);
        return OperationResult<IReadOnlyList<RemoteItem>>.Success(ToRemoteItems(cosPath.BucketName, prefix, listing));
    }

    private IReadOnlyList<RemoteItem> ToRemoteItems(
        string bucketName,
        string prefix,
        TencentCosObjectListing listing)
    {
        var items = new List<RemoteItem>();

        foreach (var commonPrefix in listing.CommonPrefixes)
        {
            var normalized = commonPrefix.TrimEnd('/');
            var name = GetName(normalized);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            items.Add(new RemoteItem(
                name,
                new RemotePath($"{bucketName}/{normalized}", RemotePathKind.Folder),
                RemoteItemKind.Folder,
                size: null,
                updatedAt: null));
        }

        foreach (var summary in listing.Objects)
        {
            if (summary.Key.EndsWith("/", StringComparison.Ordinal) || summary.Key == prefix)
            {
                continue;
            }

            var name = GetName(summary.Key);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            items.Add(new RemoteItem(
                name,
                new RemotePath($"{bucketName}/{summary.Key}", RemotePathKind.ObjectPath),
                RemoteItemKind.File,
                summary.Size,
                summary.LastModified,
                summary.ETag,
                contentType: null));
        }

        return items
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Kind)
            .ToArray();
    }

    private static OperationResult<TencentCosPath> GetObjectPath(RemotePath path, string emptyKeyMessage)
    {
        var pathResult = TencentCosPath.FromRemotePath(path);
        if (pathResult.IsFailure)
        {
            return pathResult;
        }

        var cosPath = pathResult.GetValueOrThrow();
        if (string.IsNullOrWhiteSpace(cosPath.KeyPrefix))
        {
            return OperationResult<TencentCosPath>.Failure(StorageError.Validation(emptyKeyMessage));
        }

        return OperationResult<TencentCosPath>.Success(cosPath);
    }

    private void MoveFolder(TencentCosPath source, TencentCosPath destination)
    {
        var sourcePrefix = source.ToFolderPrefix();
        var destinationPrefix = destination.ToFolderPrefix();
        using var marker = CreateFolderMarkerContent();
        _client.PutObject(destination.BucketName, destinationPrefix, marker);
        MoveFolderPage(source.BucketName, sourcePrefix, destination.BucketName, destinationPrefix, null);
        TryDeleteObject(source.BucketName, sourcePrefix);
    }

    private void MoveFolderPage(
        string sourceBucket,
        string sourcePrefix,
        string destinationBucket,
        string destinationPrefix,
        string? cursor)
    {
        var listing = _client.ListObjects(sourceBucket, sourcePrefix, cursor);
        foreach (var commonPrefix in listing.CommonPrefixes)
        {
            var relative = commonPrefix[sourcePrefix.Length..];
            var nextDestinationPrefix = destinationPrefix + relative;
            using var marker = CreateFolderMarkerContent();
            _client.PutObject(destinationBucket, nextDestinationPrefix, marker);
            MoveFolderPage(sourceBucket, commonPrefix, destinationBucket, nextDestinationPrefix, null);
            TryDeleteObject(sourceBucket, commonPrefix);
        }

        foreach (var summary in listing.Objects)
        {
            if (summary.Key == sourcePrefix)
            {
                continue;
            }

            var destinationKey = destinationPrefix + summary.Key[sourcePrefix.Length..];
            _client.CopyObject(sourceBucket, summary.Key, destinationBucket, destinationKey);
            _client.DeleteObject(sourceBucket, summary.Key);
        }

        if (!string.IsNullOrWhiteSpace(listing.NextCursor))
        {
            MoveFolderPage(sourceBucket, sourcePrefix, destinationBucket, destinationPrefix, listing.NextCursor);
        }
    }

    private void TryDeleteObject(string bucketName, string key)
    {
        try
        {
            _client.DeleteObject(bucketName, key);
        }
        catch
        {
        }
    }

    private static MemoryStream CreateFolderMarkerContent()
    {
        return new MemoryStream([0]);
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
