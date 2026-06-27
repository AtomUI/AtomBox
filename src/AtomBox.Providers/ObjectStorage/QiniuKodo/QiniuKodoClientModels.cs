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
    public QiniuKodoDownloadObject(Stream content, long? contentLength)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        ContentLength = contentLength;
    }

    public Stream Content { get; }

    public long? ContentLength { get; }

    public void Dispose()
    {
        Content.Dispose();
    }
}
