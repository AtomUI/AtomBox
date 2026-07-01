using BaiduBce;
using BaiduBce.Auth;
using BaiduBce.Services.Bos;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.BaiduBos;

public sealed class BaiduBosProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public BaiduBosProviderCreator(ProviderDescriptor descriptor, ProviderCredentialResolver credentialResolver)
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
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("Baidu BOS endpoint is required."));
        }

        var region = account.GetProviderConfigValue("region");
        if (string.IsNullOrWhiteSpace(region))
        {
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("Baidu BOS region is required."));
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
            var configuration = new BceClientConfiguration
            {
                Credentials = new DefaultBceCredentials(accessKeyId, accessKeySecret),
                Endpoint = NormalizeEndpoint(account.Endpoint),
                Protocol = BceConstants.Protocol.Https,
                Region = region.Trim()
            };
            var client = new BaiduBosSdkClient(configuration, account.GetProviderConfigValue("bucket"));
            return OperationResult<IStorageProvider>.Success(
                new S3CompatibleProvider(client, materialLease, Descriptor.Capabilities));
        }
        catch (Exception exception)
        {
            await materialLease.DisposeAsync().ConfigureAwait(false);
            return OperationResult<IStorageProvider>.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private static OperationResult ValidateSecretMaterial(CredentialSecretMaterial material)
    {
        if (!material.TryGetValue("accessKeyId", out _))
        {
            return OperationResult.Failure(StorageError.Validation("Baidu BOS credential requires accessKeyId."));
        }

        if (!material.TryGetValue("accessKeySecret", out _))
        {
            return OperationResult.Failure(StorageError.Validation("Baidu BOS credential requires accessKeySecret."));
        }

        return OperationResult.Success();
    }
}
