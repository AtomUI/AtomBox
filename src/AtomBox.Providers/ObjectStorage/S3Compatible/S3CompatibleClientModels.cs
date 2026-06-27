namespace AtomBox.Providers.ObjectStorage.S3Compatible;

internal sealed record S3CompatibleBucket(
    string Name,
    DateTimeOffset? CreatedAt);

internal sealed record S3CompatibleObjectListing(
    IReadOnlyList<S3CompatibleObjectSummary> Objects,
    IReadOnlyList<string> CommonPrefixes,
    string? NextCursor = null);

internal sealed record S3CompatibleObjectSummary(
    string Key,
    long Size,
    DateTimeOffset? LastModified,
    string? ETag,
    string? ContentType);

internal sealed class S3CompatibleDownloadObject : IDisposable
{
    public S3CompatibleDownloadObject(Stream content, long? contentLength)
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
