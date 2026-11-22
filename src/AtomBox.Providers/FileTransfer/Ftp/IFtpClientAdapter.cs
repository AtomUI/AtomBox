namespace AtomBox.Providers.FileTransfer.Ftp;

internal interface IFtpClientAdapter : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FtpRemoteEntry>> ListDirectoryAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default);

    Task MoveFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task MoveDirectoryAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task UploadFileAsync(
        Stream content,
        string path,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default);

    Task DownloadFileAsync(
        string path,
        Stream destination,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default);
}
