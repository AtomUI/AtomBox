using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.Common;

internal static class ProviderPaging
{
    public static OperationResult<RemoteItemPage> PageInMemory(
        RemotePath path,
        IReadOnlyList<RemoteItem> allItems,
        RemotePageRequest pageRequest)
    {
        var offset = 0;
        if (pageRequest.Cursor is { } cursor && !cursor.TryGetOffset(out offset))
        {
            return OperationResult<RemoteItemPage>.Failure(Core.Errors.StorageError.Validation("Remote page cursor is invalid."));
        }

        if (offset > allItems.Count)
        {
            offset = allItems.Count;
        }

        var items = allItems.Skip(offset).Take(pageRequest.PageSize).ToArray();
        var nextOffset = offset + pageRequest.PageSize;
        var previousOffset = Math.Max(0, offset - pageRequest.PageSize);
        return OperationResult<RemoteItemPage>.Success(new RemoteItemPage(
            path,
            items,
            offset > 0 ? RemotePageCursor.FromOffset(previousOffset) : null,
            nextOffset < allItems.Count ? RemotePageCursor.FromOffset(nextOffset) : null,
            pageRequest.PageSize));
    }
}
