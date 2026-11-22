using Amazon.S3;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.QiniuKodo;

public sealed class QiniuKodoProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public QiniuKodoProviderCreator(ProviderDescriptor descriptor, ProviderCredentialResolver credentialResolver)
    {
        Descriptor = descriptor;
        _credentialResolver = credentialResolver;
    }

    public ProviderDescriptor Descriptor { get; }

    public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var region = NormalizeRegion(account.Region);
        if (region is null)
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(StorageError.Validation("Qiniu Kodo region is required.")));
        }

        return AwsSdkS3ProviderFactory.CreateAsync(
            account,
            Descriptor,
            _credentialResolver,
            new AwsSdkS3ProviderProfile(
                $"https://s3-{region}.qiniucs.com", region, ForcePathStyle: true, CursorNamespace: "qiniu-kodo",
                RootBucket: account.GetProviderConfigValue("bucket"), CreateBucketAcl: S3CannedACL.Private),
            cancellationToken: cancellationToken);
    }

    internal static string? NormalizeRegion(string? region)
    {
        return region?.Trim().ToLowerInvariant() switch
        {
            "z0" or "cn-east" or "huadong" or "east-cn" or "cn-east-1" => "cn-east-1",
            "z1" or "cn-north" or "huabei" or "north-cn" or "cn-north-1" => "cn-north-1",
            "z2" or "cn-south" or "huanan" or "south-cn" or "cn-south-1" => "cn-south-1",
            "na0" or "us-north" or "north-america" or "us-north-1" => "us-north-1",
            "as0" or "singapore" or "ap-southeast" or "ap-southeast-1" => "ap-southeast-1",
            "cn-east-2" or "huadong-2" => "cn-east-2",
            null or "" => null,
            var value => value
        };
    }
}
