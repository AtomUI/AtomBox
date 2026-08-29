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

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (!_client.IsConnected)
        {
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<SftpRemoteEntry>> ListDirectoryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var entries = new List<SftpRemoteEntry>();
        await foreach (var item in _client.ListDirectoryAsync(path, cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new SftpRemoteEntry(
                item.Name,
                item.IsDirectory,
                item.IsRegularFile,
                item.Length,
                item.LastWriteTime));
        }

        return entries;
    }

    public async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken)
    {
        return await _client.ExistsAsync(path, cancellationToken).ConfigureAwait(false) &&
               (await _client.GetAttributesAsync(path, cancellationToken).ConfigureAwait(false)).IsRegularFile;
    }

    public async Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken)
    {
        return await _client.ExistsAsync(path, cancellationToken).ConfigureAwait(false) &&
               (await _client.GetAttributesAsync(path, cancellationToken).ConfigureAwait(false)).IsDirectory;
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        if (!await _client.ExistsAsync(path, cancellationToken).ConfigureAwait(false))
        {
            await _client.CreateDirectoryAsync(path, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        return _client.DeleteFileAsync(path, cancellationToken);
    }

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        return _client.DeleteDirectoryAsync(path, cancellationToken);
    }

    public Task MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        return _client.RenameFileAsync(sourcePath, destinationPath, cancellationToken);
    }

    public Task UploadFileAsync(Stream content, string path, CancellationToken cancellationToken)
    {
        return _client.UploadFileAsync(content, path, cancellationToken);
    }

    public Task DownloadFileAsync(
        string path,
        Stream destination,
        CancellationToken cancellationToken)
    {
        return _client.DownloadFileAsync(path, destination, cancellationToken);
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
}
