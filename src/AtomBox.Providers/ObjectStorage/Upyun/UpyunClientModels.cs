namespace AtomBox.Providers.ObjectStorage.Upyun;

internal sealed record UpyunBucket(string Name);

internal sealed record UpyunObjectListing(
    IReadOnlyList<UpyunObjectSummary> Objects,
    IReadOnlyList<string> CommonPrefixes,
    string? NextCursor = null);

internal sealed record UpyunObjectSummary(
    string Key,
    long Size,
    DateTimeOffset? LastModified,
    string? ETag,
    string? ContentType);

internal sealed class UpyunDownloadObject : IDisposable
{
    public UpyunDownloadObject(Stream content, long? contentLength)
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
