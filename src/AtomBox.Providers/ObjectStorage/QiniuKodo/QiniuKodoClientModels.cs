namespace AtomBox.Providers.ObjectStorage.QiniuKodo;

internal sealed record QiniuKodoBucket(string Name);

internal sealed record QiniuKodoObjectListing(
    IReadOnlyList<QiniuKodoObjectSummary> Objects,
    IReadOnlyList<string> CommonPrefixes,
    string? NextCursor = null);

internal sealed record QiniuKodoObjectSummary(
    string Key,
    long Size,
    DateTimeOffset? LastModified,
    string? ETag,
    string? ContentType);

internal sealed class QiniuKodoDownloadObject : IDisposable
{
    private readonly IDisposable? _owner;

    public QiniuKodoDownloadObject(Stream content, long? contentLength, IDisposable? owner = null)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        ContentLength = contentLength;
        _owner = owner;
    }

    public Stream Content { get; }

    public long? ContentLength { get; }

    public void Dispose()
    {
        try
        {
            Content.Dispose();
        }
        finally
        {
            _owner?.Dispose();
        }
    }
}
