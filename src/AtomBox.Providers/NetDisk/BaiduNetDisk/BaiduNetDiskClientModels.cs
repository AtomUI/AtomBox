namespace AtomBox.Providers.NetDisk.BaiduNetDisk;

internal sealed record BaiduNetDiskFileItem(
    string Path,
    string Name,
    bool IsFolder,
    long? Size,
    DateTimeOffset? UpdatedAt);

internal sealed class BaiduNetDiskDownloadObject : IDisposable
{
    private readonly IDisposable? _owner;

    public BaiduNetDiskDownloadObject(Stream content, long? contentLength, IDisposable? owner = null)
    {
        Content = content;
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
