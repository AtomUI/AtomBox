namespace AtomBox.Providers.FileTransfer.Sftp;

internal interface ISftpClientAdapter : IDisposable
{
    bool IsConnected { get; }

    string? WorkingDirectory { get; }

    void Connect();

    Task ConnectAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<SftpRemoteEntry> ListDirectory(string path);

    bool FileExists(string path);

    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);

    bool DirectoryExists(string path);

    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default);

    void CreateDirectory(string path);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    void DeleteFile(string path);

    void DeleteDirectory(string path);

    void Move(string sourcePath, string destinationPath);

    Task UploadFileAsync(
        Stream content,
        string path,
        Action<ulong>? progress = null,
        CancellationToken cancellationToken = default);

    Task DownloadFileAsync(
        string path,
        Stream destination,
        Action<ulong>? progress = null,
        CancellationToken cancellationToken = default);
}
