using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.NetDisk.AliyunDrive;

public sealed class AliyunDriveProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public AliyunDriveProviderCreator(
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

        var endpoint = string.IsNullOrWhiteSpace(account.Endpoint)
            ? "https://openapi.alipan.com"
            : account.Endpoint;
        var driveId = account.GetProviderConfigValue("driveId");
        if (string.IsNullOrWhiteSpace(driveId))
        {
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("Aliyun Drive driveId is required."));
        }

        var rootFileId = account.GetProviderConfigValue("rootFileId");
        if (string.IsNullOrWhiteSpace(rootFileId))
        {
            rootFileId = "root";
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

            var accessToken = materialLease.Material.GetRequiredValue("token");
            return OperationResult<IStorageProvider>.Success(
                new AliyunDriveProvider(
                    new AliyunDriveHttpClient(endpoint, accessToken),
                    materialLease,
                    Descriptor.Capabilities,
                    driveId,
                    rootFileId));
        }
        catch (Exception exception)
        {
            await materialLease.DisposeAsync().ConfigureAwait(false);
            return OperationResult<IStorageProvider>.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    private static OperationResult ValidateSecretMaterial(CredentialSecretMaterial material)
    {
        if (!material.TryGetValue("token", out _))
        {
            return OperationResult.Failure(StorageError.Validation("Aliyun Drive credential requires token."));
        }

        return OperationResult.Success();
    }
}
