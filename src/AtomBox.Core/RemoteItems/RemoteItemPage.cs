using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.RemoteItems;

public sealed record RemoteItemPage
{
    public RemoteItemPage(
        RemotePath Path,
        IReadOnlyList<RemoteItem> Items,
        RemotePageCursor? PreviousCursor,
        RemotePageCursor? NextCursor,
        int PageSize)
    {
        ArgumentNullException.ThrowIfNull(Items);

        this.Path = Path;
        this.Items = Array.AsReadOnly(Items.ToArray());
        this.PreviousCursor = PreviousCursor;
        this.NextCursor = NextCursor;
        this.PageSize = PageSize;
    }

    public RemotePath Path { get; }

    public IReadOnlyList<RemoteItem> Items { get; }

    public RemotePageCursor? PreviousCursor { get; }

    public RemotePageCursor? NextCursor { get; }

    public int PageSize { get; }

    public bool HasPreviousPage => PreviousCursor is not null;

    public bool HasNextPage => NextCursor is not null;

    public void Deconstruct(
        out RemotePath Path,
        out IReadOnlyList<RemoteItem> Items,
        out RemotePageCursor? PreviousCursor,
        out RemotePageCursor? NextCursor,
        out int PageSize)
    {
        Path = this.Path;
        Items = this.Items;
        PreviousCursor = this.PreviousCursor;
        NextCursor = this.NextCursor;
        PageSize = this.PageSize;
    }
}
