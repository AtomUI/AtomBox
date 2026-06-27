using Aliyun.OSS.Common;
using AtomBox.Core.Errors;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.Tests;

public sealed class ProviderErrorMapperTests
{
    [Theory]
    [InlineData("InvalidAccessKeyId")]
    [InlineData("SignatureDoesNotMatch")]
    [InlineData("SecurityTokenExpired")]
    public void FromException_MapsAliyunAuthenticationErrors(string errorCode)
    {
        var error = ProviderErrorMapper.FromException(CreateServiceException(errorCode));

        Assert.Equal(StorageErrorCategory.Authentication, error.Category);
        Assert.Equal(StorageErrorCode.AuthenticationFailed, error.Code);
        Assert.Equal(errorCode, error.ProviderErrorCode);
        Assert.DoesNotContain("raw sdk", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromException_MapsAliyunAccessDeniedToAuthorization()
    {
        var error = ProviderErrorMapper.FromException(CreateServiceException("AccessDenied"));

        Assert.Equal(StorageErrorCategory.Authorization, error.Category);
        Assert.Equal(StorageErrorCode.AuthorizationFailed, error.Code);
        Assert.Equal("AccessDenied", error.ProviderErrorCode);
    }

    [Theory]
    [InlineData("NoSuchBucket")]
    [InlineData("NoSuchKey")]
    public void FromException_MapsAliyunMissingResourcesToNotFound(string errorCode)
    {
        var error = ProviderErrorMapper.FromException(CreateServiceException(errorCode));

        Assert.Equal(StorageErrorCategory.NotFound, error.Category);
        Assert.Equal(StorageErrorCode.NotFound, error.Code);
        Assert.Equal(errorCode, error.ProviderErrorCode);
    }

    [Fact]
    public void FromException_MapsClientExceptionToRetryableProviderFailure()
    {
        var error = ProviderErrorMapper.FromException(new ClientException("raw sdk client failure"));

        Assert.Equal(StorageErrorCategory.Provider, error.Category);
        Assert.Equal(StorageErrorCode.ProviderUnavailable, error.Code);
        Assert.True(error.IsRetryable);
        Assert.DoesNotContain("raw sdk", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Conflict)]
    [InlineData(System.Net.HttpStatusCode.PreconditionFailed)]
    public void FromException_MapsHttpConflictToConflict(System.Net.HttpStatusCode statusCode)
    {
        var error = ProviderErrorMapper.FromException(new HttpRequestException("raw sdk conflict", null, statusCode));

        Assert.Equal(StorageErrorCategory.Conflict, error.Category);
        Assert.Equal(StorageErrorCode.Conflict, error.Code);
        Assert.Equal(((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture), error.ProviderErrorCode);
        Assert.DoesNotContain("raw sdk", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromException_MapsHttpBadRequestToValidation()
    {
        var error = ProviderErrorMapper.FromException(new HttpRequestException("raw sdk bad request", null, System.Net.HttpStatusCode.BadRequest));

        Assert.Equal(StorageErrorCategory.Validation, error.Category);
        Assert.Equal(StorageErrorCode.ValidationFailed, error.Code);
        Assert.DoesNotContain("raw sdk", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("RequestTimeout")]
    [InlineData("ConnectionTimeout")]
    public void FromException_MapsAliyunNetworkErrorsToRetryableNetworkFailure(string errorCode)
    {
        var error = ProviderErrorMapper.FromException(CreateServiceException(errorCode));

        Assert.Equal(StorageErrorCategory.Network, error.Category);
        Assert.Equal(StorageErrorCode.NetworkUnavailable, error.Code);
        Assert.True(error.IsRetryable);
        Assert.Equal(errorCode, error.ProviderErrorCode);
    }

    [Fact]
    public void FromException_MapsAliyunClockSkewToValidationFailure()
    {
        var error = ProviderErrorMapper.FromException(CreateServiceException("RequestTimeTooSkewed"));

        Assert.Equal(StorageErrorCategory.Validation, error.Category);
        Assert.Equal(StorageErrorCode.ValidationFailed, error.Code);
        Assert.False(error.IsRetryable);
        Assert.Equal("RequestTimeTooSkewed", error.ProviderErrorCode);
        Assert.Contains("时间", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ServiceUnavailable")]
    [InlineData("TooManyRequests")]
    [InlineData("Throttling")]
    [InlineData("SlowDown")]
    [InlineData("InternalError")]
    public void FromException_MapsAliyunTemporaryServiceErrorsToRetryableProviderFailure(string errorCode)
    {
        var error = ProviderErrorMapper.FromException(CreateServiceException(errorCode));

        Assert.Equal(StorageErrorCategory.Provider, error.Category);
        Assert.Equal(StorageErrorCode.ProviderUnavailable, error.Code);
        Assert.True(error.IsRetryable);
        Assert.Equal(errorCode, error.ProviderErrorCode);
    }

    private static ServiceException CreateServiceException(string errorCode)
    {
        var exception = new ServiceException("raw sdk service failure");
        typeof(ServiceException)
            .GetProperty(nameof(ServiceException.ErrorCode))!
            .SetValue(exception, errorCode);
        return exception;
    }
}
