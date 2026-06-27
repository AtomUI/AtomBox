using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.NetDisk.BaiduNetDisk;

namespace AtomBox.Providers.Tests;

public sealed class BaiduNetDiskProviderTests
{
    [Fact]
    public async Task ListAsync_MapsFilesAndFolders_FromConfiguredRootPath()
    {
        var updatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var client = new FakeBaiduNetDiskClient
        {
            Items =
            [
                new BaiduNetDiskFileItem("/apps/atombox/docs", "docs", true, null, updatedAt),
                new BaiduNetDiskFileItem("/apps/atombox/readme.txt", "readme.txt", false, 128, updatedAt)
            ]
        };
        await using var provider = CreateProvider(client, rootPath: "/apps/atombox");

        var result = await provider.ListAsync(RemotePath.Root);

        Assert.True(result.IsSuccess);
        Assert.Equal("/apps/atombox", client.LastListPath);
        Assert.Collection(
            result.GetValueOrThrow(),
            folder =>
            {
                Assert.Equal("docs", folder.Name);
                Assert.Equal("apps/atombox/docs", folder.Path.Value);
                Assert.Equal(RemoteItemKind.Folder, folder.Kind);
            },
            file =>
            {
                Assert.Equal("readme.txt", file.Name);
                Assert.Equal("apps/atombox/readme.txt", file.Path.Value);
                Assert.Equal(RemoteItemKind.File, file.Kind);
                Assert.Equal(128, file.Size);
            });
    }

    [Fact]
    public async Task UploadDownloadAndDelete_UseRemotePaths()
    {
        var client = new FakeBaiduNetDiskClient { DownloadPayload = [5, 6, 7] };
        await using var provider = CreateProvider(client);
        await using var content = new MemoryStream([1, 2, 3, 4]);
        await using var destination = new MemoryStream();
        var progress = new ProgressRecorder();

        var upload = await provider.UploadAsync(new RemotePath("folder/file.txt"), content, content.Length, progress);
        var download = await provider.DownloadAsync(new RemotePath("folder/file.txt"), destination, progress);
        var delete = await provider.DeleteAsync(new RemotePath("folder/file.txt"));

        Assert.True(upload.IsSuccess);
        Assert.True(download.IsSuccess);
        Assert.True(delete.IsSuccess);
        Assert.Equal("/folder/file.txt", client.UploadPath);
        Assert.Equal([1, 2, 3, 4], client.UploadPayload);
        Assert.Equal("/folder/file.txt", client.DownloadPath);
        Assert.Equal([5, 6, 7], destination.ToArray());
        Assert.Equal("/folder/file.txt", client.DeletedPath);
        Assert.NotNull(progress.Latest);
    }

    [Fact]
    public async Task UploadAsync_ResolvesRelativePathUnderConfiguredRootPath()
    {
        var client = new FakeBaiduNetDiskClient();
        await using var provider = CreateProvider(client, rootPath: "/apps/atombox");
        await using var content = new MemoryStream([1]);

        var result = await provider.UploadAsync(new RemotePath("file.txt"), content, content.Length);

        Assert.True(result.IsSuccess);
        Assert.Equal("/apps/atombox/file.txt", client.UploadPath);
    }

    [Fact]
    public async Task Creator_ReturnsValidationFailureAndReleasesLease_WhenCredentialTokenIsMissing()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.BaiduNetDiskProviderId);
        var lease = new TrackableCredentialLease(new CredentialRef("cred-1"));
        var creator = new BaiduNetDiskProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new StaticCredentialStore(
                new CredentialMaterialLease(
                    lease,
                    new CredentialSecretMaterial(new Dictionary<string, string>
                    {
                        ["refreshToken"] = "not-used-yet"
                    })))));

        var result = await creator.CreateAsync(CreateAccount());

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
        Assert.True(lease.WasDisposed);
    }

    [Fact]
    public void Registry_DeclaresBaiduNetDiskConfigFields()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.BaiduNetDiskProviderId);

        Assert.Contains(descriptor.ConfigFields, field => field.Key == "endpoint" && !field.IsRequired);
        Assert.Contains(descriptor.ConfigFields, field => field.Key == "contentEndpoint" && !field.IsRequired);
        Assert.Contains(descriptor.ConfigFields, field => field.Key == "rootPath" && !field.IsRequired);
    }

    private static BaiduNetDiskProvider CreateProvider(FakeBaiduNetDiskClient client, string rootPath = "/")
    {
        return new BaiduNetDiskProvider(
            client,
            new CredentialMaterialLease(
                new TrackableCredentialLease(new CredentialRef("cred-1")),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["token"] = "token-value"
                })),
            StorageCapabilitySet.Empty,
            rootPath);
    }

    private static StorageAccount CreateAccount()
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.NetDisk,
            StorageProviderRegistry.BaiduNetDiskProviderId,
            "Baidu Netdisk",
            "https://pan.baidu.com",
            null,
            new CredentialRef("cred-1"),
            now,
            now,
            new Dictionary<string, string>
            {
                ["rootPath"] = "/apps/atombox"
            });
    }

    private sealed class FakeBaiduNetDiskClient : IBaiduNetDiskClient
    {
        public IReadOnlyList<BaiduNetDiskFileItem> Items { get; init; } = [];

        public byte[] DownloadPayload { get; init; } = [];

        public string? LastListPath { get; private set; }

        public string? DeletedPath { get; private set; }

        public string? UploadPath { get; private set; }

        public byte[] UploadPayload { get; private set; } = [];

        public string? DownloadPath { get; private set; }

        public Task<IReadOnlyList<BaiduNetDiskFileItem>> ListAsync(
            string directoryPath,
            CancellationToken cancellationToken = default)
        {
            LastListPath = directoryPath;
            return Task.FromResult(Items);
        }

        public Task DeleteAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            DeletedPath = path;
            return Task.CompletedTask;
        }

        public Task UploadAsync(
            string path,
            Stream content,
            long? contentLength,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            UploadPath = path;
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            UploadPayload = copy.ToArray();
            progress?.Report(UploadPayload.LongLength);
            return Task.CompletedTask;
        }

        public Task<BaiduNetDiskDownloadObject> GetObjectAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            DownloadPath = path;
            return Task.FromResult(new BaiduNetDiskDownloadObject(
                new MemoryStream(DownloadPayload),
                DownloadPayload.LongLength));
        }

        public void Dispose()
        {
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
            return Task.FromResult(OperationResult<CredentialLease>.Success(
                new TrackableCredentialLease(credentialRef)));
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
            : base(credentialRef, "baidu-netdisk-provider-test")
        {
        }

        public bool WasDisposed { get; private set; }

        public override ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
