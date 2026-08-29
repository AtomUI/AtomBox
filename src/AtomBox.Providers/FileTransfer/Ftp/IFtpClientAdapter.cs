namespace AtomBox.Providers.FileTransfer.Ftp;

internal interface IFtpClientAdapter : IDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<FtpRemoteEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken);

    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken);

    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken);

    Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken);

    Task MoveFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken);

    Task MoveDirectoryAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken);

    Task UploadFileAsync(Stream content, string path, CancellationToken cancellationToken);

    Task DownloadFileAsync(string path, Stream destination, CancellationToken cancellationToken);
}
