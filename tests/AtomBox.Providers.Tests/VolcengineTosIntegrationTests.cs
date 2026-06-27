using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.VolcengineTos;
using System.Text;

namespace AtomBox.Providers.Tests;

public sealed class VolcengineTosIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task VolcengineTosIntegration_ListUploadPrefixListDownloadAndKeepObjects()
    {
        var environment = VolcengineTosTestEnvironment.TryRead();
        if (environment is null)
        {
            return;
        }

        await using var provider = await CreateProviderAsync(environment);
        var folderPrefix = environment.CreateVisibleFolderPrefix();
        var rootObjectKey = $"{folderPrefix}visible-root-file.txt";
        var childObjectKey = $"{folderPrefix}child/visible-child-file.txt";
        var rootObjectPath = new RemotePath($"{environment.Bucket}/{rootObjectKey}", RemotePathKind.ObjectPath);
        var childObjectPath = new RemotePath($"{environment.Bucket}/{childObjectKey}", RemotePathKind.ObjectPath);
        var folderPath = new RemotePath($"{environment.Bucket}/{folderPrefix.TrimEnd('/')}", RemotePathKind.Folder);
        var childFolderPath = new RemotePath($"{environment.Bucket}/{folderPrefix}child", RemotePathKind.Folder);
        var rootPayload = Encoding.UTF8.GetBytes(
            $"AtomBox Volcengine TOS visible root object{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        var childPayload = Encoding.UTF8.GetBytes(
            $"AtomBox Volcengine TOS visible child object{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        var bucketList = await provider.ListAsync(new RemotePath(environment.Bucket, RemotePathKind.BucketRoot));
        Assert.True(bucketList.IsSuccess, FormatError("Expected TOS bucket root list to succeed.", bucketList.Error));

        await using (var rootUpload = new MemoryStream(rootPayload))
        {
            var uploadResult = await provider.UploadAsync(rootObjectPath, rootUpload, rootUpload.Length);
            Assert.True(uploadResult.IsSuccess, FormatError("Expected TOS root object upload to succeed.", uploadResult.Error));
        }

        await using (var childUpload = new MemoryStream(childPayload))
        {
            var uploadResult = await provider.UploadAsync(childObjectPath, childUpload, childUpload.Length);
            Assert.True(uploadResult.IsSuccess, FormatError("Expected TOS child object upload to succeed.", uploadResult.Error));
        }

        var folderList = await provider.ListAsync(folderPath);
        Assert.True(folderList.IsSuccess, FormatError("Expected TOS folder prefix list to succeed.", folderList.Error));
        Assert.Contains(folderList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "visible-root-file.txt");
        Assert.Contains(folderList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.Folder && item.Name == "child");

        var childFolderList = await provider.ListAsync(childFolderPath);
        Assert.True(childFolderList.IsSuccess, FormatError("Expected TOS child prefix list to succeed.", childFolderList.Error));
        Assert.Contains(childFolderList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "visible-child-file.txt");

        await using (var rootDownload = new MemoryStream())
        {
            var downloadResult = await provider.DownloadAsync(rootObjectPath, rootDownload);
            Assert.True(downloadResult.IsSuccess, FormatError("Expected TOS root object download to succeed.", downloadResult.Error));
            Assert.Equal(rootPayload, rootDownload.ToArray());
        }

        await using (var childDownload = new MemoryStream())
        {
            var downloadResult = await provider.DownloadAsync(childObjectPath, childDownload);
            Assert.True(downloadResult.IsSuccess, FormatError("Expected TOS child object download to succeed.", downloadResult.Error));
            Assert.Equal(childPayload, childDownload.ToArray());
        }
    }

    private static async Task<IStorageProvider> CreateProviderAsync(VolcengineTosTestEnvironment environment)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.VolcengineTosProviderId);
        var creator = new VolcengineTosProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new EnvironmentCredentialStore(environment)));
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderRegistry.VolcengineTosProviderId,
            "Volcengine TOS Integration",
            environment.Endpoint,
            environment.Region,
            new CredentialRef("volcengine-tos-integration"),
            now,
            now,
            new Dictionary<string, string>
            {
                ["bucket"] = environment.Bucket,
                ["region"] = environment.Region
            });

        var result = await creator.CreateAsync(account);
        Assert.True(result.IsSuccess, FormatError("Expected TOS provider creation to succeed.", result.Error));
        return result.GetValueOrThrow();
    }

    private static string FormatError(string message, StorageError? error)
    {
        return error is null
            ? message
            : $"{message} Code={error.Code}; Category={error.Category}; Retryable={error.IsRetryable}; ProviderErrorCode={error.ProviderErrorCode}; Message={error.Message}";
    }

    private sealed record VolcengineTosTestEnvironment(
        string Endpoint,
        string Region,
        string Bucket,
        string AccessKeyId,
        string AccessKeySecret,
        string TestPrefix)
    {
        public static VolcengineTosTestEnvironment? TryRead()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_VOLCENGINE_TOS"), "1", StringComparison.Ordinal))
            {
                return null;
            }

            var endpoint = Environment.GetEnvironmentVariable("ATOMBOX_VOLCENGINE_TOS_ENDPOINT");
            var region = Environment.GetEnvironmentVariable("ATOMBOX_VOLCENGINE_TOS_REGION");
            var bucket = Environment.GetEnvironmentVariable("ATOMBOX_VOLCENGINE_TOS_BUCKET");
            var accessKeyId = Environment.GetEnvironmentVariable("ATOMBOX_VOLCENGINE_TOS_ACCESS_KEY_ID");
            var accessKeySecret = Environment.GetEnvironmentVariable("ATOMBOX_VOLCENGINE_TOS_ACCESS_KEY_SECRET");
            if (string.IsNullOrWhiteSpace(endpoint) ||
                string.IsNullOrWhiteSpace(region) ||
                string.IsNullOrWhiteSpace(bucket) ||
                string.IsNullOrWhiteSpace(accessKeyId) ||
                string.IsNullOrWhiteSpace(accessKeySecret))
            {
                return null;
            }

            var prefix = Environment.GetEnvironmentVariable("ATOMBOX_VOLCENGINE_TOS_TEST_PREFIX");
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "atombox-tests/";
            }

            return new VolcengineTosTestEnvironment(
                endpoint.Trim(),
                region.Trim(),
                bucket.Trim(),
                accessKeyId.Trim(),
                accessKeySecret.Trim(),
                NormalizePrefix(prefix));
        }

        public string CreateVisibleFolderPrefix()
        {
            var explicitPrefix = Environment.GetEnvironmentVariable("ATOMBOX_VOLCENGINE_TOS_MANUAL_PREFIX");
            if (!string.IsNullOrWhiteSpace(explicitPrefix))
            {
                return NormalizePrefix(explicitPrefix);
            }

            return $"{TestPrefix}volcengine-tos-visible-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}/";
        }

        private static string NormalizePrefix(string prefix)
        {
            return prefix.Trim().TrimStart('/').TrimEnd('/') + "/";
        }
    }

    private sealed class EnvironmentCredentialStore : ICredentialStore
    {
        private readonly VolcengineTosTestEnvironment _environment;
        private readonly CredentialRef _credentialRef = new("volcengine-tos-integration");

        public EnvironmentCredentialStore(VolcengineTosTestEnvironment environment)
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
            : base(credentialRef, "volcengine-tos-integration")
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
