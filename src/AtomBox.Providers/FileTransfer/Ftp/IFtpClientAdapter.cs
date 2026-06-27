namespace AtomBox.Providers.FileTransfer.Ftp;

internal interface IFtpClientAdapter : IDisposable
{
    bool IsConnected { get; }

    void Connect();

    IReadOnlyList<FtpRemoteEntry> ListDirectory(string path);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    void DeleteFile(string path);

    void DeleteDirectory(string path);

    void MoveFile(string sourcePath, string destinationPath);

    void MoveDirectory(string sourcePath, string destinationPath);

    void UploadFile(Stream content, string path, Action<long>? progress = null);

    void DownloadFile(string path, Stream destination, Action<long>? progress = null);
}
