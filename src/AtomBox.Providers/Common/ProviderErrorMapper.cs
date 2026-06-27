using AtomBox.Core.Errors;
using Aliyun.OSS.Common;
using COSXML.CosException;
using FluentFTP.Exceptions;
using AtomBox.Providers.NetDisk.AliyunDrive;
using AtomBox.Providers.NetDisk.BaiduNetDisk;
using AtomBox.Providers.FileTransfer.WebDav;
using AtomBox.Providers.ObjectStorage.QiniuKodo;
using Renci.SshNet.Common;
using TOS.Error;
using ObsServiceException = OBS.ServiceException;
using AliyunServiceException = Aliyun.OSS.Common.ServiceException;

namespace AtomBox.Providers.Common;

public static class ProviderErrorMapper
{
    public static StorageError FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            AliyunServiceException serviceException => FromServiceException(serviceException),
            SshAuthenticationException => new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication),
            SftpPermissionDeniedException => new StorageError(
                StorageErrorCode.AuthorizationFailed,
                "Provider authorization failed.",
                StorageErrorCategory.Authorization),
            SftpPathNotFoundException => new StorageError(
                StorageErrorCode.NotFound,
                "Remote resource was not found.",
                StorageErrorCategory.NotFound),
            SshConnectionException => new StorageError(
                StorageErrorCode.NetworkUnavailable,
                "Provider network connection failed.",
                StorageErrorCategory.Network,
                isRetryable: true),
            SshOperationTimeoutException => new StorageError(
                StorageErrorCode.NetworkUnavailable,
                "Provider operation timed out.",
                StorageErrorCategory.Network,
                isRetryable: true),
            FtpAuthenticationException => new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication),
            FtpCommandException commandException => FromFtpCommandException(commandException),
            FtpException => new StorageError(
                StorageErrorCode.ProviderUnavailable,
                "Provider FTP operation failed.",
                StorageErrorCategory.Provider,
                isRetryable: true),
            CosServerException serverException => FromCosServerException(serverException),
            CosClientException => new StorageError(
                StorageErrorCode.ProviderUnavailable,
                "Provider COS client failed before the remote service completed the request.",
                StorageErrorCategory.Provider,
                isRetryable: true),
            TosServerException serverException => FromTosServerException(serverException),
            TosClientException => new StorageError(
                StorageErrorCode.ProviderUnavailable,
                "Provider TOS client failed before the remote service completed the request.",
                StorageErrorCategory.Provider,
                isRetryable: true),
            ObsServiceException serviceException => FromHuaweiObsServiceException(serviceException),
            QiniuKodoHttpException httpException => FromQiniuHttpException(httpException),
            AliyunDriveApiException driveException => FromAliyunDriveApiException(driveException),
            BaiduNetDiskApiException baiduException => FromBaiduNetDiskApiException(baiduException),
            WebDavHttpException webDavException => FromWebDavHttpException(webDavException),
            HttpRequestException httpException when httpException.StatusCode is not null => FromHttpRequestException(httpException),
            HttpRequestException => new StorageError(
                StorageErrorCode.NetworkUnavailable,
                "Provider HTTP network request failed.",
                StorageErrorCategory.Network,
                isRetryable: true),
            ClientException => new StorageError(
                StorageErrorCode.ProviderUnavailable,
                "Provider client failed before the remote service completed the request.",
                StorageErrorCategory.Provider,
                isRetryable: true),
            OperationCanceledException => new StorageError(
                StorageErrorCode.OperationCanceled,
                "Provider operation was canceled.",
                StorageErrorCategory.Canceled,
                isRetryable: true),
            TimeoutException => new StorageError(
                StorageErrorCode.NetworkUnavailable,
                "Provider operation timed out.",
                StorageErrorCategory.Network,
                isRetryable: true),
            ArgumentException => StorageError.Validation("Provider configuration is invalid."),
            InvalidOperationException => StorageError.Unknown("Provider operation failed."),
            _ => StorageError.Unknown($"Unexpected provider error: {exception.GetType().Name}: {exception.Message}")
        };
    }

    private static StorageError FromServiceException(AliyunServiceException exception)
    {
        var errorCode = exception.ErrorCode;
        if (IsClockSkewError(errorCode))
        {
            return ClockSkewError(errorCode);
        }

        if (IsAuthenticationError(errorCode))
        {
            return new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication,
                providerErrorCode: errorCode);
        }

        if (IsAuthorizationError(errorCode))
        {
            return new StorageError(
                StorageErrorCode.AuthorizationFailed,
                "Provider authorization failed.",
                StorageErrorCategory.Authorization,
                providerErrorCode: errorCode);
        }

        if (IsNotFoundError(errorCode))
        {
            return new StorageError(
                StorageErrorCode.NotFound,
                "Remote resource was not found.",
                StorageErrorCategory.NotFound,
                providerErrorCode: errorCode);
        }

        if (IsNetworkError(errorCode))
        {
            return new StorageError(
                StorageErrorCode.NetworkUnavailable,
                "Provider network request failed.",
                StorageErrorCategory.Network,
                isRetryable: true,
                providerErrorCode: errorCode);
        }

        if (IsRetryableServiceError(errorCode))
        {
            return new StorageError(
                StorageErrorCode.ProviderUnavailable,
                "Provider service is temporarily unavailable.",
                StorageErrorCategory.Provider,
                isRetryable: true,
                providerErrorCode: errorCode);
        }

        return new StorageError(
            StorageErrorCode.ProviderUnavailable,
            "Provider service request failed.",
            StorageErrorCategory.Provider,
            isRetryable: true,
            providerErrorCode: errorCode);
    }

    private static StorageError FromFtpCommandException(FtpCommandException exception)
    {
        var completionCode = exception.CompletionCode;
        if (completionCode.StartsWith("530", StringComparison.Ordinal))
        {
            return new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication,
                providerErrorCode: completionCode);
        }

        if (completionCode.StartsWith("550", StringComparison.Ordinal) ||
            completionCode.StartsWith("553", StringComparison.Ordinal))
        {
            return new StorageError(
                StorageErrorCode.AuthorizationFailed,
                "Provider authorization failed.",
                StorageErrorCategory.Authorization,
                providerErrorCode: completionCode);
        }

        if (completionCode.StartsWith("450", StringComparison.Ordinal) ||
            completionCode.StartsWith("421", StringComparison.Ordinal))
        {
            return new StorageError(
                StorageErrorCode.ProviderUnavailable,
                "Provider FTP service is temporarily unavailable.",
                StorageErrorCategory.Provider,
                isRetryable: true,
                providerErrorCode: completionCode);
        }

        return new StorageError(
            StorageErrorCode.ProviderUnavailable,
            "Provider FTP command failed.",
            StorageErrorCategory.Provider,
            providerErrorCode: completionCode);
    }

    private static StorageError FromCosServerException(CosServerException exception)
    {
        var errorCode = string.IsNullOrWhiteSpace(exception.errorCode)
            ? exception.statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : exception.errorCode;

        if (IsClockSkewError(errorCode))
        {
            return ClockSkewError(errorCode);
        }

        if (exception.statusCode is 401 or 403 ||
            string.Equals(errorCode, "AccessDenied", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(errorCode, "SignatureDoesNotMatch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(errorCode, "InvalidSecretId", StringComparison.OrdinalIgnoreCase))
        {
            return new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication,
                providerErrorCode: errorCode);
        }

        if (exception.statusCode == 404 ||
            string.Equals(errorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(errorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase))
        {
            return new StorageError(
                StorageErrorCode.NotFound,
                "Remote resource was not found.",
                StorageErrorCategory.NotFound,
                providerErrorCode: errorCode);
        }

        return new StorageError(
            StorageErrorCode.ProviderUnavailable,
            "Provider COS service request failed.",
            StorageErrorCategory.Provider,
            isRetryable: exception.statusCode is 408 or 429 or >= 500,
            providerErrorCode: errorCode);
    }

    private static StorageError FromHuaweiObsServiceException(ObsServiceException exception)
    {
        var statusCode = (int)exception.StatusCode;
        var errorCode = string.IsNullOrWhiteSpace(exception.ErrorCode)
            ? statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : exception.ErrorCode;

        if (IsClockSkewError(errorCode))
        {
            return ClockSkewError(errorCode);
        }

        if (statusCode is 401 or 403 ||
            string.Equals(errorCode, "AccessDenied", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(errorCode, "SignatureDoesNotMatch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(errorCode, "InvalidAccessKeyId", StringComparison.OrdinalIgnoreCase))
        {
            return new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication,
                providerErrorCode: errorCode);
        }

        if (statusCode == 404 ||
            string.Equals(errorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(errorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase))
        {
            return new StorageError(
                StorageErrorCode.NotFound,
                "Remote resource was not found.",
                StorageErrorCategory.NotFound,
                providerErrorCode: errorCode);
        }

        return new StorageError(
            StorageErrorCode.ProviderUnavailable,
            "Provider Huawei OBS service request failed.",
            StorageErrorCategory.Provider,
            isRetryable: statusCode is 408 or 429 or >= 500,
            providerErrorCode: errorCode);
    }

    private static StorageError FromTosServerException(TosServerException exception)
    {
        var errorCode = string.IsNullOrWhiteSpace(exception.Code)
            ? exception.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : exception.Code;

        if (exception.StatusCode is 401 or 403 ||
            string.Equals(errorCode, "AccessDenied", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(errorCode, "SignatureDoesNotMatch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(errorCode, "InvalidAccessKeyId", StringComparison.OrdinalIgnoreCase))
        {
            return new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication,
                providerErrorCode: errorCode);
        }

        if (exception.StatusCode == 404 ||
            string.Equals(errorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(errorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase))
        {
            return new StorageError(
                StorageErrorCode.NotFound,
                "Remote resource was not found.",
                StorageErrorCategory.NotFound,
                providerErrorCode: errorCode);
        }

        return new StorageError(
            StorageErrorCode.ProviderUnavailable,
            "Provider TOS service request failed.",
            StorageErrorCategory.Provider,
            isRetryable: exception.StatusCode is 408 or 429 or >= 500,
            providerErrorCode: errorCode);
    }

    private static StorageError FromQiniuHttpException(QiniuKodoHttpException exception)
    {
        var providerErrorCode = exception.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return FromQiniuStatusCode(exception.StatusCode, providerErrorCode);
    }

    private static StorageError FromQiniuStatusCode(int statusCode, string providerErrorCode)
    {
        if (statusCode is 401 or 631)
        {
            return new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication,
                providerErrorCode: providerErrorCode);
        }

        if (statusCode == 403)
        {
            return new StorageError(
                StorageErrorCode.AuthorizationFailed,
                "Provider authorization failed.",
                StorageErrorCategory.Authorization,
                providerErrorCode: providerErrorCode);
        }

        if (statusCode is 404 or 612 or 614)
        {
            return new StorageError(
                StorageErrorCode.NotFound,
                "Remote resource was not found.",
                StorageErrorCategory.NotFound,
                providerErrorCode: providerErrorCode);
        }

        return new StorageError(
            StorageErrorCode.ProviderUnavailable,
            "Provider Qiniu Kodo request failed.",
            StorageErrorCategory.Provider,
            isRetryable: statusCode is 429 or >= 500,
            providerErrorCode: providerErrorCode);
    }

    private static StorageError FromAliyunDriveApiException(AliyunDriveApiException exception)
    {
        var providerErrorCode = string.IsNullOrWhiteSpace(exception.ProviderErrorCode)
            ? exception.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : exception.ProviderErrorCode;

        if (exception.StatusCode is 401 or 403 ||
            string.Equals(providerErrorCode, "AccessTokenInvalid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerErrorCode, "AccessTokenExpired", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerErrorCode, "InvalidAccessToken", StringComparison.OrdinalIgnoreCase))
        {
            return new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication,
                providerErrorCode: providerErrorCode);
        }

        if (exception.StatusCode == 404 ||
            string.Equals(providerErrorCode, "FileNotFound", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerErrorCode, "NotFound.File", StringComparison.OrdinalIgnoreCase))
        {
            return new StorageError(
                StorageErrorCode.NotFound,
                "Remote resource was not found.",
                StorageErrorCategory.NotFound,
                providerErrorCode: providerErrorCode);
        }

        return new StorageError(
            StorageErrorCode.ProviderUnavailable,
            "Provider Aliyun Drive request failed.",
            StorageErrorCategory.Provider,
            isRetryable: exception.StatusCode is 408 or 429 or >= 500,
            providerErrorCode: providerErrorCode);
    }

    private static StorageError FromBaiduNetDiskApiException(BaiduNetDiskApiException exception)
    {
        var providerErrorCode = exception.Errno.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (exception.Errno is 110 or 111 or 21314 or 21315 or 21316 or 21317)
        {
            return new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication,
                providerErrorCode: providerErrorCode);
        }

        if (exception.Errno is 31023 or 31024 or 31034 or 42211)
        {
            return new StorageError(
                StorageErrorCode.NotFound,
                "Remote resource was not found.",
                StorageErrorCategory.NotFound,
                providerErrorCode: providerErrorCode);
        }

        if (exception.Errno is 31061 or 31218 or 31219)
        {
            return new StorageError(
                StorageErrorCode.AuthorizationFailed,
                "Provider authorization failed.",
                StorageErrorCategory.Authorization,
                providerErrorCode: providerErrorCode);
        }

        return new StorageError(
            StorageErrorCode.ProviderUnavailable,
            "Provider Baidu Netdisk request failed.",
            StorageErrorCategory.Provider,
            isRetryable: exception.Errno is -1 or 2 or 31045 or 31064,
            providerErrorCode: providerErrorCode);
    }

    private static StorageError FromWebDavHttpException(WebDavHttpException exception)
    {
        var providerErrorCode = exception.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return exception.StatusCode switch
        {
            401 => new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication,
                providerErrorCode: providerErrorCode),
            403 or 423 => new StorageError(
                StorageErrorCode.AuthorizationFailed,
                "Provider authorization failed.",
                StorageErrorCategory.Authorization,
                providerErrorCode: providerErrorCode),
            404 => new StorageError(
                StorageErrorCode.NotFound,
                "Remote resource was not found.",
                StorageErrorCategory.NotFound,
                providerErrorCode: providerErrorCode),
            409 or 412 => new StorageError(
                StorageErrorCode.Conflict,
                "Remote resource conflict.",
                StorageErrorCategory.Conflict,
                providerErrorCode: providerErrorCode),
            408 or 429 or >= 500 => new StorageError(
                StorageErrorCode.ProviderUnavailable,
                "Provider WebDAV service is temporarily unavailable.",
                StorageErrorCategory.Provider,
                isRetryable: true,
                providerErrorCode: providerErrorCode),
            _ => new StorageError(
                StorageErrorCode.ProviderUnavailable,
                "Provider WebDAV request failed.",
                StorageErrorCategory.Provider,
                providerErrorCode: providerErrorCode)
        };
    }

    private static StorageError FromHttpRequestException(HttpRequestException exception)
    {
        var statusCode = (int)exception.StatusCode!.Value;
        var providerErrorCode = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return statusCode switch
        {
            401 => new StorageError(
                StorageErrorCode.AuthenticationFailed,
                "Provider authentication failed.",
                StorageErrorCategory.Authentication,
                providerErrorCode: providerErrorCode),
            403 => new StorageError(
                StorageErrorCode.AuthorizationFailed,
                IsSafeProviderHttpMessage(exception.Message) ? exception.Message : "Provider authorization failed.",
                StorageErrorCategory.Authorization,
                providerErrorCode: providerErrorCode),
            404 => new StorageError(
                StorageErrorCode.NotFound,
                "Remote resource was not found.",
                StorageErrorCategory.NotFound,
                providerErrorCode: providerErrorCode),
            409 or 412 => new StorageError(
                StorageErrorCode.Conflict,
                "Remote resource conflict.",
                StorageErrorCategory.Conflict,
                providerErrorCode: providerErrorCode),
            400 => StorageError.Validation(
                IsSafeProviderHttpMessage(exception.Message) ? exception.Message : "Provider request is invalid."),
            408 or 429 or >= 500 => new StorageError(
                StorageErrorCode.ProviderUnavailable,
                "Provider HTTP service is temporarily unavailable.",
                StorageErrorCategory.Provider,
                isRetryable: true,
                providerErrorCode: providerErrorCode),
            _ => new StorageError(
                StorageErrorCode.ProviderUnavailable,
                "Provider HTTP request failed.",
                StorageErrorCategory.Provider,
                providerErrorCode: providerErrorCode)
        };
    }

    private static bool IsAuthenticationError(string? errorCode)
    {
        return string.Equals(errorCode, "InvalidAccessKeyId", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "SignatureDoesNotMatch", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "SecurityTokenExpired", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthorizationError(string? errorCode)
    {
        return string.Equals(errorCode, "AccessDenied", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNotFoundError(string? errorCode)
    {
        return string.Equals(errorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNetworkError(string? errorCode)
    {
        return string.Equals(errorCode, "RequestTimeout", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "ConnectionTimeout", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClockSkewError(string? errorCode)
    {
        return string.Equals(errorCode, "RequestTimeTooSkewed", StringComparison.OrdinalIgnoreCase);
    }

    private static StorageError ClockSkewError(string? providerErrorCode)
    {
        return new StorageError(
            StorageErrorCode.ValidationFailed,
            "本机系统时间与云服务时间偏差过大，请同步 Windows 日期和时间后重试。",
            StorageErrorCategory.Validation,
            providerErrorCode: providerErrorCode);
    }

    private static bool IsRetryableServiceError(string? errorCode)
    {
        return string.Equals(errorCode, "ServiceUnavailable", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "TooManyRequests", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "Throttling", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "SlowDown", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(errorCode, "InternalError", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeProviderHttpMessage(string? message)
    {
        return !string.IsNullOrWhiteSpace(message) &&
               message.StartsWith("Upyun ", StringComparison.Ordinal);
    }
}
