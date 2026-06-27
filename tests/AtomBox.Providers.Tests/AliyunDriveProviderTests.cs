using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.NetDisk.AliyunDrive;

namespace AtomBox.Providers.Tests;

public sealed class AliyunDriveProviderTests
{
    [Fact]
    public async Task ListAsync_MapsFilesAndFolders_FromConfiguredRootFileId()
    {
        var updatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var client = new FakeAliyunDriveClient
        {
            Items =
            [
                new AliyunDriveFileItem("folder-id", "docs", true, null, updatedAt, null),
                new AliyunDriveFileItem("file-id", "readme.txt", false, 128, updatedAt, "text/plain")
            ]
        };
        await using var provider = CreateProvider(client, rootFileId: "root-file-id");

        var result = await provider.ListAsync(RemotePath.Root);

        Assert.True(result.IsSuccess);
        Assert.Equal("drive-1", client.LastListDriveId);
        Assert.Equal("root-file-id", client.LastListParentFileId);
        Assert.Collection(
            result.GetValueOrThrow(),
            folder =>
            {
                Assert.Equal("docs", folder.Name);
                Assert.Equal("folder-id", folder.Path.Value);
                Assert.Equal(RemoteItemKind.Folder, folder.Kind);
            },
            file =>
            {
                Assert.Equal("readme.txt", file.Name);
                Assert.Equal("file-id", file.Path.Value);
                Assert.Equal(RemoteItemKind.File, file.Kind);
                Assert.Equal(128, file.Size);
                Assert.Equal("text/plain", file.ContentType);
            });
    }

    [Fact]
    public async Task UploadDownloadAndDelete_UseFileIds()
    {
        var client = new FakeAliyunDriveClient { DownloadPayload = [5, 6, 7] };
        await using var provider = CreateProvider(client);
        await using var content = new MemoryStream([1, 2, 3, 4]);
        await using var destination = new MemoryStream();
        var progress = new ProgressRecorder();

        var upload = await provider.UploadAsync(new RemotePath("folder-id/file.txt"), content, content.Length, progress);
        var download = await provider.DownloadAsync(new RemotePath("file-id"), destination, progress);
        var delete = await provider.DeleteAsync(new RemotePath("file-id"));

        Assert.True(upload.IsSuccess);
        Assert.True(download.IsSuccess);
        Assert.True(delete.IsSuccess);
        Assert.Equal("drive-1", client.UploadDriveId);
        Assert.Equal("folder-id", client.UploadParentFileId);
        Assert.Equal("file.txt", client.UploadName);
        Assert.Equal([1, 2, 3, 4], client.UploadPayload);
        Assert.Equal("file-id", client.DownloadFileId);
        Assert.Equal([5, 6, 7], destination.ToArray());
        Assert.Equal("file-id", client.DeletedFileId);
        Assert.NotNull(progress.Latest);
    }

    [Fact]
    public async Task UploadAsync_ResolvesSingleFileNameUnderConfiguredRootFileId()
    {
        var client = new FakeAliyunDriveClient();
        await using var provider = CreateProvider(client, rootFileId: "root-file-id");
        await using var content = new MemoryStream([1]);

        var result = await provider.UploadAsync(new RemotePath("file.txt"), content, content.Length);

        Assert.True(result.IsSuccess);
        Assert.Equal("root-file-id", client.UploadParentFileId);
        Assert.Equal("file.txt", client.UploadName);
    }

    [Fact]
    public async Task Creator_ReturnsValidationFailureAndReleasesLease_WhenCredentialTokenIsMissing()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.AliyunDriveProviderId);
        var lease = new TrackableCredentialLease(new CredentialRef("cred-1"));
        var creator = new AliyunDriveProviderCreator(
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
    public void Registry_DeclaresAliyunDriveConfigFields()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.AliyunDriveProviderId);

        Assert.Contains(descriptor.ConfigFields, field => field.Key == "driveId" && field.IsRequired);
        Assert.Contains(descriptor.ConfigFields, field => field.Key == "rootFileId" && !field.IsRequired);
        Assert.Contains(descriptor.ConfigFields, field => field.Key == "endpoint" && !field.IsRequired);
    }

    private static AliyunDriveProvider CreateProvider(FakeAliyunDriveClient client, string rootFileId = "root")
    {
        return new AliyunDriveProvider(
            client,
            new CredentialMaterialLease(
                new TrackableCredentialLease(new CredentialRef("cred-1")),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["token"] = "token-value"
                })),
            StorageCapabilitySet.Empty,
            "drive-1",
            rootFileId);
    }

    private static StorageAccount CreateAccount()
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.NetDisk,
            StorageProviderRegistry.AliyunDriveProviderId,
            "Aliyun Drive",
            "https://openapi.alipan.com",
            null,
            new CredentialRef("cred-1"),
            now,
            now,
            new Dictionary<string, string>
            {
                ["driveId"] = "drive-1"
            });
    }

    private sealed class FakeAliyunDriveClient : IAliyunDriveClient
    {
        public IReadOnlyList<AliyunDriveFileItem> Items { get; init; } = [];

        public byte[] DownloadPayload { get; init; } = [];

        public string? LastListDriveId { get; private set; }

        public string? LastListParentFileId { get; private set; }

        public string? DeletedFileId { get; private set; }

        public string? UploadDriveId { get; private set; }

        public string? UploadParentFileId { get; private set; }

        public string? UploadName { get; private set; }

        public byte[] UploadPayload { get; private set; } = [];

        public string? DownloadFileId { get; private set; }

        public Task<IReadOnlyList<AliyunDriveFileItem>> ListAsync(
            string driveId,
            string parentFileId,
            CancellationToken cancellationToken = default)
        {
            LastListDriveId = driveId;
            LastListParentFileId = parentFileId;
            return Task.FromResult(Items);
        }

        public Task DeleteAsync(
            string driveId,
            string fileId,
            CancellationToken cancellationToken = default)
        {
            DeletedFileId = fileId;
            return Task.CompletedTask;
        }

        public Task UploadAsync(
            string driveId,
            string parentFileId,
            string name,
            Stream content,
            long? contentLength,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            UploadDriveId = driveId;
            UploadParentFileId = parentFileId;
            UploadName = name;
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            UploadPayload = copy.ToArray();
            progress?.Report(UploadPayload.LongLength);
            return Task.CompletedTask;
        }

        public Task<AliyunDriveDownloadObject> GetObjectAsync(
            string driveId,
            string fileId,
            CancellationToken cancellationToken = default)
        {
            DownloadFileId = fileId;
            return Task.FromResult(new AliyunDriveDownloadObject(
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
            : base(credentialRef, "aliyun-drive-provider-test")
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
