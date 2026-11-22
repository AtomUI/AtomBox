using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.JdCloudOss;

public sealed class JdCloudOssProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public JdCloudOssProviderCreator(ProviderDescriptor descriptor, ProviderCredentialResolver credentialResolver)
    {
        Descriptor = descriptor;
        _credentialResolver = credentialResolver;
    }

    public ProviderDescriptor Descriptor { get; }

    public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var endpoint = AwsSdkS3ProviderFactory.NormalizeEndpoint(account.Endpoint, "JDCloud OSS");
        if (endpoint.IsFailure)
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(endpoint.Error!));
        }

        if (string.IsNullOrWhiteSpace(account.Region))
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(StorageError.Validation("JDCloud OSS region is required.")));
        }

        return AwsSdkS3ProviderFactory.CreateAsync(
            account,
            Descriptor,
            _credentialResolver,
            new AwsSdkS3ProviderProfile(
                endpoint.GetValueOrThrow(), account.Region, ForcePathStyle: true, CursorNamespace: "jdcloud-oss",
                SingleCopyLimit: 5L * 1024 * 1024 * 1024),
            cancellationToken: cancellationToken);
    }
}
