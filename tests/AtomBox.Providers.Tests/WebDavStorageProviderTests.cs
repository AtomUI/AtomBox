using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.FileTransfer.WebDav;

namespace AtomBox.Providers.Tests;

public sealed class WebDavStorageProviderTests
{
    [Fact]
    public async Task ListAsync_MapsFoldersAndFiles()
    {
        var client = new FakeWebDavClient
        {
            Entries =
            [
                new WebDavRemoteEntry("docs", true, null, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
                new WebDavRemoteEntry("readme.txt", false, 128, new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero))
            ]
        };
        await using var provider = CreateProvider(client, rootPath: "/remote");

        var result = await provider.ListAsync(RemotePath.Root);

        Assert.True(result.IsSuccess);
        Assert.Equal("/remote", client.LastListPath);
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
    public async Task UploadDownloadAndDelete_UseMappedRootPath()
    {
        var client = new FakeWebDavClient
        {
            DownloadPayload = [5, 6, 7],
            ExistingFiles = ["/remote/folder/file.txt"],
            ExistingDirectories = ["/remote", "/remote/folder"]
        };
        await using var provider = CreateProvider(client, rootPath: "/remote");
        await using var content = new MemoryStream([1, 2, 3]);
        await using var destination = new MemoryStream();
        var progress = new ProgressRecorder();

        var upload = await provider.UploadAsync(new RemotePath("folder/file.txt"), content, content.Length, progress);
        var download = await provider.DownloadAsync(new RemotePath("folder/file.txt"), destination, progress);
        var delete = await provider.DeleteAsync(new RemotePath("folder/file.txt"));

        Assert.True(upload.IsSuccess);
        Assert.True(download.IsSuccess);
        Assert.True(delete.IsSuccess);
        Assert.Equal("/remote/folder/file.txt", client.UploadPath);
        Assert.Equal("/remote/folder/file.txt", client.DownloadPath);
        Assert.Equal("/remote/folder/file.txt", client.DeletePath);
        Assert.Equal([1, 2, 3], client.UploadPayload);
        Assert.Equal([5, 6, 7], destination.ToArray());
        Assert.NotNull(progress.Latest);
    }

    [Fact]
    public async Task CreateFolderAsync_CreatesMissingFolderAndParents()
    {
        var client = new FakeWebDavClient
        {
            ExistingDirectories = ["/remote"]
        };
        await using var provider = CreateProvider(client, rootPath: "/remote");

        var result = await provider.CreateFolderAsync(new RemotePath("a/b", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal(["/remote/a", "/remote/a/b"], client.CreatedDirectories);
    }

    [Fact]
    public async Task RenameAsync_MovesFileWithinSameParent()
    {
        var client = new FakeWebDavClient
        {
            ExistingDirectories = ["/remote", "/remote/folder"],
            ExistingFiles = ["/remote/folder/old.txt"]
        };
        await using var provider = CreateProvider(client, rootPath: "/remote");

        var result = await provider.RenameAsync(new RemotePath("folder/old.txt"), "new.txt");

        Assert.True(result.IsSuccess);
        Assert.Equal(("/remote/folder/old.txt", "/remote/folder/new.txt"), client.MovedPath);
    }

    [Fact]
    public async Task MoveAsync_MovesFolderAndCreatesDestinationParent()
    {
        var client = new FakeWebDavClient
        {
            ExistingDirectories = ["/remote", "/remote/source"]
        };
        await using var provider = CreateProvider(client, rootPath: "/remote");

        var result = await provider.MoveAsync(
            new RemotePath("source", RemotePathKind.Folder),
            new RemotePath("target/source", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal(["/remote/target"], client.CreatedDirectories);
        Assert.Equal(("/remote/source", "/remote/target/source"), client.MovedPath);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsValidation_WhenPathIsFolder()
    {
        var client = new FakeWebDavClient
        {
            ExistingDirectories = ["/remote/folder"]
        };
        await using var provider = CreateProvider(client, rootPath: "/remote");
        await using var destination = new MemoryStream();

        var result = await provider.DownloadAsync(new RemotePath("folder", RemotePathKind.Folder), destination);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
        Assert.Null(client.DownloadPath);
    }

    [Fact]
    public async Task Creator_CreatesAnonymousProvider_WithoutReadingCredentialStore()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.WebDavProviderId);
        var creator = new WebDavStorageProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new ThrowingCredentialStore()));

        var result = await creator.CreateAsync(CreateAccount(
            new Dictionary<string, string>
            {
                ["authMode"] = "anonymous"
            }));

        Assert.True(result.IsSuccess);
        await result.GetValueOrThrow().DisposeAsync();
    }

    [Fact]
    public async Task Creator_ReturnsValidationFailureAndReleasesLease_WhenCredentialSecretIsIncomplete()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.WebDavProviderId);
        var lease = new TrackableCredentialLease(new CredentialRef("cred-1"));
        var creator = new WebDavStorageProviderCreator(
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

    [Theory]
    [InlineData("ftp://example.com/dav")]
    [InlineData("not-a-url")]
    public async Task Creator_ReturnsValidationFailure_WhenEndpointIsInvalid(string endpoint)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.WebDavProviderId);
        var creator = new WebDavStorageProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new ThrowingCredentialStore()));

        var result = await creator.CreateAsync(CreateAccount(endpoint: endpoint));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task Creator_ReturnsValidationFailure_WhenTimeoutIsInvalid()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.WebDavProviderId);
        var creator = new WebDavStorageProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new ThrowingCredentialStore()));

        var result = await creator.CreateAsync(CreateAccount(
            new Dictionary<string, string>
            {
                ["authMode"] = "anonymous",
                ["timeoutSeconds"] = "0"
            }));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public void NormalizeComparablePath_DecodesPercentEncodedUtf8Segments()
    {
        var encoded = WebDavHttpClientAdapter.NormalizeComparablePath("/dav/%e6%88%91%e7%9a%84%e5%9d%9a%e6%9e%9c%e4%ba%91/folder");
        var decoded = WebDavHttpClientAdapter.NormalizeComparablePath("/dav/我的坚果云/folder/");

        Assert.Equal(decoded, encoded);
    }

    [Fact]
    public async Task ListPageAsync_FiltersBySearchPrefixBeforePaging()
    {
        var client = new FakeWebDavClient
        {
            Entries =
            [
                new WebDavRemoteEntry("alpha-1.txt", false, 1, null),
                new WebDavRemoteEntry("beta-1.txt", false, 1, null),
                new WebDavRemoteEntry("alpha-2.txt", false, 1, null),
                new WebDavRemoteEntry("alpha-3.txt", false, 1, null),
                new WebDavRemoteEntry("gamma-1.txt", false, 1, null)
            ]
        };
        await using var provider = CreateProvider(client, rootPath: "/remote");

        var firstPage = await provider.ListPageAsync(
            new RemotePath("docs", RemotePathKind.Folder),
            new RemotePageRequest(2, searchPrefix: "alpha"));

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(["alpha-1.txt", "alpha-2.txt"], firstPage.GetValueOrThrow().Items.Select(item => item.Name));
        Assert.NotNull(firstPage.GetValueOrThrow().NextCursor);

        var secondPage = await provider.ListPageAsync(
            new RemotePath("docs", RemotePathKind.Folder),
            new RemotePageRequest(2, firstPage.GetValueOrThrow().NextCursor, "alpha"));

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(["alpha-3.txt"], secondPage.GetValueOrThrow().Items.Select(item => item.Name));
        Assert.Null(secondPage.GetValueOrThrow().NextCursor);
    }
    private static WebDavStorageProvider CreateProvider(FakeWebDavClient client, string? rootPath = null)
    {
        return new WebDavStorageProvider(
            client,
            new CredentialMaterialLease(
                new TrackableCredentialLease(new CredentialRef("cred-1")),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["username"] = "deploy",
                    ["password"] = "secret"
                })),
            new StorageCapabilitySet(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download | StorageCapability.Delete | StorageCapability.CreateFolder | StorageCapability.Rename | StorageCapability.Move),
            rootPath);
    }

    private static StorageAccount CreateAccount(
        IReadOnlyDictionary<string, string>? providerConfig = null,
        string? endpoint = "https://example.com/dav/")
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.FileTransfer,
            StorageProviderRegistry.WebDavProviderId,
            "WebDAV",
            endpoint,
            null,
            new CredentialRef("cred-1"),
            now,
            now,
            providerConfig);
    }

    private sealed class FakeWebDavClient : IWebDavClientAdapter
    {
        public IReadOnlyList<WebDavRemoteEntry> Entries { get; init; } = [];

        public IReadOnlyCollection<string> ExistingFiles { get; init; } = [];

        public IReadOnlyCollection<string> ExistingDirectories { get; init; } = [];

        public List<string> CreatedDirectories { get; } = [];

        public string? LastListPath { get; private set; }

        public string? DeletePath { get; private set; }

        public (string SourcePath, string DestinationPath)? MovedPath { get; private set; }

        public string? UploadPath { get; private set; }

        public byte[] UploadPayload { get; private set; } = [];

        public string? DownloadPath { get; private set; }

        public byte[] DownloadPayload { get; init; } = [];

        public IReadOnlyList<WebDavRemoteEntry> ListDirectory(string path)
        {
            LastListPath = path;
            return Entries;
        }

        public bool FileExists(string path)
        {
            return ExistingFiles.Contains(path);
        }

        public bool DirectoryExists(string path)
        {
            return ExistingDirectories.Contains(path) || CreatedDirectories.Contains(path);
        }

        public void CreateDirectory(string path)
        {
            CreatedDirectories.Add(path);
        }

        public void Delete(string path)
        {
            DeletePath = path;
        }

        public void Move(string sourcePath, string destinationPath)
        {
            MovedPath = (sourcePath, destinationPath);
        }

        public void UploadFile(Stream content, string path, Action<long>? progress = null)
        {
            UploadPath = path;
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            UploadPayload = copy.ToArray();
            progress?.Invoke(UploadPayload.Length);
        }

        public void DownloadFile(string path, Stream destination, Action<long>? progress = null)
        {
            DownloadPath = path;
            destination.Write(DownloadPayload);
            progress?.Invoke(DownloadPayload.Length);
        }

        public void Dispose()
        {
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

    private sealed class ThrowingCredentialStore : ICredentialStore
    {
        public Task<OperationResult<CredentialRef>> SaveAsync(
            CredentialSecretMaterial material,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("WebDAV validation should fail before credential access.");
        }

        public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("WebDAV validation should fail before credential access.");
        }

        public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("WebDAV validation should fail before credential access.");
        }

        public Task<OperationResult<bool>> ExistsAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("WebDAV validation should fail before credential access.");
        }

        public Task<OperationResult> MarkPendingDeleteAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("WebDAV validation should fail before credential access.");
        }
    }

    private sealed class TrackableCredentialLease : CredentialLease
    {
        public TrackableCredentialLease(CredentialRef credentialRef)
            : base(credentialRef, "webdav-provider-test")
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
