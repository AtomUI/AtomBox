using System.Text.RegularExpressions;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.AliyunOss;

internal sealed record AliyunOssEndpointProfile(
    string Region,
    bool IsInternal,
    string S3Endpoint,
    string NativeEndpoint)
{
    private static readonly Regex StandardHostPattern = new(
        @"^(?:s3\.)?oss-(?<region>[a-z0-9-]+?)(?<internal>-internal)?\.aliyuncs\.com$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static OperationResult<AliyunOssEndpointProfile> TryCreate(string? endpoint, string? configuredRegion)
    {
        var normalized = AwsSdkS3ProviderFactory.NormalizeEndpoint(endpoint, "Aliyun OSS");
        if (normalized.IsFailure)
        {
            return OperationResult<AliyunOssEndpointProfile>.Failure(normalized.Error!);
        }

        var uri = new Uri(normalized.GetValueOrThrow());
        if (!uri.IsDefaultPort)
        {
            return OperationResult<AliyunOssEndpointProfile>.Failure(
                StorageError.Validation("Aliyun OSS standard endpoint cannot contain a custom port."));
        }

        var match = StandardHostPattern.Match(uri.Host);
        if (!match.Success)
        {
            return OperationResult<AliyunOssEndpointProfile>.Failure(StorageError.Validation(
                "Aliyun OSS requires a standard regional oss-<region>.aliyuncs.com or s3.oss-<region>.aliyuncs.com endpoint."));
        }

        var region = match.Groups["region"].Value.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(configuredRegion) &&
            !string.Equals(region, configuredRegion.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<AliyunOssEndpointProfile>.Failure(StorageError.Validation(
                $"Aliyun OSS endpoint region '{region}' does not match configured region '{configuredRegion.Trim()}'."));
        }

        var internalSuffix = match.Groups["internal"].Success ? "-internal" : string.Empty;
        return OperationResult<AliyunOssEndpointProfile>.Success(new AliyunOssEndpointProfile(
            region,
            match.Groups["internal"].Success,
            $"https://s3.oss-{region}{internalSuffix}.aliyuncs.com",
            $"https://oss-{region}{internalSuffix}.aliyuncs.com"));
    }
}
