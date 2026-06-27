using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.Upyun;
using System.Text;

namespace AtomBox.Providers.Tests;

public sealed class UpyunIntegrationTests
{
    [Fact]
    [Trait("Category", "ManualIntegration")]
    public async Task UpyunManualUpload_UploadTxtObject_WhenExplicitlyEnabled()
    {
        var environment = UpyunTestEnvironment.TryRead();
        if (environment is null ||
            !string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_UPYUN_MANUAL_UPLOAD"), "1", StringComparison.Ordinal))
        {
            return;
        }

        await using var provider = await CreateProviderAsync(environment);
        var key = Environment.GetEnvironmentVariable("ATOMBOX_UPYUN_MANUAL_OBJECT_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            key = environment.CreateManualObjectKey();
        }

        var objectPath = new RemotePath($"{environment.Service}/{key}", RemotePathKind.ObjectPath);
        var payload = Encoding.UTF8.GetBytes(
            $"AtomBox manual Upyun upload probe{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        await using var upload = new MemoryStream(payload);
        var uploadResult = await provider.UploadAsync(objectPath, upload, upload.Length);

        Assert.True(uploadResult.IsSuccess, FormatError("Expected manual upload to succeed.", uploadResult.Error));
    }

    private static async Task<IStorageProvider> CreateProviderAsync(UpyunTestEnvironment environment)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.UpyunProviderId);
        var creator = new UpyunProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new EnvironmentCredentialStore(environment)));
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderRegistry.UpyunProviderId,
            "Upyun Integration",
            environment.Endpoint,
            null,
            new CredentialRef("upyun-integration"),
            now,
            now,
            new Dictionary<string, string>
            {
                ["bucket"] = environment.Service
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

    private sealed record UpyunTestEnvironment(
        string Endpoint,
        string Service,
        string Operator,
        string Password,
        string TestPrefix)
    {
        public static UpyunTestEnvironment? TryRead()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_UPYUN"), "1", StringComparison.Ordinal))
            {
                return null;
            }

            var endpoint = Environment.GetEnvironmentVariable("ATOMBOX_UPYUN_ENDPOINT");
            var service = Environment.GetEnvironmentVariable("ATOMBOX_UPYUN_SERVICE");
            var operatorName = Environment.GetEnvironmentVariable("ATOMBOX_UPYUN_OPERATOR");
            var password = Environment.GetEnvironmentVariable("ATOMBOX_UPYUN_PASSWORD");
            if (string.IsNullOrWhiteSpace(endpoint) ||
                string.IsNullOrWhiteSpace(service) ||
                string.IsNullOrWhiteSpace(operatorName) ||
                string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            var prefix = Environment.GetEnvironmentVariable("ATOMBOX_UPYUN_TEST_PREFIX");
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "atombox-tests/";
            }

            return new UpyunTestEnvironment(
                endpoint.Trim(),
                service.Trim(),
                operatorName.Trim(),
                password.Trim(),
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
        private readonly UpyunTestEnvironment _environment;
        private readonly CredentialRef _credentialRef = new("upyun-integration");

        public EnvironmentCredentialStore(UpyunTestEnvironment environment)
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
                    ["accessKeyId"] = _environment.Operator,
                    ["accessKeySecret"] = _environment.Password
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
            : base(credentialRef, "upyun-integration")
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
