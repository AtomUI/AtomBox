using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.AliyunOss;
using System.Text;

namespace AtomBox.Providers.Tests;

public sealed class AliyunOssIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task AliyunOssIntegration_ListUploadDownloadDeleteSmallObject()
    {
        var environment = AliyunOssTestEnvironment.TryRead();
        if (environment is null)
        {
            return;
        }

        await using var provider = await CreateProviderAsync(environment);
        var objectPath = new RemotePath(
            $"{environment.Bucket}/{environment.CreateObjectKey()}",
            RemotePathKind.ObjectPath);
        var payload = Encoding.UTF8.GetBytes($"AtomBox OSS integration test {Guid.NewGuid():N}");
        var uploaded = false;

        try
        {
            var listBeforeUpload = await provider.ListAsync(new RemotePath(environment.Bucket, RemotePathKind.BucketRoot));
            Assert.True(listBeforeUpload.IsSuccess, FormatError("Expected bucket root list to succeed.", listBeforeUpload.Error));

            await using (var upload = new MemoryStream(payload))
            {
                var uploadResult = await provider.UploadAsync(objectPath, upload, upload.Length);
                Assert.True(uploadResult.IsSuccess, FormatError("Expected upload to succeed.", uploadResult.Error));
                uploaded = true;
            }

            var parentPath = objectPath.GetParent() ?? new RemotePath(environment.Bucket, RemotePathKind.BucketRoot);
            var listAfterUpload = await provider.ListAsync(parentPath);
            Assert.True(listAfterUpload.IsSuccess, FormatError("Expected parent list to succeed.", listAfterUpload.Error));
            Assert.Contains(listAfterUpload.GetValueOrThrow(), item => item.Name == objectPath.Name);

            await using (var download = new MemoryStream())
            {
                var downloadResult = await provider.DownloadAsync(objectPath, download);
                Assert.True(downloadResult.IsSuccess, FormatError("Expected download to succeed.", downloadResult.Error));
                Assert.Equal(payload, download.ToArray());
            }

            var deleteResult = await provider.DeleteAsync(objectPath);
            Assert.True(deleteResult.IsSuccess, FormatError("Expected delete to succeed.", deleteResult.Error));
            uploaded = false;

            await using var afterDeleteDownload = new MemoryStream();
            var missingResult = await provider.DownloadAsync(objectPath, afterDeleteDownload);
            Assert.True(missingResult.IsFailure);
        }
        finally
        {
            if (uploaded)
            {
                await provider.DeleteAsync(objectPath);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AliyunOssIntegration_ListsPseudoFoldersAndPrefixScopedObjects()
    {
        var environment = AliyunOssTestEnvironment.TryRead();
        if (environment is null)
        {
            return;
        }

        await using var provider = await CreateProviderAsync(environment);
        var testFolder = environment.CreateFolderPrefix();
        const string childFolderName = "child";
        const string fileName = "prefix-list.txt";
        var objectKey = $"{testFolder}{childFolderName}/{fileName}";
        var objectPath = new RemotePath($"{environment.Bucket}/{objectKey}", RemotePathKind.ObjectPath);
        var parentPrefixPath = new RemotePath($"{environment.Bucket}/{testFolder.TrimEnd('/')}", RemotePathKind.Folder);
        var childPrefixPath = new RemotePath($"{environment.Bucket}/{testFolder}{childFolderName}", RemotePathKind.Folder);
        var uploaded = false;

        try
        {
            await using (var upload = new MemoryStream(Encoding.UTF8.GetBytes("AtomBox OSS prefix listing probe")))
            {
                var uploadResult = await provider.UploadAsync(objectPath, upload, upload.Length);
                Assert.True(uploadResult.IsSuccess, FormatError("Expected upload to succeed.", uploadResult.Error));
                uploaded = true;
            }

            var parentList = await provider.ListAsync(parentPrefixPath);
            Assert.True(parentList.IsSuccess, FormatError("Expected parent prefix list to succeed.", parentList.Error));
            var childFolder = Assert.Single(parentList.GetValueOrThrow(), item => item.Name == childFolderName);
            Assert.Equal(RemoteItemKind.Folder, childFolder.Kind);

            var childList = await provider.ListAsync(childPrefixPath);
            Assert.True(childList.IsSuccess, FormatError("Expected child prefix list to succeed.", childList.Error));
            var file = Assert.Single(childList.GetValueOrThrow(), item => item.Name == fileName);
            Assert.Equal(RemoteItemKind.File, file.Kind);
        }
        finally
        {
            if (uploaded)
            {
                await provider.DeleteAsync(objectPath);
            }
        }
    }

    [Fact]
    [Trait("Category", "ManualIntegration")]
    public async Task AliyunOssManualUpload_UploadTxtObject_WhenExplicitlyEnabled()
    {
        var environment = AliyunOssTestEnvironment.TryRead();
        if (environment is null ||
            !string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_ALIYUN_OSS_MANUAL_UPLOAD"), "1", StringComparison.Ordinal))
        {
            return;
        }

        await using var provider = await CreateProviderAsync(environment);
        var key = Environment.GetEnvironmentVariable("ATOMBOX_OSS_MANUAL_OBJECT_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            key = environment.CreateManualObjectKey();
        }

        var objectPath = new RemotePath($"{environment.Bucket}/{key}", RemotePathKind.ObjectPath);
        var payload = Encoding.UTF8.GetBytes(
            $"AtomBox manual OSS upload probe{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        await using var upload = new MemoryStream(payload);
        var uploadResult = await provider.UploadAsync(objectPath, upload, upload.Length);

        Assert.True(uploadResult.IsSuccess, $"Expected manual upload to succeed. Category: {uploadResult.Error?.Category}");
    }

    private static async Task<IStorageProvider> CreateProviderAsync(AliyunOssTestEnvironment environment)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.AliyunOssProviderId);
        var creator = new AliyunOssProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new EnvironmentCredentialStore(environment)));
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderRegistry.AliyunOssProviderId,
            "Aliyun OSS Integration",
            environment.Endpoint,
            environment.Region,
            new CredentialRef("aliyun-oss-integration"),
            now,
            now,
            new Dictionary<string, string>
            {
                ["bucket"] = environment.Bucket
            });

        var result = await creator.CreateAsync(account);
        Assert.True(result.IsSuccess, $"Expected provider creation to succeed. Category: {result.Error?.Category}");
        return result.GetValueOrThrow();
    }

    private static string FormatError(string message, StorageError? error)
    {
        return error is null
            ? message
            : $"{message} Code={error.Code}; Category={error.Category}; Retryable={error.IsRetryable}; ProviderErrorCode={error.ProviderErrorCode}; Message={error.Message}";
    }

    private sealed record AliyunOssTestEnvironment(
        string Endpoint,
        string? Region,
        string Bucket,
        string AccessKeyId,
        string AccessKeySecret,
        string TestPrefix)
    {
        public static AliyunOssTestEnvironment? TryRead()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_ALIYUN_OSS"), "1", StringComparison.Ordinal))
            {
                return null;
            }

            var endpoint = Environment.GetEnvironmentVariable("ATOMBOX_OSS_ENDPOINT");
            var bucket = Environment.GetEnvironmentVariable("ATOMBOX_OSS_BUCKET");
            var accessKeyId = Environment.GetEnvironmentVariable("ATOMBOX_OSS_ACCESS_KEY_ID");
            var accessKeySecret = Environment.GetEnvironmentVariable("ATOMBOX_OSS_ACCESS_KEY_SECRET");
            if (string.IsNullOrWhiteSpace(endpoint) ||
                string.IsNullOrWhiteSpace(bucket) ||
                string.IsNullOrWhiteSpace(accessKeyId) ||
                string.IsNullOrWhiteSpace(accessKeySecret))
            {
                return null;
            }

            var prefix = Environment.GetEnvironmentVariable("ATOMBOX_OSS_TEST_PREFIX");
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "atombox-tests/";
            }

            return new AliyunOssTestEnvironment(
                endpoint.Trim(),
                Environment.GetEnvironmentVariable("ATOMBOX_OSS_REGION")?.Trim(),
                bucket.Trim(),
                accessKeyId.Trim(),
                accessKeySecret.Trim(),
                NormalizePrefix(prefix));
        }

        public string CreateObjectKey()
        {
            return $"{TestPrefix}{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.txt";
        }

        public string CreateManualObjectKey()
        {
            return $"{TestPrefix}manual-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.txt";
        }

        public string CreateFolderPrefix()
        {
            return $"{TestPrefix}prefix-list-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}/";
        }

        private static string NormalizePrefix(string prefix)
        {
            return prefix.Trim().TrimStart('/').TrimEnd('/') + "/";
        }
    }

    private sealed class EnvironmentCredentialStore : ICredentialStore
    {
        private readonly AliyunOssTestEnvironment _environment;
        private readonly CredentialRef _credentialRef = new("aliyun-oss-integration");

        public EnvironmentCredentialStore(AliyunOssTestEnvironment environment)
        {
            _environment = environment;
        }

        public Task<OperationResult<CredentialRef>> SaveAsync(
            CredentialSecretMaterial material,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialRef>.Success(_credentialRef));
        }

        public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialLease>.Success(new EnvironmentCredentialLease(_credentialRef)));
        }

        public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            var lease = new CredentialMaterialLease(
                new EnvironmentCredentialLease(_credentialRef),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["accessKeyId"] = _environment.AccessKeyId,
                    ["accessKeySecret"] = _environment.AccessKeySecret
                }));

            return Task.FromResult(OperationResult<CredentialMaterialLease>.Success(lease));
        }

        public Task<OperationResult<bool>> ExistsAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<bool>.Success(credentialRef == _credentialRef));
        }

        public Task<OperationResult> MarkPendingDeleteAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class EnvironmentCredentialLease : CredentialLease
    {
        public EnvironmentCredentialLease(CredentialRef credentialRef)
            : base(credentialRef, "aliyun-oss-integration")
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
