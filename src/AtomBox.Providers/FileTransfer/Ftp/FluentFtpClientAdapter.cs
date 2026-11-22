using FluentFTP;
using FluentFTP.Exceptions;

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

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_client.IsConnected)
        {
            await _client.Connect(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<FtpRemoteEntry>> ListDirectoryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var entries = await _client.GetListing(path, cancellationToken).ConfigureAwait(false);
        return entries
            .Select(item => new FtpRemoteEntry(
                item.Name,
                item.Type == FtpObjectType.Directory,
                item.Type == FtpObjectType.File,
                item.Size,
                item.Modified))
            .ToArray();
    }

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        return _client.FileExists(path, cancellationToken);
    }

    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        return _client.DirectoryExists(path, cancellationToken);
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        await _client.CreateDirectory(path, force: true, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        return _client.DeleteFile(path, cancellationToken);
    }

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        return _client.DeleteDirectory(path, cancellationToken);
    }

    public async Task MoveFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var moved = await _client.MoveFile(
            sourcePath,
            destinationPath,
            FtpRemoteExists.Skip,
            cancellationToken).ConfigureAwait(false);
        if (!moved)
        {
            throw new FtpException("FTP file move was not completed.");
        }
    }

    public async Task MoveDirectoryAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var moved = await _client.MoveDirectory(
            sourcePath,
            destinationPath,
            FtpRemoteExists.Skip,
            cancellationToken).ConfigureAwait(false);
        if (!moved)
        {
            throw new FtpException("FTP directory move was not completed.");
        }
    }

    public async Task UploadFileAsync(
        Stream content,
        string path,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IProgress<FtpProgress>? progressAdapter = progress is null
            ? null
            : new InlineProgress<FtpProgress>(report => progress(report.TransferredBytes));
        var status = await _client.UploadStream(
            content,
            path,
            FtpRemoteExists.Overwrite,
            createRemoteDir: true,
            progress: progressAdapter,
            token: cancellationToken).ConfigureAwait(false);
        if (status != FtpStatus.Success)
        {
            throw new FtpException($"FTP upload finished with status {status}.");
        }
    }

    public async Task DownloadFileAsync(
        string path,
        Stream destination,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IProgress<FtpProgress>? progressAdapter = progress is null
            ? null
            : new InlineProgress<FtpProgress>(report => progress(report.TransferredBytes));
        var downloaded = await _client.DownloadStream(
            destination,
            path,
            progress: progressAdapter,
            token: cancellationToken).ConfigureAwait(false);
        if (!downloaded)
        {
            throw new FtpException("FTP download was not completed.");
        }
    }

    public ValueTask DisposeAsync()
    {
        return _client.DisposeAsync();
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

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }
}
