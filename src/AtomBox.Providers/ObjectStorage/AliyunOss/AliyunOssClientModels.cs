namespace AtomBox.Providers.ObjectStorage.AliyunOss;

internal sealed record AliyunOssBucket(
    string Name,
    DateTimeOffset? CreatedAt);

internal sealed record AliyunOssObjectListing(
    IReadOnlyList<AliyunOssObjectSummary> Objects,
    IReadOnlyList<string> CommonPrefixes,
    string? NextCursor = null);

internal sealed record AliyunOssObjectSummary(
    string Key,
    long Size,
    DateTimeOffset? LastModified,
    string? ETag,
    string? ContentType);

internal sealed class AliyunOssDownloadObject : IDisposable
{
    private readonly IDisposable? _owner;

    public AliyunOssDownloadObject(Stream content, long? contentLength, IDisposable? owner = null)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        ContentLength = contentLength;
        _owner = owner;
    }

    public Stream Content { get; }

    public long? ContentLength { get; }

    public void Dispose()
    {
        Content.Dispose();
        _owner?.Dispose();
    }
}
