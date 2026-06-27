using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.QiniuKodo;
using System.Text;

namespace AtomBox.Providers.Tests;

public sealed class QiniuKodoIntegrationTests
{
    [Fact]
    [Trait("Category", "ManualIntegration")]
    public async Task QiniuKodoManualUpload_UploadTxtObject_WhenExplicitlyEnabled()
    {
        var environment = QiniuKodoTestEnvironment.TryRead();
        if (environment is null ||
            !string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_QINIU_KODO_MANUAL_UPLOAD"), "1", StringComparison.Ordinal))
        {
            return;
        }

        await using var provider = await CreateProviderAsync(environment);
        var key = Environment.GetEnvironmentVariable("ATOMBOX_QINIU_KODO_MANUAL_OBJECT_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            key = environment.CreateManualObjectKey();
        }

        var objectPath = new RemotePath($"{environment.Bucket}/{key}", RemotePathKind.ObjectPath);
        var payload = Encoding.UTF8.GetBytes(
            $"AtomBox manual Qiniu Kodo upload probe{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        await using var upload = new MemoryStream(payload);
        var uploadResult = await provider.UploadAsync(objectPath, upload, upload.Length);

        Assert.True(uploadResult.IsSuccess, FormatError("Expected manual upload to succeed.", uploadResult.Error));
    }

    private static async Task<IStorageProvider> CreateProviderAsync(QiniuKodoTestEnvironment environment)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.QiniuKodoProviderId);
        var creator = new QiniuKodoProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new EnvironmentCredentialStore(environment)));
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderRegistry.QiniuKodoProviderId,
            "Qiniu Kodo Integration",
            environment.Endpoint,
            environment.Region,
            new CredentialRef("qiniu-kodo-integration"),
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

    private sealed record QiniuKodoTestEnvironment(
        string Endpoint,
        string Region,
        string Bucket,
        string AccessKey,
        string SecretKey,
        string TestPrefix)
    {
        public static QiniuKodoTestEnvironment? TryRead()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_QINIU_KODO"), "1", StringComparison.Ordinal))
            {
                return null;
            }

            var endpoint = Environment.GetEnvironmentVariable("ATOMBOX_QINIU_KODO_ENDPOINT");
            var region = Environment.GetEnvironmentVariable("ATOMBOX_QINIU_KODO_REGION");
            var bucket = Environment.GetEnvironmentVariable("ATOMBOX_QINIU_KODO_BUCKET");
            var accessKey = Environment.GetEnvironmentVariable("ATOMBOX_QINIU_KODO_ACCESS_KEY");
            var secretKey = Environment.GetEnvironmentVariable("ATOMBOX_QINIU_KODO_SECRET_KEY");
            if (string.IsNullOrWhiteSpace(endpoint) ||
                string.IsNullOrWhiteSpace(region) ||
                string.IsNullOrWhiteSpace(bucket) ||
                string.IsNullOrWhiteSpace(accessKey) ||
                string.IsNullOrWhiteSpace(secretKey))
            {
                return null;
            }

            var prefix = Environment.GetEnvironmentVariable("ATOMBOX_QINIU_KODO_TEST_PREFIX");
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "atombox-tests/";
            }

            return new QiniuKodoTestEnvironment(
                endpoint.Trim(),
                region.Trim(),
                bucket.Trim(),
                accessKey.Trim(),
                secretKey.Trim(),
                NormalizePrefix(prefix));
        }

        public string CreateManualObjectKey()
        {
            return $"{TestPrefix}manual-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.txt";
        }

        private static string NormalizePrefix(string prefix)
        {
            return prefix.Trim().TrimStart('/').TrimEnd('/') + "/";
        }
    }

    private sealed class EnvironmentCredentialStore : ICredentialStore
    {
        private readonly QiniuKodoTestEnvironment _environment;
        private readonly CredentialRef _credentialRef = new("qiniu-kodo-integration");

        public EnvironmentCredentialStore(QiniuKodoTestEnvironment environment)
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
                    ["accessKeyId"] = _environment.AccessKey,
                    ["accessKeySecret"] = _environment.SecretKey
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
            : base(credentialRef, "qiniu-kodo-integration")
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
