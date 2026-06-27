using AtomBox.Core.RemoteItems;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Browsing;

public enum RemoteEntryState
{
    Empty = 0,
    SingleAccount = 1,
    AccountSelection = 2
}

public sealed record RemoteEntryResult(RemoteEntryState State, IReadOnlyList<StorageAccountId> AccountIds);

public sealed record ListRemoteItemsResult(
    RemotePath Path,
    IReadOnlyList<RemoteItem> Items,
    RemotePageCursor? PreviousCursor,
    RemotePageCursor? NextCursor,
    int PageSize,
    RemotePathContextResult Context,
    ListRemoteItemsRequest? RetryRequest = null)
{
    public bool HasPreviousPage => PreviousCursor is not null;

    public bool HasNextPage => NextCursor is not null;

    public static ListRemoteItemsResult FromPage(RemoteItemPage page)
    {
        return new ListRemoteItemsResult(
            page.Path,
            page.Items,
            page.PreviousCursor,
            page.NextCursor,
            page.PageSize,
            RemotePathContextResult.From(page.Path, page.PreviousCursor, page.NextCursor));
    }
}

public sealed record RemotePathContextResult(
    RemotePath CurrentPath,
    RemotePath? ParentPath,
    bool IsRoot,
    bool IsBucketRoot,
    bool CanUpload,
    bool CanDeleteSelectedFile,
    bool HasPreviousPage,
    bool HasNextPage)
{
    public static RemotePathContextResult From(
        RemotePath path,
        RemotePageCursor? previousCursor = null,
        RemotePageCursor? nextCursor = null)
    {
        return new RemotePathContextResult(
            path,
            path.GetParent(),
            path.IsRoot,
            path.Kind == RemotePathKind.BucketRoot,
            !path.IsRoot,
            !path.IsRoot,
            previousCursor is not null,
            nextCursor is not null);
    }
}
