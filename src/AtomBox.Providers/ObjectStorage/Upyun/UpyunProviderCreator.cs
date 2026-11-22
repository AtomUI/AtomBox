using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.Upyun;

public sealed class UpyunProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public UpyunProviderCreator(ProviderDescriptor descriptor, ProviderCredentialResolver credentialResolver)
    {
        Descriptor = descriptor;
        _credentialResolver = credentialResolver;
    }

    public ProviderDescriptor Descriptor { get; }

    public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var bucket = account.GetProviderConfigValue("bucket");
        if (string.IsNullOrWhiteSpace(bucket))
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(StorageError.Validation("Upyun service name is required.")));
        }

        if (!string.Equals(account.GetProviderConfigValue("credentialMode"), "s3", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(StorageError.Validation(
                "This Upyun account still uses operator credentials. Edit it and save an S3 AccessKey/SecretAccessKey before connecting.")));
        }

        return AwsSdkS3ProviderFactory.CreateAsync(
            account,
            Descriptor,
            _credentialResolver,
            new AwsSdkS3ProviderProfile(
                "https://s3.api.upyun.com", "us-east-1", ForcePathStyle: true, CursorNamespace: "upyun",
                RootBucket: bucket, SingleCopyLimit: 500L * 1024 * 1024,
                MultipartCopyPartSize: 10L * 1024 * 1024),
            cancellationToken: cancellationToken);
    }
}
