using Amazon.Runtime;
using Amazon.S3;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.GenericS3;

namespace AtomBox.Providers.Tests;

public sealed class GenericS3ProviderCreatorTests
{
    [Theory]
    [InlineData(403, "SignatureDoesNotMatch", StorageErrorCategory.Authentication)]
    [InlineData(403, "AccessDenied", StorageErrorCategory.Authorization)]
    [InlineData(404, "NoSuchKey", StorageErrorCategory.NotFound)]
    [InlineData(503, "SlowDown", StorageErrorCategory.Provider)]
    public void ErrorMapper_MapsAmazonS3Errors(
        int statusCode,
        string errorCode,
        StorageErrorCategory expectedCategory)
    {
        var exception = new AmazonS3Exception("SDK diagnostic message")
        {
            StatusCode = (System.Net.HttpStatusCode)statusCode,
            ErrorCode = errorCode
        };

        var error = ProviderErrorMapper.FromException(exception);

        Assert.Equal(expectedCategory, error.Category);
        Assert.Equal(errorCode, error.ProviderErrorCode);
        Assert.DoesNotContain("SDK diagnostic message", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_RejectMissingRegion()
    {
        var result = GenericS3ProviderCreator.TryCreateOptions(CreateAccount("http://127.0.0.1:9000"));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
        Assert.Contains("region", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Options_UseConfiguredRegionAndPathStyle()
    {
        var result = GenericS3ProviderCreator.TryCreateOptions(CreateAccount(
            "https://s3.example.com",
            region: "auto"));

        var options = result.GetValueOrThrow();
        var config = GenericS3ProviderCreator.CreateAmazonConfig(options);
        Assert.Equal("auto", config.AuthenticationRegion);
        Assert.True(config.ForcePathStyle);
    }

    [Theory]
    [InlineData("minio.local:9000")]
    [InlineData("ftp://minio.local")]
    [InlineData("http://minio.local:9000/api")]
    [InlineData("http://minio.local:9000/?x=1")]
    public void Options_RejectInvalidEndpoint(string endpoint)
    {
        var result = GenericS3ProviderCreator.TryCreateOptions(CreateAccount(endpoint));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public void Credentials_UseBasicCredentialsWithoutSessionToken()
    {
        var result = GenericS3ProviderCreator.TryCreateCredentials(new CredentialSecretMaterial(
            new Dictionary<string, string>
            {
                ["accessKeyId"] = "access",
                ["accessKeySecret"] = "secret"
            }));

        Assert.IsType<BasicAWSCredentials>(result.GetValueOrThrow());
    }

    [Fact]
    public void Credentials_UseSessionCredentialsWithSessionToken()
    {
        var result = GenericS3ProviderCreator.TryCreateCredentials(new CredentialSecretMaterial(
            new Dictionary<string, string>
            {
                ["accessKeyId"] = "access",
                ["accessKeySecret"] = "secret",
                ["sessionToken"] = "token"
            }));

        Assert.IsType<SessionAWSCredentials>(result.GetValueOrThrow());
    }

    private static StorageAccount CreateAccount(
        string endpoint,
        string? region = null,
        IReadOnlyDictionary<string, string>? providerConfig = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderRegistry.S3CompatibleProviderId,
            "Generic S3 Test",
            endpoint,
            region,
            new CredentialRef("cred-1"),
            now,
            now,
            providerConfig);
    }
}
