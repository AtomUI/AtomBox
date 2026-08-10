using Amazon.Runtime;
using Amazon.S3;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.GenericS3;

public sealed class GenericS3ProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public GenericS3ProviderCreator(ProviderDescriptor descriptor, ProviderCredentialResolver credentialResolver)
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

        var optionsResult = TryCreateOptions(account);
        if (optionsResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(optionsResult.Error!);
        }

        var materialResult = await _credentialResolver.AcquireMaterialAsync(account, cancellationToken).ConfigureAwait(false);
        if (materialResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(materialResult.Error!);
        }

        var materialLease = materialResult.GetValueOrThrow();
        try
        {
            var credentialResult = TryCreateCredentials(materialLease.Material);
            if (credentialResult.IsFailure)
            {
                await materialLease.DisposeAsync().ConfigureAwait(false);
                return OperationResult<IStorageProvider>.Failure(credentialResult.Error!);
            }

            var options = optionsResult.GetValueOrThrow();
            var client = new AmazonS3Client(credentialResult.GetValueOrThrow(), CreateAmazonConfig(options));
            return OperationResult<IStorageProvider>.Success(new S3CompatibleProvider(
                new AwsSdkS3CompatibleClient(client),
                materialLease,
                Descriptor.Capabilities));
        }
        catch (Exception exception)
        {
            await materialLease.DisposeAsync().ConfigureAwait(false);
            return OperationResult<IStorageProvider>.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    internal static OperationResult<GenericS3Options> TryCreateOptions(StorageAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.Endpoint) ||
            !Uri.TryCreate(account.Endpoint.Trim(), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            (!string.IsNullOrEmpty(endpoint.AbsolutePath) && endpoint.AbsolutePath != "/") ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            return OperationResult<GenericS3Options>.Failure(StorageError.Validation(
                "S3 endpoint must be an absolute HTTP or HTTPS service root URL without a path, query, fragment, or user information."));
        }

        if (string.IsNullOrWhiteSpace(account.Region))
        {
            return OperationResult<GenericS3Options>.Failure(StorageError.Validation(
                "S3 compatible provider requires region."));
        }

        var region = account.Region.Trim();
        var normalizedEndpoint = endpoint.GetLeftPart(UriPartial.Authority);
        return OperationResult<GenericS3Options>.Success(new GenericS3Options(
            normalizedEndpoint,
            region));
    }

    internal static OperationResult<AWSCredentials> TryCreateCredentials(CredentialSecretMaterial material)
    {
        if (!material.TryGetValue("accessKeyId", out var accessKeyId))
        {
            return OperationResult<AWSCredentials>.Failure(StorageError.Validation(
                "S3 compatible credential requires accessKeyId."));
        }

        if (!material.TryGetValue("accessKeySecret", out var accessKeySecret))
        {
            return OperationResult<AWSCredentials>.Failure(StorageError.Validation(
                "S3 compatible credential requires accessKeySecret."));
        }

        return material.TryGetValue("sessionToken", out var sessionToken)
            ? OperationResult<AWSCredentials>.Success(new SessionAWSCredentials(accessKeyId, accessKeySecret, sessionToken))
            : OperationResult<AWSCredentials>.Success(new BasicAWSCredentials(accessKeyId, accessKeySecret));
    }

    internal static AmazonS3Config CreateAmazonConfig(GenericS3Options options)
    {
        return new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            AuthenticationRegion = options.Region,
            ForcePathStyle = true
        };
    }
}

internal sealed record GenericS3Options(
    string Endpoint,
    string Region);
