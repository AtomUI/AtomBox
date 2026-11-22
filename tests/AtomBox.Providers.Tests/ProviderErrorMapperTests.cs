using Amazon.S3;
using AtomBox.Core.Errors;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.AliyunOss;

namespace AtomBox.Providers.Tests;

public sealed class ProviderErrorMapperTests
{
    [Theory]
    [InlineData(403, "SignatureDoesNotMatch", StorageErrorCategory.Authentication)]
    [InlineData(403, "AccessDenied", StorageErrorCategory.Authorization)]
    [InlineData(404, "NoSuchBucket", StorageErrorCategory.NotFound)]
    [InlineData(400, "RequestTimeout", StorageErrorCategory.Network)]
    [InlineData(400, "RequestTimeTooSkewed", StorageErrorCategory.Validation)]
    [InlineData(503, "SlowDown", StorageErrorCategory.Provider)]
    public void FromException_MapsSharedAwsS3Errors(
        int statusCode,
        string errorCode,
        StorageErrorCategory expectedCategory)
    {
        var exception = new AmazonS3Exception("raw sdk diagnostic")
        {
            StatusCode = (System.Net.HttpStatusCode)statusCode,
            ErrorCode = errorCode
        };

        var error = ProviderErrorMapper.FromException(exception);

        Assert.Equal(expectedCategory, error.Category);
        Assert.Equal(errorCode, error.ProviderErrorCode);
        Assert.DoesNotContain("raw sdk", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Conflict)]
    [InlineData(System.Net.HttpStatusCode.PreconditionFailed)]
    public void FromException_MapsNativeHttpConflictToConflict(System.Net.HttpStatusCode statusCode)
    {
        var error = ProviderErrorMapper.FromException(new HttpRequestException("raw http conflict", null, statusCode));

        Assert.Equal(StorageErrorCategory.Conflict, error.Category);
        Assert.Equal(StorageErrorCode.Conflict, error.Code);
        Assert.Equal(((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture), error.ProviderErrorCode);
        Assert.DoesNotContain("raw http", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromException_MapsNativeHttpBadRequestToValidation()
    {
        var error = ProviderErrorMapper.FromException(
            new HttpRequestException("raw http bad request", null, System.Net.HttpStatusCode.BadRequest));

        Assert.Equal(StorageErrorCategory.Validation, error.Category);
        Assert.Equal(StorageErrorCode.ValidationFailed, error.Code);
        Assert.DoesNotContain("raw http", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromException_MapsNativeOssErrorCodeAndSafeRequestId()
    {
        var error = ProviderErrorMapper.FromException(
            new AliyunOssNativeException(403, "AccessDenied", "request-123"));

        Assert.Equal(StorageErrorCategory.Authorization, error.Category);
        Assert.Equal("AccessDenied", error.ProviderErrorCode);
        Assert.Contains("request-123", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
