using Renci.SshNet;
using Renci.SshNet.Common;

namespace AtomBox.Providers.FileTransfer.Sftp;

internal sealed class SshNetSftpClientAdapter : ISftpClientAdapter
{
    private readonly SftpClient _client;
    private readonly IDisposable? _privateKeyOwner;
    private readonly string _hostKeyPolicy;
    private readonly string? _hostKeyFingerprint;

    public SshNetSftpClientAdapter(
        string host,
        int port,
        string username,
        string password,
        string hostKeyPolicy,
        string? hostKeyFingerprint,
        int timeoutSeconds)
    {
        _hostKeyPolicy = hostKeyPolicy;
        _hostKeyFingerprint = hostKeyFingerprint;
        _client = new SftpClient(host, port, username, password);
        ConfigureTimeout(timeoutSeconds);
        ConfigureHostKeyValidation();
    }

    public SshNetSftpClientAdapter(
        string host,
        int port,
        string username,
        string privateKey,
        string? privateKeyPassphrase,
        string hostKeyPolicy,
        string? hostKeyFingerprint,
        int timeoutSeconds)
    {
        _hostKeyPolicy = hostKeyPolicy;
        _hostKeyFingerprint = hostKeyFingerprint;
        var privateKeyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKey));
        _privateKeyOwner = privateKeyStream;
        var privateKeyFile = string.IsNullOrWhiteSpace(privateKeyPassphrase)
            ? new PrivateKeyFile(privateKeyStream)
            : new PrivateKeyFile(privateKeyStream, privateKeyPassphrase);
        var connectionInfo = new ConnectionInfo(
            host,
            port,
            username,
            new PrivateKeyAuthenticationMethod(username, privateKeyFile));
        connectionInfo.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        _client = new SftpClient(connectionInfo);
        ConfigureTimeout(timeoutSeconds);
        ConfigureHostKeyValidation();
    }

    public bool IsConnected => _client.IsConnected;

    public string? WorkingDirectory => _client.IsConnected ? _client.WorkingDirectory : null;

    public void Connect()
    {
        if (!_client.IsConnected)
        {
            _client.Connect();
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_client.IsConnected)
        {
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public IReadOnlyList<SftpRemoteEntry> ListDirectory(string path)
    {
        return _client.ListDirectory(path)
            .Select(item => new SftpRemoteEntry(
                item.Name,
                item.IsDirectory,
                item.IsRegularFile,
                item.Length,
                item.LastWriteTime))
            .ToArray();
    }

    public bool FileExists(string path)
    {
        return _client.Exists(path) && _client.GetAttributes(path).IsRegularFile;
    }

    public async Task<bool> FileExistsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!await _client.ExistsAsync(path, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var attributes = await _client.GetAttributesAsync(path, cancellationToken).ConfigureAwait(false);
        return attributes.IsRegularFile;
    }

    public bool DirectoryExists(string path)
    {
        return _client.Exists(path) && _client.GetAttributes(path).IsDirectory;
    }

    public async Task<bool> DirectoryExistsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!await _client.ExistsAsync(path, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var attributes = await _client.GetAttributesAsync(path, cancellationToken).ConfigureAwait(false);
        return attributes.IsDirectory;
    }

    public void CreateDirectory(string path)
    {
        if (!_client.Exists(path))
        {
            _client.CreateDirectory(path);
        }
    }

    public async Task CreateDirectoryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!await _client.ExistsAsync(path, cancellationToken).ConfigureAwait(false))
        {
            await _client.CreateDirectoryAsync(path, cancellationToken).ConfigureAwait(false);
        }
    }

    public void DeleteFile(string path)
    {
        _client.DeleteFile(path);
    }

    public void DeleteDirectory(string path)
    {
        _client.DeleteDirectory(path);
    }

    public void Move(string sourcePath, string destinationPath)
    {
        _client.RenameFile(sourcePath, destinationPath);
    }

    public Task UploadFileAsync(
        Stream content,
        string path,
        Action<ulong>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IProgress<UploadFileProgressReport>? progressAdapter = progress is null
            ? null
            : new InlineProgress<UploadFileProgressReport>(
                report => progress(report.TotalBytesUploaded));
        return _client.UploadFileAsync(
            content,
            path,
            canOverride: true,
            uploadProgress: progressAdapter,
            cancellationToken: cancellationToken);
    }

    public Task DownloadFileAsync(
        string path,
        Stream destination,
        Action<ulong>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IProgress<DownloadFileProgressReport>? progressAdapter = progress is null
            ? null
            : new InlineProgress<DownloadFileProgressReport>(
                report => progress(report.TotalBytesDownloaded));
        return _client.DownloadFileAsync(
            path,
            destination,
            downloadProgress: progressAdapter,
            cancellationToken: cancellationToken);
    }

    public void Dispose()
    {
        _client.Dispose();
        _privateKeyOwner?.Dispose();
    }

    private void ConfigureHostKeyValidation()
    {
        _client.HostKeyReceived += (_, args) =>
        {
            if (string.Equals(_hostKeyPolicy, "acceptAny", StringComparison.OrdinalIgnoreCase))
            {
                args.CanTrust = true;
                return;
            }

            if (!string.Equals(_hostKeyPolicy, "fingerprint", StringComparison.OrdinalIgnoreCase))
            {
                args.CanTrust = false;
                return;
            }

            args.CanTrust =
                FingerprintMatches(args.FingerPrintSHA256, _hostKeyFingerprint) ||
                FingerprintMatches(args.FingerPrintMD5, _hostKeyFingerprint) ||
                FingerprintMatches(Convert.ToHexString(args.FingerPrint), _hostKeyFingerprint);
        };
    }

    private void ConfigureTimeout(int timeoutSeconds)
    {
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        _client.ConnectionInfo.Timeout = timeout;
        _client.OperationTimeout = timeout;
    }

    private static bool FingerprintMatches(string? actual, string? expected)
    {
        return !string.IsNullOrWhiteSpace(actual) &&
            !string.IsNullOrWhiteSpace(expected) &&
            string.Equals(NormalizeFingerprint(actual), NormalizeFingerprint(expected), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFingerprint(string value)
    {
        var normalized = value.Trim();
        foreach (var prefix in new[] { "SHA256:", "MD5:" })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        return normalized
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }
}
