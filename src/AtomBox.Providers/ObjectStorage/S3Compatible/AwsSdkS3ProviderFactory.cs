using Amazon.Runtime;
using Amazon.S3;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.ObjectStorage.S3Compatible;

internal sealed record AwsSdkS3ProviderProfile(
    string Endpoint,
    string Region,
    bool ForcePathStyle,
    string CursorNamespace,
    S3ObjectListingApi ObjectListingApi = S3ObjectListingApi.V2,
    string? RootBucket = null,
    long? SingleCopyLimit = null,
    long MultipartCopyPartSize = 256L * 1024 * 1024,
    Func<string, StorageError?>? ValidateBucketName = null,
    S3CannedACL? CreateBucketAcl = null);

internal static class AwsSdkS3ProviderFactory
{
    public static async Task<OperationResult<IStorageProvider>> CreateAsync(
        StorageAccount account,
        ProviderDescriptor descriptor,
        ProviderCredentialResolver credentialResolver,
        AwsSdkS3ProviderProfile profile,
        Func<IAsyncS3CompatibleClient, CredentialSecretMaterial, IAsyncS3CompatibleClient>? decorateClient = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var serviceEndpoint) ||
            serviceEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation(
                "Migrated object storage providers require an HTTPS S3 endpoint."));
        }

        var materialResult = await credentialResolver.AcquireMaterialAsync(account, cancellationToken).ConfigureAwait(false);
        if (materialResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(materialResult.Error!);
        }

        var materialLease = materialResult.GetValueOrThrow();
        IAsyncS3CompatibleClient? client = null;
        try
        {
            var credentialsResult = TryCreateCredentials(materialLease.Material);
            if (credentialsResult.IsFailure)
            {
                await materialLease.DisposeAsync().ConfigureAwait(false);
                return OperationResult<IStorageProvider>.Failure(credentialsResult.Error!);
            }

            var config = CreateAmazonConfig(profile);
            client = new AwsSdkS3CompatibleClient(
                new AmazonS3Client(credentialsResult.GetValueOrThrow(), config),
                new AwsSdkS3ClientOptions(
                    profile.ObjectListingApi,
                    UseChunkEncoding: false,
                    DisablePayloadSigning: true,
                    profile.SingleCopyLimit,
                    profile.MultipartCopyPartSize,
                    CreateBucketAcl: profile.CreateBucketAcl));
            if (decorateClient is not null)
            {
                client = decorateClient(client, materialLease.Material);
            }

            return OperationResult<IStorageProvider>.Success(new S3CompatibleProvider(
                client,
                materialLease,
                descriptor.Capabilities,
                new S3CompatibleProviderOptions(
                    profile.CursorNamespace,
                    profile.RootBucket,
                    profile.ValidateBucketName)));
        }
        catch (Exception exception)
        {
            client?.Dispose();
            await materialLease.DisposeAsync().ConfigureAwait(false);
            return OperationResult<IStorageProvider>.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    public static OperationResult<AWSCredentials> TryCreateCredentials(CredentialSecretMaterial material)
    {
        if (!material.TryGetValue("accessKeyId", out var accessKeyId))
        {
            return OperationResult<AWSCredentials>.Failure(StorageError.Validation(
                "Object storage credential requires accessKeyId."));
        }

        if (!material.TryGetValue("accessKeySecret", out var accessKeySecret))
        {
            return OperationResult<AWSCredentials>.Failure(StorageError.Validation(
                "Object storage credential requires accessKeySecret."));
        }

        var hasToken = material.TryGetValue("securityToken", out var securityToken) ||
            material.TryGetValue("sessionToken", out securityToken);
        return hasToken
            ? OperationResult<AWSCredentials>.Success(new SessionAWSCredentials(accessKeyId, accessKeySecret, securityToken))
            : OperationResult<AWSCredentials>.Success(new BasicAWSCredentials(accessKeyId, accessKeySecret));
    }

    public static OperationResult<string> NormalizeEndpoint(string? endpoint, string providerName)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return OperationResult<string>.Failure(StorageError.Validation($"{providerName} endpoint is required."));
        }

        var candidate = endpoint.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = $"https://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/") ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return OperationResult<string>.Failure(StorageError.Validation(
                $"{providerName} endpoint must be an HTTP or HTTPS service root without path, query, fragment, or user information."));
        }

        return OperationResult<string>.Success(uri.GetLeftPart(UriPartial.Authority));
    }

    internal static AmazonS3Config CreateAmazonConfig(AwsSdkS3ProviderProfile profile)
    {
        return new AmazonS3Config
        {
            ServiceURL = profile.Endpoint,
            AuthenticationRegion = profile.Region,
            ForcePathStyle = profile.ForcePathStyle,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };
    }
}
