using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage;
using AtomBox.Providers.ObjectStorage.AliyunOss;

namespace AtomBox.Providers.Tests;

public sealed class AliyunOssProviderTests
{
    [Fact]
    public async Task ListAsync_MapsBuckets_WhenPathIsRoot()
    {
        var client = new FakeAliyunOssClient
        {
            Buckets =
            [
                new AliyunOssBucket("bucket-a", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero))
            ]
        };
        await using var provider = CreateProvider(client);

        var result = await provider.ListAsync(RemotePath.Root);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.GetValueOrThrow());
        Assert.Equal("bucket-a", item.Name);
        Assert.Equal(RemoteItemKind.Bucket, item.Kind);
        Assert.Equal(RemotePathKind.BucketRoot, item.Path.Kind);
        Assert.Equal("bucket-a", item.Path.Value);
    }

    [Fact]
    public async Task ListAsync_MapsCommonPrefixesAndObjects_WhenPathIsBucketOrFolder()
    {
        var updatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var client = new FakeAliyunOssClient
        {
            ObjectListing = new AliyunOssObjectListing(
                [
                    new AliyunOssObjectSummary("folder/file.txt", 128, updatedAt, "etag-1", "text/plain"),
                    new AliyunOssObjectSummary("folder/", 0, updatedAt, null, null)
                ],
                ["folder/child/"])
        };
        await using var provider = CreateProvider(client);

        var result = await provider.ListAsync(new RemotePath("bucket-a/folder", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.LastListBucketName);
        Assert.Equal("folder/", client.LastListPrefix);

        var items = result.GetValueOrThrow();
        Assert.Collection(
            items,
            folder =>
            {
                Assert.Equal("child", folder.Name);
                Assert.Equal(RemoteItemKind.Folder, folder.Kind);
                Assert.Equal("bucket-a/folder/child", folder.Path.Value);
            },
            file =>
            {
                Assert.Equal("file.txt", file.Name);
                Assert.Equal(RemoteItemKind.File, file.Kind);
                Assert.Equal(128, file.Size);
                Assert.Equal(updatedAt, file.UpdatedAt);
                Assert.Equal("etag-1", file.ETag);
                Assert.Equal("text/plain", file.ContentType);
                Assert.Equal("bucket-a/folder/file.txt", file.Path.Value);
            });
    }

    [Fact]
    public async Task ListAsync_MapsBucketRootCommonPrefixesAsFolders()
    {
        var client = new FakeAliyunOssClient
        {
            ObjectListing = new AliyunOssObjectListing(
                [new AliyunOssObjectSummary("readme.txt", 32, null, null, "text/plain")],
                ["documents/", "photos/"])
        };
        await using var provider = CreateProvider(client);

        var result = await provider.ListAsync(new RemotePath("bucket-a", RemotePathKind.BucketRoot));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.LastListBucketName);
        Assert.Equal(string.Empty, client.LastListPrefix);
        Assert.Collection(
            result.GetValueOrThrow(),
            documents =>
            {
                Assert.Equal("documents", documents.Name);
                Assert.Equal(RemoteItemKind.Folder, documents.Kind);
                Assert.Equal("bucket-a/documents", documents.Path.Value);
            },
            photos =>
            {
                Assert.Equal("photos", photos.Name);
                Assert.Equal(RemoteItemKind.Folder, photos.Kind);
                Assert.Equal("bucket-a/photos", photos.Path.Value);
            },
            file =>
            {
                Assert.Equal("readme.txt", file.Name);
                Assert.Equal(RemoteItemKind.File, file.Kind);
                Assert.Equal("bucket-a/readme.txt", file.Path.Value);
            });
    }

    [Fact]
    public async Task ListAsync_UsesFolderPathAsOssPrefixSearch()
    {
        var client = new FakeAliyunOssClient
        {
            ObjectListing = new AliyunOssObjectListing(
                [new AliyunOssObjectSummary("projects/alpha/readme.txt", 64, null, null, "text/plain")],
                ["projects/alpha/assets/"])
        };
        await using var provider = CreateProvider(client);

        var result = await provider.ListAsync(new RemotePath("bucket-a/projects/alpha", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.LastListBucketName);
        Assert.Equal("projects/alpha/", client.LastListPrefix);
        Assert.Collection(
            result.GetValueOrThrow(),
            folder =>
            {
                Assert.Equal("assets", folder.Name);
                Assert.Equal(RemoteItemKind.Folder, folder.Kind);
                Assert.Equal("bucket-a/projects/alpha/assets", folder.Path.Value);
            },
            file =>
            {
                Assert.Equal("readme.txt", file.Name);
                Assert.Equal(RemoteItemKind.File, file.Kind);
                Assert.Equal("bucket-a/projects/alpha/readme.txt", file.Path.Value);
            });
    }

    [Fact]
    public async Task ListPageAsync_UsesSearchPrefixAsNativeOssPrefixWithoutCorruptingNames()
    {
        var client = new FakeAliyunOssClient
        {
            ObjectListing = new AliyunOssObjectListing(
                [new AliyunOssObjectSummary("projects/alpha/report.txt", 64, null, null, "text/plain")],
                ["projects/alpha/releases/"])
        };
        await using var provider = CreateProvider(client);

        var result = await provider.ListPageAsync(
            new RemotePath("bucket-a/projects/alpha", RemotePathKind.Folder),
            new RemotePageRequest(50, searchPrefix: "re"));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.LastListBucketName);
        Assert.Equal("projects/alpha/re", client.LastListPrefix);
        Assert.Collection(
            result.GetValueOrThrow().Items,
            folder =>
            {
                Assert.Equal("releases", folder.Name);
                Assert.Equal(RemoteItemKind.Folder, folder.Kind);
                Assert.Equal("bucket-a/projects/alpha/releases", folder.Path.Value);
            },
            file =>
            {
                Assert.Equal("report.txt", file.Name);
                Assert.Equal(RemoteItemKind.File, file.Kind);
                Assert.Equal("bucket-a/projects/alpha/report.txt", file.Path.Value);
            });
    }

    [Fact]
    public async Task DeleteAsync_RejectsBucketRootDeletion()
    {
        await using var provider = CreateProvider(new FakeAliyunOssClient());

        var result = await provider.DeleteAsync(new RemotePath("bucket-a", RemotePathKind.BucketRoot));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task DeleteAsync_DeletesObjectPath()
    {
        var client = new FakeAliyunOssClient();
        await using var provider = CreateProvider(client);

        var result = await provider.DeleteAsync(new RemotePath("bucket-a/folder/file.txt", RemotePathKind.ObjectPath));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.DeletedBucketName);
        Assert.Equal("folder/file.txt", client.DeletedKey);
    }

    [Fact]
    public async Task DeleteAsync_FolderDeletesFolderMarkerKey()
    {
        var client = new FakeAliyunOssClient();
        await using var provider = CreateProvider(client);

        var result = await provider.DeleteAsync(new RemotePath("bucket-a/folder/child", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.DeletedBucketName);
        Assert.Equal("folder/child/", client.DeletedKey);
    }

    [Fact]
    public async Task UploadAsync_UploadsObjectStream()
    {
        var client = new FakeAliyunOssClient();
        await using var provider = CreateProvider(client);
        await using var content = new MemoryStream([1, 2, 3, 4]);
        var progress = new ProgressRecorder();

        var result = await provider.UploadAsync(
            new RemotePath("bucket-a/folder/file.txt", RemotePathKind.ObjectPath),
            content,
            content.Length,
            progress);

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.PutBucketName);
        Assert.Equal("folder/file.txt", client.PutKey);
        Assert.Equal([1, 2, 3, 4], client.PutPayload);
        Assert.Equal(100, progress.Latest?.Percent);
    }

    [Fact]
    public async Task TransferCapabilities_ReportCooperativeCancellation()
    {
        await using var provider = CreateProvider(new FakeAliyunOssClient());

        Assert.True(provider.TransferCapabilities.SupportsCooperativeCancellation);
        Assert.False(provider.TransferCapabilities.SupportsActiveAbort);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadAsync_ObservesCancellationDuringTransfer(bool useMultipart)
    {
        var client = new FakeAliyunOssClient
        {
            BlockUploadUntilCanceled = true
        };
        var capabilities = useMultipart
            ? new StorageCapabilitySet(StorageCapability.MultipartUpload)
            : StorageCapabilitySet.Empty;
        await using var provider = CreateProvider(client, capabilities);
        await using var content = new MemoryStream([1, 2, 3, 4]);
        using var cancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var upload = Task.Run(() => provider.UploadAsync(
            new RemotePath("bucket-a/folder/file.txt", RemotePathKind.ObjectPath),
            content,
            useMultipart ? ObjectStorageUploadPolicy.MultipartThreshold : content.Length,
            cancellationToken: cancellation.Token));
        await client.UploadStarted.Task.WaitAsync(timeout.Token);

        cancellation.Cancel();
        var result = await upload.WaitAsync(timeout.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCode.OperationCanceled, result.Error?.Code);
        Assert.Equal(StorageErrorCategory.Canceled, result.Error?.Category);
        Assert.Equal(useMultipart, client.MultipartUploadUsed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadAsync_DoesNotReportSuccess_WhenCancellationRacesWithSdkCompletion(
        bool useMultipart)
    {
        using var cancellation = new CancellationTokenSource();
        var client = new FakeAliyunOssClient
        {
            BeforeUploadReturns = cancellation.Cancel
        };
        var capabilities = useMultipart
            ? new StorageCapabilitySet(StorageCapability.MultipartUpload)
            : StorageCapabilitySet.Empty;
        await using var provider = CreateProvider(client, capabilities);
        await using var content = new MemoryStream([1, 2, 3, 4]);

        var result = await provider.UploadAsync(
            new RemotePath("bucket-a/folder/file.txt", RemotePathKind.ObjectPath),
            content,
            useMultipart ? ObjectStorageUploadPolicy.MultipartThreshold : content.Length,
            cancellationToken: cancellation.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCode.OperationCanceled, result.Error?.Code);
        Assert.Equal(StorageErrorCategory.Canceled, result.Error?.Category);
        Assert.Equal(useMultipart, client.MultipartUploadUsed);
    }

    [Fact]
    public async Task UploadAsync_ReturnsConflict_WhenObjectAlreadyExists()
    {
        var client = new FakeAliyunOssClient
        {
            ObjectListing = new AliyunOssObjectListing(
                [new AliyunOssObjectSummary("folder/file.txt", 128, null, null, null)],
                [])
        };
        await using var provider = CreateProvider(client);
        await using var content = new MemoryStream([1, 2, 3, 4]);

        var result = await provider.UploadAsync(
            new RemotePath("bucket-a/folder/file.txt", RemotePathKind.ObjectPath),
            content,
            content.Length);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
        Assert.Null(client.PutBucketName);
        Assert.Null(client.PutKey);
    }

    [Fact]
    public async Task DownloadAsync_CopiesObjectStreamToDestination()
    {
        var client = new FakeAliyunOssClient
        {
            DownloadPayload = [5, 6, 7]
        };
        await using var provider = CreateProvider(client);
        await using var destination = new MemoryStream();
        var progress = new ProgressRecorder();

        var result = await provider.DownloadAsync(
            new RemotePath("bucket-a/folder/file.txt", RemotePathKind.ObjectPath),
            destination,
            progress);

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.GetBucketName);
        Assert.Equal("folder/file.txt", client.GetKey);
        Assert.Equal([5, 6, 7], destination.ToArray());
        Assert.Equal(100, progress.Latest?.Percent);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesCredentialLease()
    {
        var lease = new TrackableCredentialLease(new CredentialRef("cred-1"));
        var materialLease = new CredentialMaterialLease(
            lease,
            new CredentialSecretMaterial(new Dictionary<string, string>
            {
                ["accessKeyId"] = "ak-test",
                ["accessKeySecret"] = "secret-test"
            }));
        var provider = new AliyunOssProvider(
            new FakeAliyunOssClient(),
            materialLease,
            StorageCapabilitySet.Empty);

        await provider.DisposeAsync();

        Assert.True(lease.WasDisposed);
    }

    [Fact]
    public async Task Creator_ReturnsValidationFailureAndReleasesLease_WhenCredentialSecretIsIncomplete()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.AliyunOssProviderId);
        var lease = new TrackableCredentialLease(new CredentialRef("cred-1"));
        var credentialStore = new StaticCredentialStore(
            new CredentialMaterialLease(
                lease,
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["accessKeyId"] = "ak-test"
                })));
        var creator = new AliyunOssProviderCreator(
            descriptor,
            new ProviderCredentialResolver(credentialStore));

        var result = await creator.CreateAsync(CreateAccount());

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
        Assert.True(lease.WasDisposed);
    }

    private static AliyunOssProvider CreateProvider(
        FakeAliyunOssClient client,
        StorageCapabilitySet? capabilities = null)
    {
        return new AliyunOssProvider(
            client,
            new CredentialMaterialLease(
                new TrackableCredentialLease(new CredentialRef("cred-1")),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["accessKeyId"] = "ak-test",
                    ["accessKeySecret"] = "secret-test"
                })),
            capabilities ?? StorageCapabilitySet.Empty);
    }

    private static StorageAccount CreateAccount()
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderRegistry.AliyunOssProviderId,
            "Aliyun OSS",
            "oss-cn-hangzhou.aliyuncs.com",
            null,
            new CredentialRef("cred-1"),
            now,
            now);
    }

    private sealed class FakeAliyunOssClient : IAliyunOssClient
    {
        public IReadOnlyList<AliyunOssBucket> Buckets { get; init; } = [];

        public AliyunOssObjectListing ObjectListing { get; init; } = new([], []);

        public string? LastListBucketName { get; private set; }

        public string? LastListPrefix { get; private set; }

        public string? DeletedBucketName { get; private set; }

        public string? DeletedKey { get; private set; }

        public string? PutBucketName { get; private set; }

        public string? PutKey { get; private set; }

        public byte[] PutPayload { get; private set; } = [];

        public bool BlockUploadUntilCanceled { get; init; }

        public Action? BeforeUploadReturns { get; init; }

        public bool MultipartUploadUsed { get; private set; }

        public TaskCompletionSource<bool> UploadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string? GetBucketName { get; private set; }

        public string? GetKey { get; private set; }

        public byte[] DownloadPayload { get; init; } = [];

        public IReadOnlyList<AliyunOssBucket> ListBuckets()
        {
            return Buckets;
        }

        public void CreateBucket(string bucketName)
        {
            PutBucketName = bucketName;
        }

        public AliyunOssObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
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

        public void PutObject(
            string bucketName,
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            PutBucketName = bucketName;
            PutKey = key;
            if (BlockUploadUntilCanceled)
            {
                UploadStarted.TrySetResult(true);
                var buffer = new byte[1];
                while (true)
                {
                    _ = content.Read(buffer, 0, buffer.Length);
                    Thread.Sleep(1);
                }
            }

            using var copy = new MemoryStream();
            content.CopyTo(copy);
            PutPayload = copy.ToArray();
            BeforeUploadReturns?.Invoke();
        }

        public void PutObjectMultipart(
            string bucketName,
            string key,
            Stream content,
            long contentLength,
            Action<long, long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            MultipartUploadUsed = true;
            PutObject(bucketName, key, content, cancellationToken);
            progress?.Invoke(contentLength, contentLength);
        }

        public AliyunOssDownloadObject GetObject(string bucketName, string key)
        {
            GetBucketName = bucketName;
            GetKey = key;
            return new AliyunOssDownloadObject(new MemoryStream(DownloadPayload), DownloadPayload.Length);
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
            : base(credentialRef, "aliyun-oss-provider-test")
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
