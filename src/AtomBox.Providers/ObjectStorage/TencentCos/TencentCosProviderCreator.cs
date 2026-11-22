using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.TencentCos;

public sealed class TencentCosProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public TencentCosProviderCreator(ProviderDescriptor descriptor, ProviderCredentialResolver credentialResolver)
    {
        Descriptor = descriptor;
        _credentialResolver = credentialResolver;
    }

    public ProviderDescriptor Descriptor { get; }

    public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var endpoint = NormalizeServiceEndpoint(account.Endpoint);
        if (endpoint.IsFailure)
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(endpoint.Error!));
        }

        if (string.IsNullOrWhiteSpace(account.Region))
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(StorageError.Validation("Tencent COS region is required.")));
        }

        return AwsSdkS3ProviderFactory.CreateAsync(
            account,
            Descriptor,
            _credentialResolver,
            new AwsSdkS3ProviderProfile(
                endpoint.GetValueOrThrow(), account.Region, ForcePathStyle: false, CursorNamespace: "tencent-cos",
                SingleCopyLimit: 5L * 1024 * 1024 * 1024, ValidateBucketName: ValidateBucketName),
            cancellationToken: cancellationToken);
    }

    internal static OperationResult<string> NormalizeServiceEndpoint(string? endpoint)
    {
        var normalized = AwsSdkS3ProviderFactory.NormalizeEndpoint(endpoint, "Tencent COS");
        if (!normalized.TryGetValue(out var value))
        {
            return normalized;
        }

        var uri = new Uri(value);
        var host = uri.Host;
        var labels = host.Split('.');
        if (labels is [var bucket, "cos", _, "myqcloud", "com"] && ValidateBucketName(bucket) is null)
        {
            var regionalHost = host[(host.IndexOf('.') + 1)..];
            return OperationResult<string>.Success(new UriBuilder(uri) { Host = regionalHost }.Uri.GetLeftPart(UriPartial.Authority));
        }

        return normalized;
    }

    internal static StorageError? ValidateBucketName(string bucketName)
    {
        var separator = bucketName.LastIndexOf('-');
        if (separator <= 0 || separator == bucketName.Length - 1 ||
            bucketName[0] == '-' || bucketName[separator - 1] == '-' ||
            !bucketName[..separator].All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-') ||
            !bucketName[(separator + 1)..].All(char.IsAsciiDigit))
        {
            return StorageError.Validation("Enter a complete Tencent COS bucket name in BucketName-APPID format, using lowercase letters, digits and hyphens.");
        }

        return null;
    }
}
