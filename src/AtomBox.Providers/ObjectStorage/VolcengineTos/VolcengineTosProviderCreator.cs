using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.VolcengineTos;

public sealed class VolcengineTosProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public VolcengineTosProviderCreator(ProviderDescriptor descriptor, ProviderCredentialResolver credentialResolver)
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
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(StorageError.Validation("Volcengine TOS region is required.")));
        }

        return AwsSdkS3ProviderFactory.CreateAsync(
            account,
            Descriptor,
            _credentialResolver,
            new AwsSdkS3ProviderProfile(
                endpoint.GetValueOrThrow(), account.Region, ForcePathStyle: false, CursorNamespace: "volcengine-tos",
                SingleCopyLimit: 5L * 1024 * 1024 * 1024),
            cancellationToken: cancellationToken);
    }

    internal static OperationResult<string> TryCreateS3Endpoint(string? endpoint)
    {
        var normalized = AwsSdkS3ProviderFactory.NormalizeEndpoint(endpoint, "Volcengine TOS");
        if (normalized.IsFailure)
        {
            return normalized;
        }

        var uri = new Uri(normalized.GetValueOrThrow());
        var host = uri.Host.StartsWith("tos-s3-", StringComparison.OrdinalIgnoreCase)
            ? uri.Host
            : uri.Host.StartsWith("tos-", StringComparison.OrdinalIgnoreCase) ? $"tos-s3-{uri.Host[4..]}" : uri.Host;
        return OperationResult<string>.Success(new UriBuilder(uri) { Host = host }.Uri.GetLeftPart(UriPartial.Authority));
    }
}
