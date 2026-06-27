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

    public async Task<OperationResult<IStorageProvider>> CreateAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (string.IsNullOrWhiteSpace(account.Endpoint))
        {
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("JDCloud OSS endpoint is required."));
        }

        var region = account.GetProviderConfigValue("region");
        if (string.IsNullOrWhiteSpace(region))
        {
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("JDCloud OSS region is required."));
        }

        var materialResult = await _credentialResolver.AcquireMaterialAsync(account, cancellationToken).ConfigureAwait(false);
        if (materialResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(materialResult.Error!);
        }

        var materialLease = materialResult.GetValueOrThrow();
        try
        {
            var validation = ValidateSecretMaterial(materialLease.Material);
            if (validation.IsFailure)
            {
                await materialLease.DisposeAsync().ConfigureAwait(false);
                return OperationResult<IStorageProvider>.Failure(validation.Error!);
            }

            var accessKeyId = materialLease.Material.GetRequiredValue("accessKeyId");
            var accessKeySecret = materialLease.Material.GetRequiredValue("accessKeySecret");
            var client = new S3CompatibleAwsV4Client(account.Endpoint, region, accessKeyId, accessKeySecret);
            return OperationResult<IStorageProvider>.Success(
                new S3CompatibleProvider(client, materialLease, Descriptor.Capabilities));
        }
        catch (Exception exception)
        {
            await materialLease.DisposeAsync().ConfigureAwait(false);
            return OperationResult<IStorageProvider>.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    private static OperationResult ValidateSecretMaterial(CredentialSecretMaterial material)
    {
        if (!material.TryGetValue("accessKeyId", out _))
        {
            return OperationResult.Failure(StorageError.Validation("JDCloud OSS credential requires accessKeyId."));
        }

        if (!material.TryGetValue("accessKeySecret", out _))
        {
            return OperationResult.Failure(StorageError.Validation("JDCloud OSS credential requires accessKeySecret."));
        }

        return OperationResult.Success();
    }
}
