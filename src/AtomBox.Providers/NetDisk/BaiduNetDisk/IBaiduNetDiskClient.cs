namespace AtomBox.Providers.NetDisk.BaiduNetDisk;

internal interface IBaiduNetDiskClient : IDisposable
{
    Task<IReadOnlyList<BaiduNetDiskFileItem>> ListAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task UploadAsync(
        string path,
        Stream content,
        long? contentLength,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BaiduNetDiskDownloadObject> GetObjectAsync(
        string path,
        CancellationToken cancellationToken = default);
}
