using Aliyun.OSS;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.ObjectStorage.AliyunOss;

public sealed class AliyunOssProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public AliyunOssProviderCreator(
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
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("Aliyun OSS endpoint is required."));
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

            var accessKeyId = materialLease.Material.GetRequiredValue("accessKeyId");
            var accessKeySecret = materialLease.Material.GetRequiredValue("accessKeySecret");
            var securityToken = materialLease.Material.TryGetValue("securityToken", out var token) ? token : null;

            var client = string.IsNullOrWhiteSpace(securityToken)
                ? new OssClient(account.Endpoint, accessKeyId, accessKeySecret)
                : new OssClient(account.Endpoint, accessKeyId, accessKeySecret, securityToken);

            return OperationResult<IStorageProvider>.Success(
                new AliyunOssProvider(new AliyunOssSdkClient(client), materialLease, Descriptor.Capabilities));
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
            return OperationResult.Failure(StorageError.Validation("Aliyun OSS credential requires accessKeyId."));
        }

        if (!material.TryGetValue("accessKeySecret", out _))
        {
            return OperationResult.Failure(StorageError.Validation("Aliyun OSS credential requires accessKeySecret."));
        }

        return OperationResult.Success();
    }
}
