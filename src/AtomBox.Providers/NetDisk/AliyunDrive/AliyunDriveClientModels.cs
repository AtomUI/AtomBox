namespace AtomBox.Providers.NetDisk.AliyunDrive;

internal sealed record AliyunDriveFileItem(
    string FileId,
    string Name,
    bool IsFolder,
    long? Size,
    DateTimeOffset? UpdatedAt,
    string? ContentType);

internal sealed class AliyunDriveDownloadObject : IDisposable
{
    private readonly IDisposable? _owner;

    public AliyunDriveDownloadObject(Stream content, long? contentLength, IDisposable? owner = null)
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
