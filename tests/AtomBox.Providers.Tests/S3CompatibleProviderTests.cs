using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.Tests;

public sealed class S3CompatibleProviderTests
{
    [Fact]
    public async Task CreateFolderAsync_CreatesEmptyFolderMarkerObject()
    {
        var client = new FakeS3CompatibleClient();
        await using var provider = CreateProvider(client);

        var result = await provider.CreateFolderAsync(new RemotePath("bucket-a/folder/child", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.PutBucketName);
        Assert.Equal("folder/child/", client.PutKey);
        Assert.Empty(client.PutPayload);
    }

    [Fact]
    public async Task DeleteAsync_FolderDeletesFolderMarkerKey()
    {
        var client = new FakeS3CompatibleClient();
        await using var provider = CreateProvider(client);

        var result = await provider.DeleteAsync(new RemotePath("bucket-a/folder/child", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.DeletedBucketName);
        Assert.Equal("folder/child/", client.DeletedKey);
    }

    [Fact]
    public async Task MoveAsync_CopiesThenDeletesObject()
    {
        var client = new FakeS3CompatibleClient();
        await using var provider = CreateProvider(client);

        var result = await provider.MoveAsync(
            new RemotePath("bucket-a/source/file.txt", RemotePathKind.ObjectPath),
            new RemotePath("bucket-a/destination/file.txt", RemotePathKind.ObjectPath));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.CopySourceBucketName);
        Assert.Equal("source/file.txt", client.CopySourceKey);
        Assert.Equal("bucket-a", client.CopyDestinationBucketName);
        Assert.Equal("destination/file.txt", client.CopyDestinationKey);
        Assert.Equal("bucket-a", client.DeletedBucketName);
        Assert.Equal("source/file.txt", client.DeletedKey);
    }

    [Fact]
    public async Task MoveAsync_MovesFolderRecursively()
    {
        var client = new FakeS3CompatibleClient
        {
            ObjectListing = new S3CompatibleObjectListing(
                [new S3CompatibleObjectSummary("source-folder/file.txt", 1, null, null, null)],
                [])
        };
        await using var provider = CreateProvider(client);

        var result = await provider.MoveAsync(
            new RemotePath("bucket-a/source-folder", RemotePathKind.Folder),
            new RemotePath("bucket-a/destination-folder", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal("source-folder/file.txt", client.CopySourceKey);
        Assert.Equal("destination-folder/file.txt", client.CopyDestinationKey);
        Assert.Contains("source-folder/file.txt", client.DeletedKeys);
        Assert.Contains("source-folder/", client.DeletedKeys);
    }

    [Fact]
    public async Task RenameAsync_RenamesObjectWithinSameParent()
    {
        var client = new FakeS3CompatibleClient();
        await using var provider = CreateProvider(client);

        var result = await provider.RenameAsync(
            new RemotePath("bucket-a/folder/old.txt", RemotePathKind.ObjectPath),
            "new.txt");

        Assert.True(result.IsSuccess);
        Assert.Equal("folder/old.txt", client.CopySourceKey);
        Assert.Equal("folder/new.txt", client.CopyDestinationKey);
        Assert.Equal("folder/old.txt", client.DeletedKey);
    }

    [Fact]
    public async Task UploadAndDownload_HandleLargePayloadAndReportProgress()
    {
        var payload = Enumerable.Range(0, 1024 * 1024)
            .Select(index => (byte)(index % 251))
            .ToArray();
        var client = new FakeS3CompatibleClient
        {
            DownloadPayload = payload
        };
        await using var provider = CreateProvider(client);
        var uploadProgress = new ProgressRecorder();
        var downloadProgress = new ProgressRecorder();

        await using (var content = new MemoryStream(payload))
        {
            var upload = await provider.UploadAsync(
                new RemotePath("bucket-a/large.bin", RemotePathKind.ObjectPath),
                content,
                content.Length,
                uploadProgress);
            Assert.True(upload.IsSuccess);
        }

        await using var destination = new MemoryStream();
        var download = await provider.DownloadAsync(
            new RemotePath("bucket-a/large.bin", RemotePathKind.ObjectPath),
            destination,
            downloadProgress);

        Assert.True(download.IsSuccess);
        Assert.Equal(payload, client.PutPayload);
        Assert.Equal(payload, destination.ToArray());
        Assert.Equal(payload.Length, uploadProgress.Latest?.BytesTransferred);
        Assert.Equal(payload.Length, downloadProgress.Latest?.BytesTransferred);
    }

    [Fact]
    public async Task ListPageAsync_UsesDefaultOffsetPagination()
    {
        var client = new FakeS3CompatibleClient
        {
            ObjectListing = new S3CompatibleObjectListing(
                [
                    new S3CompatibleObjectSummary("folder/a.txt", 1, null, null, null),
                    new S3CompatibleObjectSummary("folder/b.txt", 1, null, null, null),
                    new S3CompatibleObjectSummary("folder/c.txt", 1, null, null, null)
                ],
                [])
        };
        await using var provider = CreateProvider(client);

        IStorageProvider storageProvider = provider;
        var firstPage = await storageProvider.ListPageAsync(
            new RemotePath("bucket-a/folder", RemotePathKind.Folder),
            new RemotePageRequest(2));
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(["a.txt", "b.txt"], firstPage.GetValueOrThrow().Items.Select(item => item.Name).ToArray());
        Assert.NotNull(firstPage.GetValueOrThrow().NextCursor);

        var secondPage = await storageProvider.ListPageAsync(
            new RemotePath("bucket-a/folder", RemotePathKind.Folder),
            new RemotePageRequest(2, firstPage.GetValueOrThrow().NextCursor));

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(["c.txt"], secondPage.GetValueOrThrow().Items.Select(item => item.Name).ToArray());
        Assert.Null(secondPage.GetValueOrThrow().NextCursor);
    }

    [Fact]
    public async Task MoveAsync_MapsClientFailureWithoutLeakingExceptionMessage()
    {
        var client = new FakeS3CompatibleClient
        {
            CopyException = new InvalidOperationException("secret-should-not-leak")
        };
        await using var provider = CreateProvider(client);

        var result = await provider.MoveAsync(
            new RemotePath("bucket-a/source.txt", RemotePathKind.ObjectPath),
            new RemotePath("bucket-a/destination.txt", RemotePathKind.ObjectPath));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Unknown, result.Error?.Category);
        Assert.DoesNotContain("secret-should-not-leak", result.Error?.Message, StringComparison.Ordinal);
        Assert.Null(client.DeletedKey);
    }

    [Fact]
    public async Task ListAsync_MapsCommonPrefixesAndSkipsFolderMarkerObjects()
    {
        var client = new FakeS3CompatibleClient
        {
            ObjectListing = new S3CompatibleObjectListing(
                [
                    new S3CompatibleObjectSummary("folder/", 0, null, null, null),
                    new S3CompatibleObjectSummary("folder/file.txt", 12, null, "\"etag\"", "text/plain")
                ],
                ["folder/child/"])
        };
        await using var provider = CreateProvider(client);

        var result = await provider.ListAsync(new RemotePath("bucket-a/folder", RemotePathKind.Folder));

        Assert.True(result.IsSuccess);
        Assert.Equal("bucket-a", client.LastListBucketName);
        Assert.Equal("folder/", client.LastListPrefix);
        Assert.Collection(
            result.GetValueOrThrow(),
            folder =>
            {
                Assert.Equal(RemoteItemKind.Folder, folder.Kind);
                Assert.Equal("child", folder.Name);
                Assert.Equal("bucket-a/folder/child", folder.Path.Value);
            },
            file =>
            {
                Assert.Equal(RemoteItemKind.File, file.Kind);
                Assert.Equal("file.txt", file.Name);
                Assert.Equal("bucket-a/folder/file.txt", file.Path.Value);
            });
    }

    [Fact]
    public async Task DisposeAsync_DisposesClientAndCredentialLease()
    {
        var client = new FakeS3CompatibleClient();
        var lease = new TrackableCredentialLease(new CredentialRef("cred-1"));
        var provider = CreateProvider(client, lease);

        await provider.DisposeAsync();

        Assert.True(client.WasDisposed);
        Assert.True(lease.WasDisposed);
    }

    [Fact]
    public async Task ListPageAsync_UsesSearchPrefixAsNativeS3PrefixWithoutCorruptingNames()
    {
        var client = new FakeS3CompatibleClient
        {
            ObjectListing = new S3CompatibleObjectListing(
                [new S3CompatibleObjectSummary("folder/report.txt", 64, null, null, "text/plain")],
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

    private static S3CompatibleProvider CreateProvider(
        FakeS3CompatibleClient client,
        TrackableCredentialLease? lease = null)
    {
        lease ??= new TrackableCredentialLease(new CredentialRef("cred-1"));
        return new S3CompatibleProvider(
            client,
            new CredentialMaterialLease(
                lease,
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["accessKeyId"] = "ak",
                    ["accessKeySecret"] = "sk"
                })),
            new StorageCapabilitySet(
                StorageCapability.List |
                StorageCapability.Upload |
                StorageCapability.Download |
                StorageCapability.Delete |
                StorageCapability.CreateFolder |
                StorageCapability.Rename |
                StorageCapability.Move));
    }

    private sealed class FakeS3CompatibleClient : IS3CompatibleClient
    {
        public IReadOnlyList<S3CompatibleBucket> Buckets { get; init; } = [];

        public S3CompatibleObjectListing ObjectListing { get; init; } = new([], []);

        public string? LastListBucketName { get; private set; }

        public string? LastListPrefix { get; private set; }

        public string? DeletedBucketName { get; private set; }

        public string? DeletedKey { get; private set; }

        public List<string> DeletedKeys { get; } = [];

        public string? CopySourceBucketName { get; private set; }

        public string? CopySourceKey { get; private set; }

        public string? CopyDestinationBucketName { get; private set; }

        public string? CopyDestinationKey { get; private set; }

        public string? PutBucketName { get; private set; }

        public string? PutKey { get; private set; }

        public byte[] PutPayload { get; private set; } = [];

        public byte[] DownloadPayload { get; init; } = [1, 2, 3];

        public Exception? CopyException { get; init; }

        public bool WasDisposed { get; private set; }

        public IReadOnlyList<S3CompatibleBucket> ListBuckets()
        {
            return Buckets;
        }

        public void CreateBucket(string bucketName)
        {
            PutBucketName = bucketName;
        }

        public S3CompatibleObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
        {
            LastListBucketName = bucketName;
            LastListPrefix = prefix;
            var offset = 0;
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                _ = int.TryParse(cursor, out offset);
            }

            var objects = ObjectListing.Objects.Skip(offset).Take(maxKeys).ToArray();
            var nextOffset = offset + maxKeys;
            var nextCursor = nextOffset < ObjectListing.Objects.Count ? nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            return new S3CompatibleObjectListing(objects, ObjectListing.CommonPrefixes, nextCursor);
        }

        public void DeleteObject(string bucketName, string key)
        {
            DeletedBucketName = bucketName;
            DeletedKey = key;
            DeletedKeys.Add(key);
        }

        public void CopyObject(
            string sourceBucketName,
            string sourceKey,
            string destinationBucketName,
            string destinationKey)
        {
            if (CopyException is not null)
            {
                throw CopyException;
            }

            CopySourceBucketName = sourceBucketName;
            CopySourceKey = sourceKey;
            CopyDestinationBucketName = destinationBucketName;
            CopyDestinationKey = destinationKey;
        }

        public void PutObject(string bucketName, string key, Stream content)
        {
            PutBucketName = bucketName;
            PutKey = key;
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            PutPayload = copy.ToArray();
        }

        public void PutObjectMultipart(string bucketName, string key, Stream content, long contentLength, Action<long, long>? progress = null)
        {
            PutObject(bucketName, key, content);
            progress?.Invoke(contentLength, contentLength);
        }

        public S3CompatibleDownloadObject GetObject(string bucketName, string key)
        {
            return new S3CompatibleDownloadObject(new MemoryStream(DownloadPayload), DownloadPayload.LongLength);
        }

        public void Dispose()
        {
            WasDisposed = true;
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

    private sealed class TrackableCredentialLease : CredentialLease
    {
        public TrackableCredentialLease(CredentialRef credentialRef)
            : base(credentialRef, "s3-compatible-provider-test")
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
