using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;
using COSXML;
using COSXML.Auth;

namespace AtomBox.Providers.ObjectStorage.TencentCos;

public sealed class TencentCosProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public TencentCosProviderCreator(
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
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("Tencent COS endpoint is required."));
        }

        var region = account.GetProviderConfigValue("region");
        if (string.IsNullOrWhiteSpace(region))
        {
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("Tencent COS region is required."));
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

            var secretId = materialLease.Material.GetRequiredValue("accessKeyId");
            var secretKey = materialLease.Material.GetRequiredValue("accessKeySecret");
            var configBuilder = new CosXmlConfig.Builder()
                .SetRegion(region);
            if (!IsStandardTencentCosEndpoint(account.Endpoint, region))
            {
                configBuilder.SetHost(account.Endpoint);
            }

            var config = configBuilder.Build();
            var credentialProvider = new DefaultQCloudCredentialProvider(secretId, secretKey, 600);
            var client = new CosXmlServer(config, credentialProvider);

            return OperationResult<IStorageProvider>.Success(
                new TencentCosProvider(new TencentCosSdkClient(client, region), materialLease, Descriptor.Capabilities));
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
            return OperationResult.Failure(StorageError.Validation("Tencent COS credential requires accessKeyId."));
        }

        if (!material.TryGetValue("accessKeySecret", out _))
        {
            return OperationResult.Failure(StorageError.Validation("Tencent COS credential requires accessKeySecret."));
        }

        return OperationResult.Success();
    }

    private static bool IsStandardTencentCosEndpoint(string endpoint, string region)
    {
        var normalizedEndpoint = endpoint.Trim()
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        var normalizedRegion = region.Trim();

        return string.Equals(
            normalizedEndpoint,
            $"cos.{normalizedRegion}.myqcloud.com",
            StringComparison.OrdinalIgnoreCase);
    }
}
