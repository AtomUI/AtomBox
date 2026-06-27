using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.NetDisk.BaiduNetDisk;

public sealed class BaiduNetDiskProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public BaiduNetDiskProviderCreator(
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

        var apiEndpoint = string.IsNullOrWhiteSpace(account.Endpoint)
            ? "https://pan.baidu.com"
            : account.Endpoint;
        var contentEndpoint = account.GetProviderConfigValue("contentEndpoint");
        if (string.IsNullOrWhiteSpace(contentEndpoint))
        {
            contentEndpoint = "https://d.pcs.baidu.com";
        }

        var rootPath = account.GetProviderConfigValue("rootPath");
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = "/";
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
                new BaiduNetDiskProvider(
                    new BaiduNetDiskHttpClient(apiEndpoint, contentEndpoint, accessToken),
                    materialLease,
                    Descriptor.Capabilities,
                    rootPath));
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
            return OperationResult.Failure(StorageError.Validation("Baidu Netdisk credential requires token."));
        }

        return OperationResult.Success();
    }
}
