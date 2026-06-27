using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;
using Qiniu.Storage;
using Qiniu.Util;

namespace AtomBox.Providers.ObjectStorage.QiniuKodo;

public sealed class QiniuKodoProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public QiniuKodoProviderCreator(
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
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("Qiniu Kodo download domain is required."));
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

            var accessKey = materialLease.Material.GetRequiredValue("accessKeyId");
            var secretKey = materialLease.Material.GetRequiredValue("accessKeySecret");
            var config = new Config
            {
                UseHttps = account.Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                Zone = GetZone(account.GetProviderConfigValue("region"))
            };
            var client = new QiniuKodoSdkClient(
                new Mac(accessKey, secretKey),
                config,
                account.Endpoint,
                accessKey,
                secretKey);

            return OperationResult<IStorageProvider>.Success(
                new QiniuKodoProvider(client, materialLease, Descriptor.Capabilities));
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
            return OperationResult.Failure(StorageError.Validation("Qiniu Kodo credential requires accessKeyId."));
        }

        if (!material.TryGetValue("accessKeySecret", out _))
        {
            return OperationResult.Failure(StorageError.Validation("Qiniu Kodo credential requires accessKeySecret."));
        }

        return OperationResult.Success();
    }

    private static Zone GetZone(string? region)
    {
        return region?.Trim().ToLowerInvariant() switch
        {
            "z0" or "cn-east" or "huadong" or "east-cn" => Zone.ZONE_CN_East,
            "z1" or "cn-north" or "huabei" or "north-cn" => Zone.ZONE_CN_North,
            "z2" or "cn-south" or "huanan" or "south-cn" => Zone.ZONE_CN_South,
            "na0" or "us-north" or "north-america" => Zone.ZONE_US_North,
            "as0" or "singapore" or "ap-southeast" => Zone.ZONE_AS_Singapore,
            "cn-east-2" or "huadong-2" => Zone.ZONE_CN_East_2,
            _ => Zone.ZONE_CN_East
        };
    }
}
