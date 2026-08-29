namespace AtomBox.Providers.FileTransfer.Sftp;

internal interface ISftpClientAdapter : IDisposable
{
    bool IsConnected { get; }

    string? WorkingDirectory { get; }

    void Connect();

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SftpRemoteEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken);

    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken);

    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken);

    Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken);

    Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken);

    Task UploadFileAsync(Stream content, string path, CancellationToken cancellationToken);

    Task DownloadFileAsync(string path, Stream destination, CancellationToken cancellationToken);
}
