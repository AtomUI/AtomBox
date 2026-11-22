using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.HuaweiObs;

public sealed class HuaweiObsProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public HuaweiObsProviderCreator(ProviderDescriptor descriptor, ProviderCredentialResolver credentialResolver)
    {
        Descriptor = descriptor;
        _credentialResolver = credentialResolver;
    }

    public ProviderDescriptor Descriptor { get; }

    public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var endpoint = AwsSdkS3ProviderFactory.NormalizeEndpoint(account.Endpoint, "Huawei OBS");
        if (endpoint.IsFailure)
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(endpoint.Error!));
        }

        var region = string.IsNullOrWhiteSpace(account.Region) ? TryDeriveRegion(endpoint.GetValueOrThrow()) : account.Region.Trim();
        if (string.IsNullOrWhiteSpace(region))
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Failure(
                StorageError.Validation("Huawei OBS region is required and could not be derived from endpoint.")));
        }

        return AwsSdkS3ProviderFactory.CreateAsync(
            account,
            Descriptor,
            _credentialResolver,
            new AwsSdkS3ProviderProfile(
                endpoint.GetValueOrThrow(), region, ForcePathStyle: false, CursorNamespace: "huawei-obs",
                ObjectListingApi: S3ObjectListingApi.V1, SingleCopyLimit: 5L * 1024 * 1024 * 1024),
            cancellationToken: cancellationToken);
    }

    internal static string? TryDeriveRegion(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var parts = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var obsIndex = Array.FindIndex(parts, item => string.Equals(item, "obs", StringComparison.OrdinalIgnoreCase));
        return obsIndex >= 0 && obsIndex + 1 < parts.Length ? parts[obsIndex + 1] : null;
    }
}
