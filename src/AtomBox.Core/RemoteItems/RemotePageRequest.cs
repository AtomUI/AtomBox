namespace AtomBox.Core.RemoteItems;

public sealed record RemotePageRequest
{
    public static RemotePageRequest FirstPage { get; } = new();

    public RemotePageRequest(int pageSize = 100, RemotePageCursor? cursor = null, string? searchPrefix = null)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Remote page size must be positive.");
        }

        if (pageSize > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Remote page size cannot exceed 1000.");
        }

        PageSize = pageSize;
        Cursor = cursor;
        SearchPrefix = string.IsNullOrWhiteSpace(searchPrefix) ? null : searchPrefix.Trim();
    }

    public int PageSize { get; }

    public RemotePageCursor? Cursor { get; }

    public string? SearchPrefix { get; }
}
