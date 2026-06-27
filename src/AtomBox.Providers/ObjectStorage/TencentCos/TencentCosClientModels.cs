namespace AtomBox.Providers.ObjectStorage.TencentCos;

internal sealed record TencentCosBucket(
    string Name,
    string? Region,
    DateTimeOffset? CreatedAt);

internal sealed record TencentCosObjectListing(
    IReadOnlyList<TencentCosObjectSummary> Objects,
    IReadOnlyList<string> CommonPrefixes,
    string? NextCursor = null);

internal sealed record TencentCosObjectSummary(
    string Key,
    long Size,
    DateTimeOffset? LastModified,
    string? ETag);

internal sealed class TencentCosDownloadObject : IDisposable
{
    public TencentCosDownloadObject(Stream content, long? contentLength)
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
