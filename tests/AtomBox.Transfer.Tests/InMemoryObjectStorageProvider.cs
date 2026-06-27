using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Transfer.Tests;

public sealed class InMemoryObjectStorageProvider : IStorageProvider
{
    private readonly Dictionary<string, Dictionary<string, byte[]>> _buckets;
    private bool _disposed;

    public InMemoryObjectStorageProvider(Dictionary<string, Dictionary<string, byte[]>> buckets)
    {
        _buckets = buckets;
    }

    public StorageCapabilitySet Capabilities { get; } =
        new(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download | StorageCapability.Delete);

    public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Failure(DisposedError()));
        }

        if (path.IsRoot)
        {
            IReadOnlyList<RemoteItem> buckets = _buckets.Keys
                .Order(StringComparer.Ordinal)
                .Select(bucket => new RemoteItem(
                    bucket,
                    new RemotePath(bucket, RemotePathKind.BucketRoot),
                    RemoteItemKind.Bucket,
                    null,
                    null))
                .ToArray();
            return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Success(buckets));
        }

        var objectPath = ParseObjectPath(path);
        if (objectPath.IsFailure)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Failure(objectPath.Error!));
        }

        var (bucketName, keyPrefix) = objectPath.GetValueOrThrow();
        if (!_buckets.TryGetValue(bucketName, out var objects))
        {
            return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Failure(StorageError.NotFound("Bucket was not found.")));
        }

        var prefix = string.IsNullOrWhiteSpace(keyPrefix)
            ? string.Empty
            : keyPrefix.TrimEnd('/') + "/";
        var items = objects
            .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Where(pair => pair.Key.Length > prefix.Length)
            .Where(pair => !pair.Key[prefix.Length..].Contains('/', StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new RemoteItem(
                Path.GetFileName(pair.Key),
                new RemotePath($"{bucketName}/{pair.Key}", RemotePathKind.ObjectPath),
                RemoteItemKind.File,
                pair.Value.Length,
                null,
                contentType: "application/octet-stream"))
            .ToArray();

        return Task.FromResult(OperationResult<IReadOnlyList<RemoteItem>>.Success(items));
    }

    public async Task<OperationResult> UploadAsync(
        RemotePath path,
        Stream content,
        long? contentLength,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return OperationResult.Failure(DisposedError());
        }

        var objectPath = ParseRequiredObjectPath(path, "Upload target object path is required.");
        if (objectPath.IsFailure)
        {
            return OperationResult.Failure(objectPath.Error!);
        }

        var (bucketName, key) = objectPath.GetValueOrThrow();
        if (!_buckets.TryGetValue(bucketName, out var objects))
        {
            return OperationResult.Failure(StorageError.NotFound("Bucket was not found."));
        }

        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
        objects[key] = copy.ToArray();
        progress?.Report(new TransferProgress(copy.Length, contentLength ?? copy.Length, null));
        return OperationResult.Success();
    }

    public Task<OperationResult> DownloadAsync(
        RemotePath path,
        Stream destination,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.FromResult(OperationResult.Failure(DisposedError()));
        }

        var objectPath = ParseRequiredObjectPath(path, "Download source object path is required.");
        if (objectPath.IsFailure)
        {
            return Task.FromResult(OperationResult.Failure(objectPath.Error!));
        }

        var (bucketName, key) = objectPath.GetValueOrThrow();
        if (!_buckets.TryGetValue(bucketName, out var objects) ||
            !objects.TryGetValue(key, out var payload))
        {
            return Task.FromResult(OperationResult.Failure(StorageError.NotFound("Remote object was not found.")));
        }

        destination.Write(payload, 0, payload.Length);
        progress?.Report(new TransferProgress(payload.Length, payload.Length, null));
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> DeleteAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.FromResult(OperationResult.Failure(DisposedError()));
        }

        var objectPath = ParseRequiredObjectPath(path, "Delete target object path is required.");
        if (objectPath.IsFailure)
        {
            return Task.FromResult(OperationResult.Failure(objectPath.Error!));
        }

        var (bucketName, key) = objectPath.GetValueOrThrow();
        if (!_buckets.TryGetValue(bucketName, out var objects) || !objects.Remove(key))
        {
            return Task.FromResult(OperationResult.Failure(StorageError.NotFound("Remote object was not found.")));
        }

        return Task.FromResult(OperationResult.Success());
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private static OperationResult<(string BucketName, string Key)> ParseRequiredObjectPath(
        RemotePath path,
        string emptyKeyMessage)
    {
        var parsed = ParseObjectPath(path);
        if (parsed.IsFailure)
        {
            return parsed;
        }

        var value = parsed.GetValueOrThrow();
        return string.IsNullOrWhiteSpace(value.Key)
            ? OperationResult<(string BucketName, string Key)>.Failure(StorageError.Validation(emptyKeyMessage))
            : OperationResult<(string BucketName, string Key)>.Success(value);
    }

    private static OperationResult<(string BucketName, string Key)> ParseObjectPath(RemotePath path)
    {
        if (path.IsRoot)
        {
            return OperationResult<(string BucketName, string Key)>.Failure(StorageError.Validation("Bucket path is required."));
        }

        var value = path.Value.Trim('/');
        var separator = value.IndexOf('/', StringComparison.Ordinal);
        var bucketName = separator < 0 ? value : value[..separator];
        var key = separator < 0 ? string.Empty : value[(separator + 1)..];

        return string.IsNullOrWhiteSpace(bucketName)
            ? OperationResult<(string BucketName, string Key)>.Failure(StorageError.Validation("Bucket name is required."))
            : OperationResult<(string BucketName, string Key)>.Success((bucketName, key));
    }

    private static StorageError DisposedError()
    {
        return new StorageError(
            StorageErrorCode.ProviderUnavailable,
            "Provider has been disposed.",
            StorageErrorCategory.Provider);
    }
}
