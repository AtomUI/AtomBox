namespace AtomBox.Providers.NetDisk.AliyunDrive;

internal interface IAliyunDriveClient : IDisposable
{
    Task<IReadOnlyList<AliyunDriveFileItem>> ListAsync(
        string driveId,
        string parentFileId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string driveId,
        string fileId,
        CancellationToken cancellationToken = default);

    Task UploadAsync(
        string driveId,
        string parentFileId,
        string name,
        Stream content,
        long? contentLength,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    Task<AliyunDriveDownloadObject> GetObjectAsync(
        string driveId,
        string fileId,
        CancellationToken cancellationToken = default);
}
