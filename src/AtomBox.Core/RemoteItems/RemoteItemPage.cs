using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.RemoteItems;

public sealed record RemoteItemPage(
    RemotePath Path,
    IReadOnlyList<RemoteItem> Items,
    RemotePageCursor? PreviousCursor,
    RemotePageCursor? NextCursor,
    int PageSize)
{
    public bool HasPreviousPage => PreviousCursor is not null;

    public bool HasNextPage => NextCursor is not null;
}
