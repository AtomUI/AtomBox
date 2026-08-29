using FluentFTP;

namespace AtomBox.Providers.FileTransfer.Ftp;

internal sealed class FluentFtpClientAdapter : IFtpClientAdapter
{
    private readonly AsyncFtpClient _client;

    public FluentFtpClientAdapter(
        string host,
        int port,
        string username,
        string password,
        bool passiveMode,
        int timeoutSeconds)
    {
        _client = new AsyncFtpClient(host, username, password, port);
        Configure(passiveMode, timeoutSeconds);
    }

    public FluentFtpClientAdapter(string host, int port, bool passiveMode, int timeoutSeconds)
        : this(host, port, "anonymous", "anonymous@", passiveMode, timeoutSeconds)
    {
    }

    public bool IsConnected => _client.IsConnected;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (!_client.IsConnected)
        {
            await _client.Connect(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<FtpRemoteEntry>> ListDirectoryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var items = await _client.GetListing(path, cancellationToken).ConfigureAwait(false);
        return items
            .Select(item => new FtpRemoteEntry(
                item.Name,
                item.Type == FtpObjectType.Directory,
                item.Type == FtpObjectType.File,
                item.Size,
                item.Modified))
            .ToArray();
    }

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken)
    {
        return _client.FileExists(path, cancellationToken);
    }

    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken)
    {
        return _client.DirectoryExists(path, cancellationToken);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        return _client.CreateDirectory(path, force: true, cancellationToken);
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        return _client.DeleteFile(path, cancellationToken);
    }

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        return _client.DeleteDirectory(path, cancellationToken);
    }

    public async Task MoveFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var moved = await _client.MoveFile(
            sourcePath,
            destinationPath,
            overwrite ? FtpRemoteExists.Overwrite : FtpRemoteExists.Skip,
            cancellationToken).ConfigureAwait(false);
        if (!moved)
        {
            throw new InvalidOperationException("FTP server did not move the remote file.");
        }
    }

    public async Task MoveDirectoryAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var moved = await _client.MoveDirectory(
            sourcePath,
            destinationPath,
            FtpRemoteExists.Skip,
            cancellationToken).ConfigureAwait(false);
        if (!moved)
        {
            throw new InvalidOperationException("FTP server did not move the remote directory.");
        }
    }

    public async Task UploadFileAsync(Stream content, string path, CancellationToken cancellationToken)
    {
        var uploaded = await _client.UploadStream(
            content,
            path,
            FtpRemoteExists.Overwrite,
            createRemoteDir: true,
            progress: null,
            cancellationToken).ConfigureAwait(false);
        if (uploaded != FtpStatus.Success)
        {
            throw new InvalidOperationException("FTP server did not accept the uploaded file.");
        }
    }

    public async Task DownloadFileAsync(
        string path,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var downloaded = await _client.DownloadStream(
            destination,
            path,
            progress: null,
            token: cancellationToken).ConfigureAwait(false);
        if (!downloaded)
        {
            throw new InvalidOperationException("FTP server did not return the requested file.");
        }
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
