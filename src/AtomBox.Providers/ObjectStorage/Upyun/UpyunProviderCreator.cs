using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.ObjectStorage.Upyun;

public sealed class UpyunProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public UpyunProviderCreator(
        ProviderDescriptor descriptor,
        ProviderCredentialResolver credentialResolver)
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
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("Upyun endpoint is required."));
        }

        var bucket = account.GetProviderConfigValue("bucket");
        if (string.IsNullOrWhiteSpace(bucket))
        {
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("Upyun service name is required."));
        }

        var materialResult = await _credentialResolver.AcquireMaterialAsync(account, cancellationToken).ConfigureAwait(false);
        if (materialResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(materialResult.Error!);
        }

        var materialLease = materialResult.GetValueOrThrow();
        try
        {
            var secretValidation = ValidateSecretMaterial(materialLease.Material);
            if (secretValidation.IsFailure)
            {
                await materialLease.DisposeAsync().ConfigureAwait(false);
                return OperationResult<IStorageProvider>.Failure(secretValidation.Error!);
            }

            var operatorName = materialLease.Material.GetRequiredValue("accessKeyId");
            var password = materialLease.Material.GetRequiredValue("accessKeySecret");
            var client = new UpyunRestClient(account.Endpoint, bucket, operatorName, password);

            return OperationResult<IStorageProvider>.Success(
                new UpyunProvider(client, materialLease, Descriptor.Capabilities));
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
            return OperationResult.Failure(StorageError.Validation("Upyun credential requires operator name."));
        }

        if (!material.TryGetValue("accessKeySecret", out _))
        {
            return OperationResult.Failure(StorageError.Validation("Upyun credential requires operator password."));
        }

        return OperationResult.Success();
    }
}
