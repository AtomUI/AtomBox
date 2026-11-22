using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.AliyunOss;

public sealed class AliyunOssProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public AliyunOssProviderCreator(ProviderDescriptor descriptor, ProviderCredentialResolver credentialResolver)
    {
        Descriptor = descriptor;
        _credentialResolver = credentialResolver;
    }

    public ProviderDescriptor Descriptor { get; }

    public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var endpointProfile = AliyunOssEndpointProfile.TryCreate(account.Endpoint, account.Region);
        if (endpointProfile.IsFailure)
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(endpointProfile.Error!));
        }

        var profile = endpointProfile.GetValueOrThrow();
        return AwsSdkS3ProviderFactory.CreateAsync(
            account,
            Descriptor,
            _credentialResolver,
            new AwsSdkS3ProviderProfile(
                profile.S3Endpoint,
                profile.Region,
                ForcePathStyle: false,
                CursorNamespace: "aliyun-oss",
                SingleCopyLimit: 5L * 1024 * 1024 * 1024),
            (s3Client, material) => new AliyunOssS3Adapter(
                s3Client,
                new AliyunOssNativeServiceClient(
                    profile.NativeEndpoint,
                    profile.Region,
                    material.GetRequiredValue("accessKeyId"),
                    material.GetRequiredValue("accessKeySecret"),
                    (material.TryGetValue("securityToken", out var token) ||
                     material.TryGetValue("sessionToken", out token))
                        ? token
                        : null)),
            cancellationToken);
    }
}
