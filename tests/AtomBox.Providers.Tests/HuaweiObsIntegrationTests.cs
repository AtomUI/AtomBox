using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.HuaweiObs;
using System.Text;

namespace AtomBox.Providers.Tests;

public sealed class HuaweiObsIntegrationTests
{
    [Fact]
    [Trait("Category", "ManualIntegration")]
    public async Task HuaweiObsManualFunctionalTest_UploadListDownloadAndKeepTxtObject_WhenExplicitlyEnabled()
    {
        var environment = HuaweiObsTestEnvironment.TryRead();
        if (environment is null ||
            !string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_HUAWEI_OBS_MANUAL_UPLOAD"), "1", StringComparison.Ordinal))
        {
            return;
        }

        await using var provider = await CreateProviderAsync(environment);
        var key = Environment.GetEnvironmentVariable("ATOMBOX_HUAWEI_OBS_MANUAL_OBJECT_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            key = environment.CreateManualObjectKey();
        }

        var objectPath = new RemotePath($"{environment.Bucket}/{key}", RemotePathKind.ObjectPath);
        var payload = Encoding.UTF8.GetBytes(
            $"AtomBox manual Huawei OBS functional probe{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        var rootList = await provider.ListAsync(new RemotePath(environment.Bucket, RemotePathKind.BucketRoot));
        Assert.True(rootList.IsSuccess, FormatError("Expected bucket root list to succeed.", rootList.Error));

        await using (var upload = new MemoryStream(payload))
        {
            var uploadResult = await provider.UploadAsync(objectPath, upload, upload.Length);
            Assert.True(uploadResult.IsSuccess, FormatError("Expected upload to succeed.", uploadResult.Error));
        }

        var parentPath = objectPath.GetParent() ?? new RemotePath(environment.Bucket, RemotePathKind.BucketRoot);
        var parentList = await provider.ListAsync(parentPath);
        Assert.True(parentList.IsSuccess, FormatError("Expected parent list to succeed.", parentList.Error));
        Assert.Contains(parentList.GetValueOrThrow(), item => item.Name == objectPath.Name);

        await using var download = new MemoryStream();
        var downloadResult = await provider.DownloadAsync(objectPath, download);
        Assert.True(downloadResult.IsSuccess, FormatError("Expected download to succeed.", downloadResult.Error));
        Assert.Equal(payload, download.ToArray());
    }

    [Fact]
    [Trait("Category", "ManualIntegration")]
    public async Task HuaweiObsManualPrefixFunctionalTest_UploadFolderObjectsAndKeep_WhenExplicitlyEnabled()
    {
        var environment = HuaweiObsTestEnvironment.TryRead();
        if (environment is null ||
            !string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_HUAWEI_OBS_MANUAL_PREFIX_UPLOAD"), "1", StringComparison.Ordinal))
        {
            return;
        }

        await using var provider = await CreateProviderAsync(environment);
        var prefix = environment.CreateManualPrefix();
        var rootObjectPath = new RemotePath($"{environment.Bucket}/{prefix}visible-root-file.txt", RemotePathKind.ObjectPath);
        var childObjectPath = new RemotePath($"{environment.Bucket}/{prefix}child/visible-child-file.txt", RemotePathKind.ObjectPath);
        var folderPath = new RemotePath($"{environment.Bucket}/{prefix.TrimEnd('/')}", RemotePathKind.Folder);
        var childFolderPath = new RemotePath($"{environment.Bucket}/{prefix}child", RemotePathKind.Folder);
        var rootPayload = Encoding.UTF8.GetBytes(
            $"AtomBox manual Huawei OBS root object{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        var childPayload = Encoding.UTF8.GetBytes(
            $"AtomBox manual Huawei OBS child object{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        await using (var upload = new MemoryStream(rootPayload))
        {
            var uploadResult = await provider.UploadAsync(rootObjectPath, upload, upload.Length);
            Assert.True(uploadResult.IsSuccess, FormatError("Expected root object upload to succeed.", uploadResult.Error));
        }

        await using (var upload = new MemoryStream(childPayload))
        {
            var uploadResult = await provider.UploadAsync(childObjectPath, upload, upload.Length);
            Assert.True(uploadResult.IsSuccess, FormatError("Expected child object upload to succeed.", uploadResult.Error));
        }

        var folderList = await provider.ListAsync(folderPath);
        Assert.True(folderList.IsSuccess, FormatError("Expected folder prefix list to succeed.", folderList.Error));
        Assert.Contains(folderList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "visible-root-file.txt");
        Assert.Contains(folderList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.Folder && item.Name == "child");

        var childFolderList = await provider.ListAsync(childFolderPath);
        Assert.True(childFolderList.IsSuccess, FormatError("Expected child prefix list to succeed.", childFolderList.Error));
        Assert.Contains(childFolderList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "visible-child-file.txt");

        await using (var download = new MemoryStream())
        {
            var downloadResult = await provider.DownloadAsync(rootObjectPath, download);
            Assert.True(downloadResult.IsSuccess, FormatError("Expected root object download to succeed.", downloadResult.Error));
            Assert.Equal(rootPayload, download.ToArray());
        }

        await using (var download = new MemoryStream())
        {
            var downloadResult = await provider.DownloadAsync(childObjectPath, download);
            Assert.True(downloadResult.IsSuccess, FormatError("Expected child object download to succeed.", downloadResult.Error));
            Assert.Equal(childPayload, download.ToArray());
        }
    }

    private static async Task<IStorageProvider> CreateProviderAsync(HuaweiObsTestEnvironment environment)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.HuaweiObsProviderId);
        var creator = new HuaweiObsProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new EnvironmentCredentialStore(environment)));
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderRegistry.HuaweiObsProviderId,
            "Huawei OBS Integration",
            environment.Endpoint,
            environment.Region,
            new CredentialRef("huawei-obs-integration"),
            now,
            now,
            new Dictionary<string, string>
            {
                ["bucket"] = environment.Bucket
            });

        var result = await creator.CreateAsync(account);
        Assert.True(result.IsSuccess, FormatError("Expected provider creation to succeed.", result.Error));
        return result.GetValueOrThrow();
    }

    private static string FormatError(string message, StorageError? error)
    {
        return error is null
            ? message
            : $"{message} Code={error.Code}; Category={error.Category}; Retryable={error.IsRetryable}; ProviderErrorCode={error.ProviderErrorCode}; Message={error.Message}";
    }

    private sealed record HuaweiObsTestEnvironment(
        string Endpoint,
        string Region,
        string Bucket,
        string AccessKeyId,
        string AccessKeySecret,
        string TestPrefix)
    {
        public static HuaweiObsTestEnvironment? TryRead()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_HUAWEI_OBS"), "1", StringComparison.Ordinal))
            {
                return null;
            }

            var endpoint = Environment.GetEnvironmentVariable("ATOMBOX_HUAWEI_OBS_ENDPOINT");
            var region = Environment.GetEnvironmentVariable("ATOMBOX_HUAWEI_OBS_REGION");
            var bucket = Environment.GetEnvironmentVariable("ATOMBOX_HUAWEI_OBS_BUCKET");
            var accessKeyId = Environment.GetEnvironmentVariable("ATOMBOX_HUAWEI_OBS_ACCESS_KEY_ID");
            var accessKeySecret = Environment.GetEnvironmentVariable("ATOMBOX_HUAWEI_OBS_ACCESS_KEY_SECRET");
            if (string.IsNullOrWhiteSpace(endpoint) ||
                string.IsNullOrWhiteSpace(region) ||
                string.IsNullOrWhiteSpace(bucket) ||
                string.IsNullOrWhiteSpace(accessKeyId) ||
                string.IsNullOrWhiteSpace(accessKeySecret))
            {
                return null;
            }

            var prefix = Environment.GetEnvironmentVariable("ATOMBOX_HUAWEI_OBS_TEST_PREFIX");
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "atombox-tests/";
            }

            return new HuaweiObsTestEnvironment(
                endpoint.Trim(),
                region.Trim(),
                bucket.Trim(),
                accessKeyId.Trim(),
                accessKeySecret.Trim(),
                NormalizePrefix(prefix));
        }

        public string CreateManualObjectKey()
        {
            return $"{TestPrefix}manual-uploads/atombox-huawei-obs-manual-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.txt";
        }

        public string CreateManualPrefix()
        {
            var prefix = Environment.GetEnvironmentVariable("ATOMBOX_HUAWEI_OBS_MANUAL_PREFIX");
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = $"{TestPrefix}manual-prefix-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            }

            return NormalizePrefix(prefix);
        }

        private static string NormalizePrefix(string prefix)
        {
            return prefix.Trim().TrimStart('/').TrimEnd('/') + "/";
        }
    }

    private sealed class EnvironmentCredentialStore : ICredentialStore
    {
        private readonly HuaweiObsTestEnvironment _environment;
        private readonly CredentialRef _credentialRef = new("huawei-obs-integration");

        public EnvironmentCredentialStore(HuaweiObsTestEnvironment environment)
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
            : base(credentialRef, "huawei-obs-integration")
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
