namespace AtomBox.Providers.FileTransfer.Sftp;

internal interface ISftpClientAdapter : IDisposable
{
    bool IsConnected { get; }

    string? WorkingDirectory { get; }

    void Connect();

    IReadOnlyList<SftpRemoteEntry> ListDirectory(string path);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    void DeleteFile(string path);

    void DeleteDirectory(string path);

    void Move(string sourcePath, string destinationPath);

    void UploadFile(Stream content, string path, Action<ulong>? progress = null);

    void DownloadFile(string path, Stream destination, Action<ulong>? progress = null);
}
