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

    public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var endpoint = TryCreateS3Endpoint(account.Endpoint);
        if (endpoint.IsFailure)
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(endpoint.Error!));
        }

        if (string.IsNullOrWhiteSpace(account.Region))
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(StorageError.Validation("Baidu BOS region is required.")));
        }

        return AwsSdkS3ProviderFactory.CreateAsync(
            account,
            Descriptor,
            _credentialResolver,
            new AwsSdkS3ProviderProfile(
                endpoint.GetValueOrThrow(), account.Region, ForcePathStyle: false, CursorNamespace: "baidu-bos",
                RootBucket: account.GetProviderConfigValue("bucket"), SingleCopyLimit: 5L * 1024 * 1024 * 1024),
            cancellationToken: cancellationToken);
    }

    internal static OperationResult<string> TryCreateS3Endpoint(string? endpoint)
    {
        var normalized = AwsSdkS3ProviderFactory.NormalizeEndpoint(endpoint, "Baidu BOS");
        if (normalized.IsFailure)
        {
            return normalized;
        }

        var uri = new Uri(normalized.GetValueOrThrow());
        var host = uri.Host.StartsWith("s3.", StringComparison.OrdinalIgnoreCase) ? uri.Host : $"s3.{uri.Host}";
        return OperationResult<string>.Success(new UriBuilder(uri) { Host = host }.Uri.GetLeftPart(UriPartial.Authority));
    }
}
