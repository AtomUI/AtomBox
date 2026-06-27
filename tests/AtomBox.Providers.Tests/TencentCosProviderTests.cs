using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.TencentCos;

namespace AtomBox.Providers.Tests;

public sealed class TencentCosProviderTests
{
    [Fact]
    public async Task ListAsync_MapsBuckets_WhenPathIsRoot()
    {
        var client = new FakeTencentCosClient
        {
            Buckets =
            [
                new TencentCosBucket("bucket-a-1250000000", "ap-guangzhou", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero))
            ]
        };
        await using var provider = CreateProvider(client);

        var result = await provider.ListAsync(RemotePath.Root);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.GetValueOrThrow());
        Assert.Equal("bucket-a-1250000000", item.Name);
        Assert.Equal(RemoteItemKind.Bucket, item.Kind);
        Assert.Equal(RemotePathKind.BucketRoot, item.Path.Kind);
        Assert.Equal("bucket-a-1250000000", item.Path.Value);
    }

    [Fact]
    public async Task ListAsync_MapsCommonPrefixesAndObjects_WhenPathIsBucketOrFolder()
    {
        var updatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var client = new FakeTencentCosClient
        {
            ObjectListing = new TencentCosObjectListing(
                [
                    new TencentCosObjectSummary("folder/file.txt", 128, updatedAt, "\"etag-1\""),
                    new TencentCosObjectSummary("folder/", 0, updatedAt, null)
                ],
                ["folder/child/"])
        };
        await using var provider = CreateProvider(client);

        var result = await provider.ListAsync(new RemotePath("bucket-a-1250000000/folder", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a-1250000000", client.LastListBucketName);
        Assert.Equal("folder/", client.LastListPrefix);

        var items = result.GetValueOrThrow();
        Assert.Collection(
            items,
            folder =>
            {
                Assert.Equal("child", folder.Name);
                Assert.Equal(RemoteItemKind.Folder, folder.Kind);
                Assert.Equal("bucket-a-1250000000/folder/child", folder.Path.Value);
            },
            file =>
            {
                Assert.Equal("file.txt", file.Name);
                Assert.Equal(RemoteItemKind.File, file.Kind);
                Assert.Equal(128, file.Size);
                Assert.Equal(updatedAt, file.UpdatedAt);
                Assert.Equal("\"etag-1\"", file.ETag);
                Assert.Equal("bucket-a-1250000000/folder/file.txt", file.Path.Value);
            });
    }

    [Fact]
    public async Task ListPageAsync_UsesSearchPrefixAsNativeCosPrefixWithoutCorruptingNames()
    {
        var client = new FakeTencentCosClient
        {
            ObjectListing = new TencentCosObjectListing(
                [new TencentCosObjectSummary("folder/report.txt", 64, null, null)],
                ["folder/releases/"])
        };
        await using var provider = CreateProvider(client);

        var result = await provider.ListPageAsync(
            new RemotePath("bucket-a/folder", RemotePathKind.Folder),
            new RemotePageRequest(50, searchPrefix: "re"));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.LastListBucketName);
        Assert.Equal("folder/re", client.LastListPrefix);
        Assert.Collection(
            result.GetValueOrThrow().Items,
            folder =>
            {
                Assert.Equal("releases", folder.Name);
                Assert.Equal(RemoteItemKind.Folder, folder.Kind);
                Assert.Equal("bucket-a/folder/releases", folder.Path.Value);
            },
            file =>
            {
                Assert.Equal("report.txt", file.Name);
                Assert.Equal(RemoteItemKind.File, file.Kind);
                Assert.Equal("bucket-a/folder/report.txt", file.Path.Value);
            });
    }

    [Fact]
    public async Task DeleteAsync_RejectsBucketRootDeletion()
    {
        await using var provider = CreateProvider(new FakeTencentCosClient());

        var result = await provider.DeleteAsync(new RemotePath("bucket-a-1250000000", RemotePathKind.BucketRoot));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task UploadDownloadAndDelete_UseObjectPath()
    {
        var client = new FakeTencentCosClient
        {
            DownloadPayload = [5, 6, 7]
        };
        await using var provider = CreateProvider(client);
        await using var content = new MemoryStream([1, 2, 3, 4]);
        await using var destination = new MemoryStream();
        var progress = new ProgressRecorder();

        var upload = await provider.UploadAsync(
            new RemotePath("bucket-a-1250000000/folder/file.txt", RemotePathKind.ObjectPath),
            content,
            content.Length,
            progress);
        var download = await provider.DownloadAsync(
            new RemotePath("bucket-a-1250000000/folder/file.txt", RemotePathKind.ObjectPath),
            destination,
            progress);
        var delete = await provider.DeleteAsync(
            new RemotePath("bucket-a-1250000000/folder/file.txt", RemotePathKind.ObjectPath));

        Assert.True(upload.IsSuccess);
        Assert.True(download.IsSuccess);
        Assert.True(delete.IsSuccess);
        Assert.Equal("bucket-a-1250000000", client.PutBucketName);
        Assert.Equal("folder/file.txt", client.PutKey);
        Assert.Equal([1, 2, 3, 4], client.PutPayload);
        Assert.Equal("bucket-a-1250000000", client.GetBucketName);
        Assert.Equal("folder/file.txt", client.GetKey);
        Assert.Equal([5, 6, 7], destination.ToArray());
        Assert.Equal("bucket-a-1250000000", client.DeletedBucketName);
        Assert.Equal("folder/file.txt", client.DeletedKey);
        Assert.NotNull(progress.Latest);
    }

    [Fact]
    public async Task Creator_ReturnsValidationFailureAndReleasesLease_WhenCredentialSecretIsIncomplete()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.TencentCosProviderId);
        var lease = new TrackableCredentialLease(new CredentialRef("cred-1"));
        var creator = new TencentCosProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new StaticCredentialStore(
                new CredentialMaterialLease(
                    lease,
                    new CredentialSecretMaterial(new Dictionary<string, string>
                    {
                        ["accessKeyId"] = "secret-id"
                    })))));

        var result = await creator.CreateAsync(CreateAccount());

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
        Assert.True(lease.WasDisposed);
    }

    private static TencentCosProvider CreateProvider(FakeTencentCosClient client)
    {
        return new TencentCosProvider(
            client,
            new CredentialMaterialLease(
                new TrackableCredentialLease(new CredentialRef("cred-1")),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["accessKeyId"] = "secret-id",
                    ["accessKeySecret"] = "secret-key"
                })),
            StorageCapabilitySet.Empty);
    }

    private static StorageAccount CreateAccount()
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderRegistry.TencentCosProviderId,
            "Tencent COS",
            "cos.ap-guangzhou.myqcloud.com",
            "ap-guangzhou",
            new CredentialRef("cred-1"),
            now,
            now);
    }

    private sealed class FakeTencentCosClient : ITencentCosClient
    {
        public IReadOnlyList<TencentCosBucket> Buckets { get; init; } = [];

        public TencentCosObjectListing ObjectListing { get; init; } = new([], []);

        public string? LastListBucketName { get; private set; }

        public string? LastListPrefix { get; private set; }

        public string? DeletedBucketName { get; private set; }

        public string? DeletedKey { get; private set; }

        public string? PutBucketName { get; private set; }

        public string? PutKey { get; private set; }

        public byte[] PutPayload { get; private set; } = [];

        public string? GetBucketName { get; private set; }

        public string? GetKey { get; private set; }

        public byte[] DownloadPayload { get; init; } = [];

        public IReadOnlyList<TencentCosBucket> ListBuckets()
        {
            return Buckets;
        }

        public void CreateBucket(string bucketName)
        {
            PutBucketName = bucketName;
        }

        public TencentCosObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
        {
            LastListBucketName = bucketName;
            LastListPrefix = prefix;
            return ObjectListing;
        }

        public void DeleteObject(string bucketName, string key)
        {
            DeletedBucketName = bucketName;
            DeletedKey = key;
        }

        public void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey)
        {
        }

        public void PutObject(string bucketName, string key, Stream content, Action<long, long>? progress = null)
        {
            PutBucketName = bucketName;
            PutKey = key;
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            PutPayload = copy.ToArray();
            progress?.Invoke(PutPayload.LongLength, PutPayload.LongLength);
        }

        public void PutObjectMultipart(string bucketName, string key, Stream content, long contentLength, Action<long, long>? progress = null)
        {
            PutObject(bucketName, key, content, progress);
            progress?.Invoke(contentLength, contentLength);
        }

        public TencentCosDownloadObject GetObject(string bucketName, string key)
        {
            GetBucketName = bucketName;
            GetKey = key;
            return new TencentCosDownloadObject(new MemoryStream(DownloadPayload), DownloadPayload.LongLength);
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
            : base(credentialRef, "tencent-cos-provider-test")
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
