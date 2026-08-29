using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.FileTransfer.Sftp;

namespace AtomBox.Providers.Tests;

public sealed class SftpStorageProviderTests
{
    [Fact]
    public async Task ListAsync_MapsFoldersAndFiles()
    {
        var client = new FakeSftpClient
        {
            Entries =
            [
                new SftpRemoteEntry(".", true, false, 0, DateTime.UtcNow),
                new SftpRemoteEntry("docs", true, false, 0, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
                new SftpRemoteEntry("readme.txt", false, true, 128, new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc))
            ]
        };
        await using var provider = CreateProvider(client, rootPath: "/home/user");

        var result = await provider.ListAsync(RemotePath.Root);

        Assert.True(result.IsSuccess);
        Assert.Equal("/home/user", client.LastListPath);
        Assert.Collection(
            result.GetValueOrThrow(),
            folder =>
            {
                Assert.Equal("docs", folder.Name);
                Assert.Equal(RemoteItemKind.Folder, folder.Kind);
                Assert.Equal("docs", folder.Path.Value);
            },
            file =>
            {
                Assert.Equal("readme.txt", file.Name);
                Assert.Equal(RemoteItemKind.File, file.Kind);
                Assert.Equal(128, file.Size);
            });
    }

    [Fact]
    public async Task HomePath_ReturnsConnectedWorkingDirectory()
    {
        var client = new FakeSftpClient
        {
            WorkingDirectory = "/home/demo"
        };
        await using var provider = CreateProvider(client);

        Assert.Equal("/home/demo", provider.HomePath);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task UploadDownloadAndDelete_UseMappedRootPath()
    {
        var client = new FakeSftpClient
        {
            DownloadPayload = [5, 6, 7],
            ExistingFiles = ["/data/folder/file.txt"],
            ExistingDirectories = ["/data", "/data/folder"]
        };
        await using var provider = CreateProvider(client, rootPath: "/data");
        await using var content = new MemoryStream([1, 2, 3]);
        await using var destination = new MemoryStream();
        var progress = new ProgressRecorder();

        var upload = await provider.UploadAsync(new RemotePath("folder/file.txt"), content, content.Length, progress);
        var download = await provider.DownloadAsync(new RemotePath("folder/file.txt"), destination, progress);
        var delete = await provider.DeleteAsync(new RemotePath("folder/file.txt"));

        Assert.True(upload.IsSuccess);
        Assert.True(download.IsSuccess);
        Assert.True(delete.IsSuccess);
        Assert.Equal("/data/folder/file.txt", client.UploadPath);
        Assert.Equal("/data/folder/file.txt", client.DownloadPath);
        Assert.Equal("/data/folder/file.txt", client.DeletePath);
        Assert.Equal([1, 2, 3], client.UploadPayload);
        Assert.Equal([5, 6, 7], destination.ToArray());
        Assert.NotNull(progress.Latest);
    }

    [Fact]
    public async Task UploadAsync_CreatesMissingParentDirectories()
    {
        var client = new FakeSftpClient
        {
            ExistingDirectories = ["/data"]
        };
        await using var provider = CreateProvider(client, rootPath: "/data");
        await using var content = new MemoryStream([1]);

        var result = await provider.UploadAsync(new RemotePath("a/b/file.txt"), content, content.Length);

        Assert.True(result.IsSuccess);
        Assert.Equal(["/data/a", "/data/a/b"], client.CreatedDirectories);
    }

    [Fact]
    public async Task UploadAsync_CanceledUpload_RemovesTemporaryRemoteFile()
    {
        var client = new FakeSftpClient
        {
            ExistingDirectories = ["/data"],
            BlockUploadUntilCanceled = true
        };
        await using var provider = CreateProvider(client, rootPath: "/data");
        await using var content = new MemoryStream(new byte[1024]);
        using var cancellation = new CancellationTokenSource();

        var uploadTask = provider.UploadAsync(
            new RemotePath("file.bin"),
            content,
            content.Length,
            cancellationToken: cancellation.Token);
        await client.UploadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();
        var result = await uploadTask;

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Canceled, result.Error?.Category);
        Assert.Equal(client.UploadedTemporaryPath, client.DeletePath);
        Assert.Null(client.UploadPath);
    }

    [Fact]
    public async Task UploadAsync_AllowsCurrentWorkingDirectoryRootPath()
    {
        var client = new FakeSftpClient
        {
            ExistingDirectories = ["a"]
        };
        await using var provider = CreateProvider(client, rootPath: ".");
        await using var content = new MemoryStream([1]);

        var result = await provider.UploadAsync(new RemotePath("a/file.txt"), content, content.Length);

        Assert.True(result.IsSuccess);
        Assert.Equal("a/file.txt", client.UploadPath);
    }

    [Fact]
    public async Task UploadAsync_UsesSlashRootPathWithoutDuplicatingSeparators()
    {
        var client = new FakeSftpClient
        {
            ExistingDirectories = ["/folder"]
        };
        await using var provider = CreateProvider(client, rootPath: "/");
        await using var content = new MemoryStream([1]);

        var result = await provider.UploadAsync(new RemotePath("folder/file.txt"), content, content.Length);

        Assert.True(result.IsSuccess);
        Assert.Equal("/folder/file.txt", client.UploadPath);
    }

    [Fact]
    public async Task UploadAsync_PreservesSpecialCharacterPathSegments()
    {
        var client = new FakeSftpClient
        {
            ExistingDirectories = ["/data", "/data/中文 folder"]
        };
        await using var provider = CreateProvider(client, rootPath: "/data");
        await using var content = new MemoryStream([1]);

        var result = await provider.UploadAsync(new RemotePath("中文 folder/file #1 + test.txt"), content, content.Length);

        Assert.True(result.IsSuccess);
        Assert.Equal("/data/中文 folder/file #1 + test.txt", client.UploadPath);
    }

    [Fact]
    public async Task DeleteAsync_UsesDirectoryDelete_WhenPathIsFolder()
    {
        var client = new FakeSftpClient
        {
            ExistingDirectories = ["/data/folder"]
        };
        await using var provider = CreateProvider(client, rootPath: "/data");

        var result = await provider.DeleteAsync(new RemotePath("folder", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal("/data/folder", client.DeletedDirectoryPath);
        Assert.Null(client.DeletePath);
    }

    [Fact]
    public async Task CreateFolderAsync_CreatesMissingFolderAndParents()
    {
        var client = new FakeSftpClient
        {
            ExistingDirectories = ["/data"]
        };
        await using var provider = CreateProvider(client, rootPath: "/data");

        var result = await provider.CreateFolderAsync(new RemotePath("a/b", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal(["/data/a", "/data/a/b"], client.CreatedDirectories);
    }

    [Fact]
    public async Task RenameAsync_MovesFileWithinSameParent()
    {
        var client = new FakeSftpClient
        {
            ExistingDirectories = ["/data", "/data/folder"],
            ExistingFiles = ["/data/folder/old.txt"]
        };
        await using var provider = CreateProvider(client, rootPath: "/data");

        var result = await provider.RenameAsync(new RemotePath("folder/old.txt"), "new.txt");

        Assert.True(result.IsSuccess);
        Assert.Equal(("/data/folder/old.txt", "/data/folder/new.txt"), client.MovedPath);
    }

    [Fact]
    public async Task MoveAsync_MovesFolderAndCreatesDestinationParent()
    {
        var client = new FakeSftpClient
        {
            ExistingDirectories = ["/data", "/data/source"]
        };
        await using var provider = CreateProvider(client, rootPath: "/data");

        var result = await provider.MoveAsync(
            new RemotePath("source", RemotePathKind.Folder),
            new RemotePath("target/source", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal(["/data/target"], client.CreatedDirectories);
        Assert.Equal(("/data/source", "/data/target/source"), client.MovedPath);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsValidation_WhenPathIsFolder()
    {
        var client = new FakeSftpClient
        {
            ExistingDirectories = ["/data/folder"]
        };
        await using var provider = CreateProvider(client, rootPath: "/data");
        await using var destination = new MemoryStream();

        var result = await provider.DownloadAsync(new RemotePath("folder", RemotePathKind.Folder), destination);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
        Assert.Null(client.DownloadPath);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsNotFound_WhenFileDoesNotExist()
    {
        var client = new FakeSftpClient();
        await using var provider = CreateProvider(client, rootPath: "/data");
        await using var destination = new MemoryStream();

        var result = await provider.DownloadAsync(new RemotePath("missing.txt"), destination);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
        Assert.Null(client.DownloadPath);
    }

    [Fact]
    public async Task Creator_ReturnsValidationFailureAndReleasesLease_WhenCredentialSecretIsIncomplete()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.SftpProviderId);
        var lease = new TrackableCredentialLease(new CredentialRef("cred-1"));
        var creator = new SftpStorageProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new StaticCredentialStore(
                new CredentialMaterialLease(
                    lease,
                    new CredentialSecretMaterial(new Dictionary<string, string>
                    {
                        ["username"] = "deploy"
                    })))));

        var result = await creator.CreateAsync(CreateAccount());

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
        Assert.True(lease.WasDisposed);
    }

    [Fact]
    public async Task Creator_ReturnsValidationFailureAndReleasesLease_WhenPrivateKeyAuthHasNoPrivateKey()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.SftpProviderId);
        var lease = new TrackableCredentialLease(new CredentialRef("cred-1"));
        var creator = new SftpStorageProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new StaticCredentialStore(
                new CredentialMaterialLease(
                    lease,
                    new CredentialSecretMaterial(new Dictionary<string, string>
                    {
                        ["username"] = "deploy"
                    })))));

        var result = await creator.CreateAsync(CreateAccount(
            new Dictionary<string, string>
            {
                ["authMode"] = "privateKey"
            }));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
        Assert.True(lease.WasDisposed);
    }

    [Fact]
    public async Task Creator_ReturnsValidationFailure_WhenHostKeyFingerprintPolicyHasNoFingerprint()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.SftpProviderId);
        var creator = new SftpStorageProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new ThrowingCredentialStore()));

        var result = await creator.CreateAsync(CreateAccount(
            new Dictionary<string, string>
            {
                ["hostKeyPolicy"] = "fingerprint"
            }));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task Creator_ReturnsValidationFailure_WhenTimeoutIsInvalid()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.SftpProviderId);
        var creator = new SftpStorageProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new ThrowingCredentialStore()));

        var result = await creator.CreateAsync(CreateAccount(
            new Dictionary<string, string>
            {
                ["timeoutSeconds"] = "0"
            }));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    private static SftpStorageProvider CreateProvider(FakeSftpClient client, string? rootPath = null)
    {
        return new SftpStorageProvider(
            client,
            new CredentialMaterialLease(
                new TrackableCredentialLease(new CredentialRef("cred-1")),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["username"] = "deploy",
                    ["password"] = "secret"
                })),
            StorageCapabilitySet.Empty,
            rootPath);
    }

    private static StorageAccount CreateAccount(IReadOnlyDictionary<string, string>? providerConfig = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.FileTransfer,
            StorageProviderRegistry.SftpProviderId,
            "SFTP",
            "example.com",
            null,
            new CredentialRef("cred-1"),
            now,
            now,
            providerConfig);
    }

    private sealed class FakeSftpClient : ISftpClientAdapter
    {
        public bool IsConnected { get; private set; }

        public string? WorkingDirectory { get; init; } = "/home/test";

        public IReadOnlyList<SftpRemoteEntry> Entries { get; init; } = [];

        public IReadOnlyCollection<string> ExistingFiles { get; init; } = [];

        public IReadOnlyCollection<string> ExistingDirectories { get; init; } = [];

        public List<string> CreatedDirectories { get; } = [];

        public string? LastListPath { get; private set; }

        public string? DeletePath { get; private set; }

        public string? DeletedDirectoryPath { get; private set; }

        public (string SourcePath, string DestinationPath)? MovedPath { get; private set; }

        public string? UploadPath { get; private set; }

        public string? UploadedTemporaryPath { get; private set; }

        public byte[] UploadPayload { get; private set; } = [];

        public string? DownloadPath { get; private set; }

        public byte[] DownloadPayload { get; init; } = [];

        public bool BlockUploadUntilCanceled { get; init; }

        public TaskCompletionSource UploadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Connect()
        {
            IsConnected = true;
        }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Connect();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SftpRemoteEntry>> ListDirectoryAsync(
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastListPath = path;
            return Task.FromResult(Entries);
        }

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                ExistingFiles.Contains(path) ||
                string.Equals(UploadedTemporaryPath, path, StringComparison.Ordinal));
        }

        public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExistingDirectories.Contains(path) || CreatedDirectories.Contains(path));
        }

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreatedDirectories.Add(path);
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeletePath = path;
            return Task.CompletedTask;
        }

        public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeletedDirectoryPath = path;
            return Task.CompletedTask;
        }

        public Task MoveAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MovedPath = (sourcePath, destinationPath);
            if (string.Equals(sourcePath, UploadedTemporaryPath, StringComparison.Ordinal))
            {
                UploadPath = destinationPath;
            }

            return Task.CompletedTask;
        }

        public async Task UploadFileAsync(Stream content, string path, CancellationToken cancellationToken)
        {
            UploadedTemporaryPath = path;
            UploadStarted.TrySetResult();
            if (BlockUploadUntilCanceled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            UploadPayload = copy.ToArray();
        }

        public async Task DownloadFileAsync(
            string path,
            Stream destination,
            CancellationToken cancellationToken)
        {
            DownloadPath = path;
            await destination.WriteAsync(DownloadPayload, cancellationToken);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingCredentialStore : ICredentialStore
    {
        public Task<OperationResult<CredentialRef>> SaveAsync(
            CredentialSecretMaterial material,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("SFTP host key validation should fail before credential access.");
        }

        public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("SFTP host key validation should fail before credential access.");
        }

        public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("SFTP host key validation should fail before credential access.");
        }

        public Task<OperationResult<bool>> ExistsAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("SFTP host key validation should fail before credential access.");
        }

        public Task<OperationResult> MarkPendingDeleteAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("SFTP host key validation should fail before credential access.");
        }
    }

    private sealed class StaticCredentialStore : ICredentialStore
    {
        private readonly CredentialMaterialLease _materialLease;

        public StaticCredentialStore(CredentialMaterialLease materialLease)
        {
            _materialLease = materialLease;
        }

        public Task<OperationResult<CredentialRef>> SaveAsync(
            CredentialSecretMaterial material,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialRef>.Success(_materialLease.CredentialRef));
        }

        public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialLease>.Success(new TrackableCredentialLease(credentialRef)));
        }

        public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialMaterialLease>.Success(_materialLease));
        }

        public Task<OperationResult<bool>> ExistsAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<bool>.Success(true));
        }

        public Task<OperationResult> MarkPendingDeleteAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class TrackableCredentialLease : CredentialLease
    {
        public TrackableCredentialLease(CredentialRef credentialRef)
            : base(credentialRef, "sftp-provider-test")
        {
        }

        public bool WasDisposed { get; private set; }

        public override ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProgressRecorder : IProgress<TransferProgress>
    {
        public TransferProgress? Latest { get; private set; }

        public void Report(TransferProgress value)
        {
            Latest = value;
        }
    }
}
