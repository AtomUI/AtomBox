using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Providers;

public interface IStorageProvider : IAsyncDisposable
{
    StorageCapabilitySet Capabilities { get; }

    Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
        RemotePath path,
        CancellationToken cancellationToken = default);

    async Task<OperationResult<RemoteItemPage>> ListPageAsync(
        RemotePath path,
        RemotePageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageRequest);

        var listResult = await ListAsync(path, cancellationToken).ConfigureAwait(false);
        if (listResult.IsFailure)
        {
            return OperationResult<RemoteItemPage>.Failure(listResult.Error!);
        }

        var allItems = listResult.GetValueOrThrow();
        var offset = 0;
        if (pageRequest.Cursor is { } cursor &&
            !cursor.TryGetOffset(out offset))
        {
            return OperationResult<RemoteItemPage>.Failure(StorageError.Validation("Remote page cursor is invalid."));
        }

        if (offset > allItems.Count)
        {
            offset = allItems.Count;
        }

        var pageItems = allItems
            .Skip(offset)
            .Take(pageRequest.PageSize)
            .ToArray();
        var nextOffset = offset + pageRequest.PageSize;
        var previousOffset = Math.Max(0, offset - pageRequest.PageSize);
        var previousCursor = offset > 0 ? RemotePageCursor.FromOffset(previousOffset) : (RemotePageCursor?)null;
        var nextCursor = nextOffset < allItems.Count ? RemotePageCursor.FromOffset(nextOffset) : (RemotePageCursor?)null;

        return OperationResult<RemoteItemPage>.Success(new RemoteItemPage(
            path,
            pageItems,
            previousCursor,
            nextCursor,
            pageRequest.PageSize));
    }

    Task<OperationResult> DeleteAsync(
        RemotePath path,
        CancellationToken cancellationToken = default);

    Task<OperationResult> CreateFolderAsync(
        RemotePath path,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Failure(
            StorageError.NotSupported("Create folder is not supported by this provider.")));
    }

    Task<OperationResult> MoveAsync(
        RemotePath sourcePath,
        RemotePath destinationPath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Failure(
            StorageError.NotSupported("Move is not supported by this provider.")));
    }

    Task<OperationResult> RenameAsync(
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
        var destinationPath = parent.Combine(newName, path.Kind);
        return MoveAsync(path, destinationPath, cancellationToken);
    }

    Task<OperationResult> UploadAsync(
        RemotePath path,
        Stream content,
        long? contentLength,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DownloadAsync(
        RemotePath path,
        Stream destination,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
