using AtomBox.Application.Accounts;
using AtomBox.Application.DependencyInjection;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.DependencyInjection;
using AtomBox.Infrastructure.Storage;
using AtomBox.Providers.Common;
using AtomBox.Providers.DependencyInjection;
using AtomBox.Transfer.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AtomBox.EndToEnd.Tests;

internal sealed class HeadlessTransferCancellationContext : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly string _stateRoot;

    private HeadlessTransferCancellationContext(
        HeadlessTransferProviderEnvironment environment,
        ServiceProvider services,
        string runId,
        string artifactRoot,
        string stateRoot,
        StorageAccount account)
    {
        Environment = environment;
        _services = services;
        RunId = runId;
        ArtifactRoot = artifactRoot;
        _stateRoot = stateRoot;
        Account = account;
    }

    public HeadlessTransferProviderEnvironment Environment { get; }

    public string RunId { get; }

    public string ArtifactRoot { get; }

    public StorageAccount Account { get; }

    public RemotePath RunRoot => Environment.CreateRunRoot(RunId);

    public T GetRequiredService<T>() where T : notnull => _services.GetRequiredService<T>();

    public static async Task<HeadlessTransferCancellationContext?> CreateAsync(string providerKey)
    {
        var environment = HeadlessTransferProviderEnvironment.TryRead(providerKey);
        if (environment is null)
        {
            return null;
        }

        var runId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var artifactRoot = Path.Combine(
            System.Environment.CurrentDirectory,
            ".artifacts",
            "headless-transfer-cancellation",
            providerKey,
            runId);
        var stateRoot = Path.Combine(Path.GetTempPath(), "AtomBox", "headless-transfer-cancellation", providerKey, runId);
        Directory.CreateDirectory(artifactRoot);

        var services = new ServiceCollection();
        services.AddAtomBoxInfrastructure();
        services.AddSingleton(new AtomBoxStoragePaths(stateRoot));
        services.AddAtomBoxProviders();
        services.AddAtomBoxTransfer();
        services.AddAtomBoxApplication();
        services.AddSingleton<ILocalTransferFileStore>(_ =>
            new DelayedLocalTransferFileStore(new LocalTransferFileStore(), TimeSpan.FromMilliseconds(12)));
        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        var initialize = serviceProvider.GetRequiredService<InfrastructureInitializer>().Initialize();
        AssertSuccess(initialize, "Initialize isolated Infrastructure state");

        var credentials = serviceProvider.GetRequiredService<ICredentialStore>();
        var credential = await credentials.SaveAsync(new CredentialSecretMaterial(environment.Secrets));
        AssertSuccess(credential, $"Persist {environment.DisplayName} credentials");

        var accountResult = await serviceProvider.GetRequiredService<AccountAppService>().AddAsync(
            new AddStorageAccountRequest(
                environment.Category,
                environment.ProviderId,
                $"{environment.DisplayName} headless cancellation {runId}",
                environment.Endpoint,
                environment.Region,
                credential.GetValueOrThrow(),
                environment.ProviderConfig));
        AssertSuccess(accountResult, $"Persist {environment.DisplayName} account");

        var account = await serviceProvider.GetRequiredService<IStorageAccountRepository>()
            .GetByIdAsync(accountResult.GetValueOrThrow().Id);
        AssertSuccess(account, $"Reload {environment.DisplayName} account");

        var settings = new ApplicationSettings(
            1,
            TransferOverwritePolicy.Overwrite,
            KeepCompletedTransfers: true,
            EnableUploadFingerprintIndex: false);
        AssertSuccess(
            await serviceProvider.GetRequiredService<IApplicationSettingsRepository>().SaveAsync(settings),
            "Set transfer concurrency to one");

        var context = new HeadlessTransferCancellationContext(
            environment,
            serviceProvider,
            runId,
            artifactRoot,
            stateRoot,
            account.GetValueOrThrow());
        await context.EnsureRunRootAsync();
        return context;
    }

    public RemotePath RemoteObject(string name)
    {
        return RunRoot.Combine(name.TrimStart('/'), RemotePathKind.ObjectPath);
    }

    public string LocalFile(string name)
    {
        return Path.Combine(ArtifactRoot, name);
    }

    public async Task<IStorageProvider> CreateProviderAsync()
    {
        var result = await GetRequiredService<IStorageProviderFactory>().CreateAsync(Account);
        AssertSuccess(result, $"Create {Environment.DisplayName} provider");
        return result.GetValueOrThrow();
    }

    public async Task WritePatternFileAsync(string path, int length)
    {
        var buffer = new byte[64 * 1024];
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        var remaining = length;
        var offset = 0L;
        while (remaining > 0)
        {
            var count = Math.Min(buffer.Length, remaining);
            for (var index = 0; index < count; index++)
            {
                buffer[index] = (byte)((offset + index) % 251);
            }

            await stream.WriteAsync(buffer.AsMemory(0, count));
            remaining -= count;
            offset += count;
        }
    }

    public async Task UploadRemoteAsync(RemotePath path, int length)
    {
        await using var provider = await CreateProviderAsync();
        await using var stream = new PatternReadStream(length);
        var result = await provider.UploadAsync(path, stream, length);
        AssertSuccess(result, $"Prepare remote object {path.Value}");
    }

    public async Task<bool> RemoteObjectExistsAsync(RemotePath path)
    {
        await using var provider = await CreateProviderAsync();
        var parent = path.GetParent() ?? RemotePath.Root;
        var list = await provider.ListAsync(parent);
        if (list.IsFailure &&
            list.Error?.Category == AtomBox.Core.Errors.StorageErrorCategory.NotFound)
        {
            return false;
        }

        AssertSuccess(list, $"List parent of {path.Value}");
        return list.GetValueOrThrow().Any(item => item.Path == path || item.Name == path.Name);
    }

    public async Task WriteEvidenceAsync(IReadOnlyDictionary<string, object?> evidence)
    {
        var payload = new Dictionary<string, object?>(evidence)
        {
            ["provider"] = Environment.ProviderKey,
            ["runId"] = RunId,
            ["completedAtUtc"] = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(
            Path.Combine(ArtifactRoot, "evidence.json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var provider = await CreateProviderAsync();
            await provider.DeleteAsync(RunRoot);
        }
        catch
        {
        }

        await _services.DisposeAsync();
        if (Directory.Exists(_stateRoot))
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
    }

    internal static void AssertSuccess(OperationResult result, string operation)
    {
        Assert.True(result.IsSuccess, FormatError(operation, result.Error));
    }

    internal static void AssertSuccess<T>(OperationResult<T> result, string operation)
    {
        Assert.True(result.IsSuccess, FormatError(operation, result.Error));
    }

    private async Task EnsureRunRootAsync()
    {
        await using var provider = await CreateProviderAsync();
        if (Environment.ProviderKey == "generic-s3")
        {
            var bucketList = await provider.ListAsync(RemotePath.Root);
            AssertSuccess(bucketList, "List Generic S3 buckets");
            if (!bucketList.GetValueOrThrow().Any(item => item.Name == Environment.Bucket))
            {
                AssertSuccess(
                    await provider.CreateFolderAsync(new RemotePath(Environment.Bucket!, RemotePathKind.BucketRoot)),
                    "Create Generic S3 test bucket");
            }
        }

        if (Environment.Category == StorageProviderCategory.FileTransfer)
        {
            var current = string.Empty;
            foreach (var segment in RunRoot.Value.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                current = string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";
                var create = await provider.CreateFolderAsync(new RemotePath(current, RemotePathKind.Folder));
                if (create.IsFailure && create.Error?.Category != AtomBox.Core.Errors.StorageErrorCategory.Conflict)
                {
                    AssertSuccess(create, $"Create {Environment.DisplayName} test folder {current}");
                }
            }
        }
        else
        {
            AssertSuccess(await provider.CreateFolderAsync(RunRoot), $"Create {Environment.DisplayName} test root");
        }
    }

    private static string FormatError(string operation, AtomBox.Core.Errors.StorageError? error)
    {
        return error is null
            ? $"{operation} failed."
            : $"{operation} failed. Category={error.Category}; Code={error.Code}; ProviderCode={error.ProviderErrorCode}; Retryable={error.IsRetryable}; Message={error.Message}";
    }

    private sealed class DelayedLocalTransferFileStore : ILocalTransferFileStore
    {
        private readonly ILocalTransferFileStore _inner;
        private readonly TimeSpan _delay;

        public DelayedLocalTransferFileStore(ILocalTransferFileStore inner, TimeSpan delay)
        {
            _inner = inner;
            _delay = delay;
        }

        public async Task<OperationResult<LocalTransferReadHandle>> OpenReadAsync(
            LocalPath path,
            CancellationToken cancellationToken = default)
        {
            var result = await _inner.OpenReadAsync(path, cancellationToken);
            if (result.IsFailure)
            {
                return OperationResult<LocalTransferReadHandle>.Failure(result.Error!);
            }

            var handle = result.GetValueOrThrow();
            return OperationResult<LocalTransferReadHandle>.Success(
                new LocalTransferReadHandle(new DelayedStream(handle.Stream, _delay), handle.Length));
        }

        public Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
            LocalPath path,
            CancellationToken cancellationToken = default)
        {
            return OpenWriteAsync(path, TransferOverwritePolicy.Skip, cancellationToken);
        }

        public async Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
            LocalPath path,
            TransferOverwritePolicy overwritePolicy,
            CancellationToken cancellationToken = default)
        {
            var result = await _inner.OpenWriteAsync(path, overwritePolicy, cancellationToken);
            if (result.IsFailure)
            {
                return OperationResult<LocalTransferWriteHandle>.Failure(result.Error!);
            }

            var handle = result.GetValueOrThrow();
            return OperationResult<LocalTransferWriteHandle>.Success(new LocalTransferWriteHandle(
                new DelayedStream(handle.Stream, _delay),
                handle.Path,
                handle.CommitAsync,
                handle.AbortAsync));
        }
    }

    private sealed class DelayedStream : Stream
    {
        private readonly Stream _inner;
        private readonly TimeSpan _delay;

        public DelayedStream(Stream inner, TimeSpan delay)
        {
            _inner = inner;
            _delay = delay;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count)
        {
            Thread.Sleep(_delay);
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            Thread.Sleep(_delay);
            return _inner.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken);
            return await _inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Thread.Sleep(_delay);
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Thread.Sleep(_delay);
            _inner.Write(buffer);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken);
            await _inner.WriteAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class PatternReadStream : Stream
    {
        private readonly long _length;
        private long _position;

        public PatternReadStream(long length) => _length = length;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _position; set => _position = Math.Clamp(value, 0, _length); }
        public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => Fill(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Fill(buffer.Span));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Fill(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, _length - _position);
            for (var index = 0; index < count; index++)
            {
                buffer[index] = (byte)((_position + index) % 251);
            }
            _position += count;
            return count;
        }
    }
}

internal sealed record HeadlessTransferProviderEnvironment(
    string ProviderKey,
    string DisplayName,
    StorageProviderId ProviderId,
    StorageProviderCategory Category,
    string Endpoint,
    string? Region,
    string? Bucket,
    string TestPrefix,
    IReadOnlyDictionary<string, string> ProviderConfig,
    IReadOnlyDictionary<string, string> Secrets,
    bool SupportsUploadCancellation,
    bool SupportsMultipartCancellation)
{
    public RemotePath CreateRunRoot(string runId)
    {
        var prefix = $"{TestPrefix.Trim('/')}/queue-cancel-{runId}";
        return Category == StorageProviderCategory.ObjectStorage
            ? new RemotePath($"{Bucket}/{prefix}", RemotePathKind.Folder)
            : new RemotePath(prefix, RemotePathKind.Folder);
    }

    public static HeadlessTransferProviderEnvironment? TryRead(string providerKey)
    {
        return providerKey switch
        {
            "generic-s3" => ReadObjectStorage(
                providerKey, "Generic S3", StorageProviderRegistry.S3CompatibleProviderId,
                "ATOMBOX_TEST_GENERIC_S3", "ATOMBOX_GENERIC_S3_ENDPOINT", "ATOMBOX_GENERIC_S3_REGION",
                "ATOMBOX_GENERIC_S3_BUCKET", "ATOMBOX_GENERIC_S3_ACCESS_KEY_ID",
                "ATOMBOX_GENERIC_S3_ACCESS_KEY_SECRET", "ATOMBOX_GENERIC_S3_TEST_PREFIX", true, true),
            "tencent-cos" => ReadObjectStorage(
                providerKey, "Tencent COS", StorageProviderRegistry.TencentCosProviderId,
                "ATOMBOX_TEST_TENCENT_COS", "ATOMBOX_COS_ENDPOINT", "ATOMBOX_COS_REGION",
                "ATOMBOX_COS_BUCKET", "ATOMBOX_COS_SECRET_ID", "ATOMBOX_COS_SECRET_KEY",
                "ATOMBOX_COS_TEST_PREFIX", true, true),
            "qiniu-kodo" => ReadObjectStorage(
                providerKey, "Qiniu Kodo", StorageProviderRegistry.QiniuKodoProviderId,
                "ATOMBOX_TEST_QINIU_KODO", "ATOMBOX_QINIU_KODO_ENDPOINT", "ATOMBOX_QINIU_KODO_REGION",
                "ATOMBOX_QINIU_KODO_BUCKET", "ATOMBOX_QINIU_KODO_ACCESS_KEY",
                "ATOMBOX_QINIU_KODO_SECRET_KEY", "ATOMBOX_QINIU_KODO_TEST_PREFIX", false, false),
            "ftp" => ReadFtp(),
            "sftp" => ReadSftp(),
            "webdav" => ReadWebDav(),
            _ => throw new ArgumentOutOfRangeException(nameof(providerKey), providerKey, "Unknown headless provider key.")
        };
    }

    private static HeadlessTransferProviderEnvironment? ReadObjectStorage(
        string key, string displayName, StorageProviderId providerId,
        string enabledName, string endpointName, string regionName, string bucketName,
        string accessKeyName, string secretKeyName, string prefixName,
        bool supportsUploadCancellation, bool supportsMultipartCancellation)
    {
        if (!Enabled(enabledName)) return null;
        var endpoint = Value(endpointName);
        var region = Value(regionName);
        var bucket = Value(bucketName);
        var accessKey = Value(accessKeyName);
        var secretKey = Value(secretKeyName);
        if (endpoint is null || region is null || bucket is null || accessKey is null || secretKey is null) return null;
        return new HeadlessTransferProviderEnvironment(
            key, displayName, providerId, StorageProviderCategory.ObjectStorage,
            endpoint, region, bucket, Prefix(prefixName),
            new Dictionary<string, string> { ["bucket"] = bucket, ["region"] = region },
            new Dictionary<string, string> { ["accessKeyId"] = accessKey, ["accessKeySecret"] = secretKey },
            supportsUploadCancellation, supportsMultipartCancellation);
    }

    private static HeadlessTransferProviderEnvironment? ReadFtp()
    {
        if (!Enabled("ATOMBOX_TEST_FTP")) return null;
        var host = Value("ATOMBOX_FTP_HOST");
        var authMode = Value("ATOMBOX_FTP_AUTH_MODE") ?? "password";
        if (host is null) return null;
        var secrets = new Dictionary<string, string> { ["authMode"] = authMode };
        if (!string.Equals(authMode, "anonymous", StringComparison.OrdinalIgnoreCase))
        {
            var username = Value("ATOMBOX_FTP_USERNAME");
            var password = Value("ATOMBOX_FTP_PASSWORD");
            if (username is null || password is null) return null;
            secrets["username"] = username;
            secrets["password"] = password;
        }
        return new HeadlessTransferProviderEnvironment(
            "ftp", "FTP", StorageProviderRegistry.FtpProviderId, StorageProviderCategory.FileTransfer,
            host, null, null, Prefix("ATOMBOX_FTP_TEST_PREFIX"),
            new Dictionary<string, string>
            {
                ["port"] = Value("ATOMBOX_FTP_PORT") ?? "21",
                ["rootPath"] = Value("ATOMBOX_FTP_ROOT_PATH") ?? "/",
                ["authMode"] = authMode,
                ["transferMode"] = Value("ATOMBOX_FTP_TRANSFER_MODE") ?? "passive",
                ["timeoutSeconds"] = Value("ATOMBOX_FTP_TIMEOUT_SECONDS") ?? "30"
            },
            secrets, true, false);
    }

    private static HeadlessTransferProviderEnvironment? ReadSftp()
    {
        if (!Enabled("ATOMBOX_TEST_SFTP")) return null;
        var host = Value("ATOMBOX_SFTP_HOST");
        var username = Value("ATOMBOX_SFTP_USERNAME");
        var authMode = Value("ATOMBOX_SFTP_AUTH_MODE") ?? "password";
        if (host is null || username is null) return null;
        var secrets = new Dictionary<string, string> { ["authMode"] = authMode, ["username"] = username };
        if (string.Equals(authMode, "privateKey", StringComparison.OrdinalIgnoreCase))
        {
            var privateKey = Value("ATOMBOX_SFTP_PRIVATE_KEY");
            if (privateKey is null) return null;
            secrets["privateKey"] = privateKey;
            var passphrase = Value("ATOMBOX_SFTP_PRIVATE_KEY_PASSPHRASE");
            if (passphrase is not null) secrets["privateKeyPassphrase"] = passphrase;
        }
        else
        {
            var password = Value("ATOMBOX_SFTP_PASSWORD");
            if (password is null) return null;
            secrets["password"] = password;
        }
        return new HeadlessTransferProviderEnvironment(
            "sftp", "SFTP", StorageProviderRegistry.SftpProviderId, StorageProviderCategory.FileTransfer,
            host, null, null, Prefix("ATOMBOX_SFTP_TEST_PREFIX"),
            new Dictionary<string, string>
            {
                ["port"] = Value("ATOMBOX_SFTP_PORT") ?? "22",
                ["rootPath"] = Value("ATOMBOX_SFTP_ROOT_PATH") ?? "/",
                ["authMode"] = authMode,
                ["hostKeyPolicy"] = Value("ATOMBOX_SFTP_HOST_KEY_POLICY") ?? "acceptAny",
                ["hostKeyFingerprint"] = Value("ATOMBOX_SFTP_HOST_KEY_FINGERPRINT") ?? string.Empty,
                ["timeoutSeconds"] = Value("ATOMBOX_SFTP_TIMEOUT_SECONDS") ?? "30"
            },
            secrets, true, false);
    }

    private static HeadlessTransferProviderEnvironment? ReadWebDav()
    {
        if (!Enabled("ATOMBOX_TEST_WEBDAV")) return null;
        var endpoint = Value("ATOMBOX_WEBDAV_ENDPOINT");
        var authMode = Value("ATOMBOX_WEBDAV_AUTH_MODE") ?? "password";
        if (endpoint is null) return null;
        var secrets = new Dictionary<string, string> { ["authMode"] = authMode };
        if (!string.Equals(authMode, "anonymous", StringComparison.OrdinalIgnoreCase))
        {
            var username = Value("ATOMBOX_WEBDAV_USERNAME");
            var password = Value("ATOMBOX_WEBDAV_PASSWORD");
            if (username is null || password is null) return null;
            secrets["username"] = username;
            secrets["password"] = password;
        }
        return new HeadlessTransferProviderEnvironment(
            "webdav", "WebDAV", StorageProviderRegistry.WebDavProviderId, StorageProviderCategory.FileTransfer,
            endpoint, null, null, Prefix("ATOMBOX_WEBDAV_TEST_PREFIX"),
            new Dictionary<string, string>
            {
                ["rootPath"] = Value("ATOMBOX_WEBDAV_ROOT_PATH") ?? "/",
                ["authMode"] = authMode,
                ["timeoutSeconds"] = Value("ATOMBOX_WEBDAV_TIMEOUT_SECONDS") ?? "30"
            },
            secrets, true, false);
    }

    private static bool Enabled(string name) => string.Equals(Value(name), "1", StringComparison.Ordinal);
    private static string Prefix(string name) => (Value(name) ?? "atombox-tests").Trim('/').Replace('\\', '/');
    private static string? Value(string name)
    {
        var value = System.Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
