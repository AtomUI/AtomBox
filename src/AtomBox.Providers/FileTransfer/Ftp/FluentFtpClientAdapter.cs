using FluentFTP;

namespace AtomBox.Providers.FileTransfer.Ftp;

internal sealed class FluentFtpClientAdapter : IFtpClientAdapter
{
    private readonly FtpClient _client;

    public FluentFtpClientAdapter(
        string host,
        int port,
        string username,
        string password,
        bool passiveMode,
        int timeoutSeconds)
    {
        _client = new FtpClient(host, username, password, port);
        Configure(passiveMode, timeoutSeconds);
    }

    public FluentFtpClientAdapter(string host, int port, bool passiveMode, int timeoutSeconds)
        : this(host, port, "anonymous", "anonymous@", passiveMode, timeoutSeconds)
    {
    }

    public bool IsConnected => _client.IsConnected;

    public void Connect()
    {
        if (!_client.IsConnected)
        {
            _client.Connect();
        }
    }

    public IReadOnlyList<FtpRemoteEntry> ListDirectory(string path)
    {
        return _client.GetListing(path)
            .Select(item => new FtpRemoteEntry(
                item.Name,
                item.Type == FtpObjectType.Directory,
                item.Type == FtpObjectType.File,
                item.Size,
                item.Modified))
            .ToArray();
    }

    public bool FileExists(string path)
    {
        return _client.FileExists(path);
    }

    public bool DirectoryExists(string path)
    {
        return _client.DirectoryExists(path);
    }

    public void CreateDirectory(string path)
    {
        _client.CreateDirectory(path, force: true);
    }

    public void DeleteFile(string path)
    {
        _client.DeleteFile(path);
    }

    public void DeleteDirectory(string path)
    {
        _client.DeleteDirectory(path);
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        _client.MoveFile(sourcePath, destinationPath, FtpRemoteExists.Skip);
    }

    public void MoveDirectory(string sourcePath, string destinationPath)
    {
        _client.MoveDirectory(sourcePath, destinationPath, FtpRemoteExists.Skip);
    }

    public void UploadFile(Stream content, string path, Action<long>? progress = null)
    {
        _client.UploadStream(
            content,
            path,
            FtpRemoteExists.Overwrite,
            createRemoteDir: true,
            progress: progress is null ? null : report => progress(report.TransferredBytes));
    }

    public void DownloadFile(string path, Stream destination, Action<long>? progress = null)
    {
        _client.DownloadStream(
            destination,
            path,
            progress: progress is null ? null : report => progress(report.TransferredBytes));
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private void Configure(bool passiveMode, int timeoutSeconds)
    {
        var timeoutMilliseconds = timeoutSeconds * 1000;
        _client.Config.DataConnectionType = passiveMode
            ? FtpDataConnectionType.AutoPassive
            : FtpDataConnectionType.AutoActive;
        _client.Config.ConnectTimeout = timeoutMilliseconds;
        _client.Config.ReadTimeout = timeoutMilliseconds;
        _client.Config.DataConnectionConnectTimeout = timeoutMilliseconds;
        _client.Config.DataConnectionReadTimeout = timeoutMilliseconds;
    }
}
