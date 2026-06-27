using System.Globalization;
using System.Security.Cryptography;
using Qiniu.Http;
using Qiniu.Storage;
using Qiniu.Util;
using AtomBox.Providers.ObjectStorage;
using AtomBox.Providers.ObjectStorage.S3Compatible;
using System.Text;

namespace AtomBox.Providers.ObjectStorage.QiniuKodo;

internal sealed class QiniuKodoSdkClient : IQiniuKodoClient
{
    private readonly Mac _mac;
    private readonly Config _config;
    private readonly BucketManager _bucketManager;
    private readonly UploadManager _uploadManager;
    private readonly ResumableUploader _resumableUploader;
    private readonly HttpClient _httpClient;
    private readonly string _downloadDomain;
    private readonly string _accessKeyId;
    private readonly string _accessKeySecret;

    public QiniuKodoSdkClient(
        Mac mac,
        Config config,
        string downloadDomain,
        string accessKeyId,
        string accessKeySecret,
        HttpClient? httpClient = null)
    {
        _mac = mac;
        _config = config;
        _bucketManager = new BucketManager(mac, config, null);
        _uploadManager = new UploadManager(config);
        _resumableUploader = new ResumableUploader(config);
        _downloadDomain = NormalizeDownloadDomain(downloadDomain);
        _accessKeyId = accessKeyId.Trim();
        _accessKeySecret = accessKeySecret;
        _httpClient = httpClient ?? new HttpClient();
    }

    public IReadOnlyList<QiniuKodoBucket> ListBuckets()
    {
        var result = _bucketManager.Buckets(shared: true);
        EnsureSuccess(result);
        return result.Result?
            .Where(bucket => !string.IsNullOrWhiteSpace(bucket))
            .Select(bucket => new QiniuKodoBucket(bucket))
            .ToArray() ?? [];
    }

    public QiniuKodoObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
    {
        var result = _bucketManager.ListFiles(bucketName, prefix, marker: cursor, limit: maxKeys, delimiter: "/");
        EnsureSuccess(result);

        var listInfo = result.Result;
        var objects = listInfo?.Items?
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .Select(item => new QiniuKodoObjectSummary(
                item.Key,
                item.Fsize,
                ToDateTimeOffset(item.PutTime),
                item.Hash,
                item.MimeType))
            .ToArray() ?? [];

        var commonPrefixes = listInfo?.CommonPrefixes?
            .Where(prefixItem => !string.IsNullOrWhiteSpace(prefixItem))
            .ToArray() ?? [];

        return new QiniuKodoObjectListing(objects, commonPrefixes, listInfo?.Marker);
    }

    public void DeleteObject(string bucketName, string key)
    {
        EnsureSuccess(_bucketManager.Delete(bucketName, key));
    }

    public void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey)
    {
        EnsureSuccess(_bucketManager.Copy(sourceBucketName, sourceKey, destinationBucketName, destinationKey, force: true));
    }

    public void PutObject(string bucketName, string key, Stream content, Action<long, long>? progress = null)
    {
        if (IsS3ApiEndpoint(_downloadDomain))
        {
            using var client = new S3CompatibleAwsV4Client(
                ToS3ServiceEndpoint(_downloadDomain, bucketName),
                ExtractS3Region(new Uri(_downloadDomain).Host),
                _accessKeyId,
                _accessKeySecret);
            client.PutObject(bucketName, key, content);
            progress?.Invoke(content.CanSeek ? content.Length : 0, content.CanSeek ? content.Length : 0);
            return;
        }

        var putPolicy = new PutPolicy { Scope = $"{bucketName}:{key}" };
        putPolicy.SetExpires(3600);
        var uploadToken = Auth.CreateUploadToken(_mac, putPolicy.ToJsonString());
        var putExtra = new PutExtra();
        if (progress is not null)
        {
            putExtra.ProgressHandler = (uploaded, total) => progress(uploaded, total);
        }

        EnsureSuccess(_uploadManager.UploadStream(content, key, uploadToken, putExtra));
    }

    public void PutObjectMultipart(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null)
    {
        if (IsS3ApiEndpoint(_downloadDomain))
        {
            using var client = new S3CompatibleAwsV4Client(
                ToS3ServiceEndpoint(_downloadDomain, bucketName),
                ExtractS3Region(new Uri(_downloadDomain).Host),
                _accessKeyId,
                _accessKeySecret);
            client.PutObjectMultipart(bucketName, key, content, contentLength, progress);
            return;
        }

        var putPolicy = new PutPolicy { Scope = $"{bucketName}:{key}" };
        putPolicy.SetExpires(3600);
        var uploadToken = Auth.CreateUploadToken(_mac, putPolicy.ToJsonString());
        var putExtra = new PutExtra
        {
            ResumeRecordFile = Path.Combine(Path.GetTempPath(), "AtomBox", "qiniu-kodo-checkpoints", $"{Sanitize(bucketName)}-{Sanitize(key)}.resume")
        };
        Directory.CreateDirectory(Path.GetDirectoryName(putExtra.ResumeRecordFile)!);
        if (progress is not null)
        {
            putExtra.ProgressHandler = (uploaded, total) => progress(uploaded, total > 0 ? total : contentLength);
        }

        EnsureSuccess(_resumableUploader.UploadStream(content, key, uploadToken, putExtra));
        progress?.Invoke(contentLength, contentLength);
    }

    public async Task<QiniuKodoDownloadObject> GetObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        if (IsS3ApiEndpoint(_downloadDomain))
        {
            return await GetObjectViaS3Async(bucketName, key, cancellationToken).ConfigureAwait(false);
        }

        var url = DownloadManager.CreatePrivateUrl(_mac, _downloadDomain, key, expireInSeconds: 3600);
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new QiniuKodoHttpException((int)response.StatusCode, response.ReasonPhrase);
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new QiniuKodoDownloadObject(new MemoryStream(content, writable: false), content.LongLength);
    }

    private async Task<QiniuKodoDownloadObject> GetObjectViaS3Async(
        string bucketName,
        string key,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(_downloadDomain);
        var path = endpoint.Host.StartsWith($"{bucketName}.", StringComparison.OrdinalIgnoreCase)
            ? $"/{EscapePath(key)}"
            : $"/{bucketName}/{EscapePath(key)}";
        var uri = new UriBuilder(endpoint)
        {
            Path = path,
            Query = string.Empty
        }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        SignS3Request(request, path, ExtractS3Region(endpoint.Host));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new QiniuKodoHttpException((int)response.StatusCode, response.ReasonPhrase);
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new QiniuKodoDownloadObject(new MemoryStream(content, writable: false), content.LongLength);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static void EnsureSuccess(HttpResult result)
    {
        if (result.Code is >= 200 and < 300)
        {
            return;
        }

        throw new QiniuKodoHttpException(result.Code, result.Text);
    }

    private static DateTimeOffset? ToDateTimeOffset(long putTime)
    {
        if (putTime <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(putTime / 10000);
    }

    private static string NormalizeDownloadDomain(string downloadDomain)
    {
        var trimmed = downloadDomain.Trim().TrimEnd('/');
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private static bool IsS3ApiEndpoint(string endpoint)
    {
        var host = new Uri(endpoint).Host;
        return host.Contains(".s3.", StringComparison.OrdinalIgnoreCase) &&
               host.EndsWith(".qiniucs.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractS3Region(string host)
    {
        var marker = ".s3.";
        var index = host.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return "cn-east-1";
        }

        var regionStart = index + marker.Length;
        var regionEnd = host.IndexOf('.', regionStart);
        return regionEnd > regionStart ? host[regionStart..regionEnd] : "cn-east-1";
    }

    private static string ToS3ServiceEndpoint(string endpoint, string bucketName)
    {
        var uri = new Uri(endpoint);
        var bucketPrefix = $"{bucketName}.";
        if (!uri.Host.StartsWith(bucketPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        var builder = new UriBuilder(uri)
        {
            Host = uri.Host[bucketPrefix.Length..],
            Path = string.Empty,
            Query = string.Empty
        };
        return builder.Uri.ToString().TrimEnd('/');
    }

    private void SignS3Request(HttpRequestMessage request, string path, string region)
    {
        const string payloadHash = "UNSIGNED-PAYLOAD";
        var now = DateTimeOffset.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var host = request.RequestUri!.Host;

        request.Headers.TryAddWithoutValidation("host", host);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);

        var canonicalHeaders = $"host:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n";
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = string.Join('\n',
            request.Method.Method,
            NormalizeCanonicalPath(path),
            string.Empty,
            canonicalHeaders,
            signedHeaders,
            payloadHash);
        var credentialScope = $"{dateStamp}/{region}/s3/aws4_request";
        var stringToSign = string.Join('\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            credentialScope,
            ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));
        var signingKey = GetSignatureKey(_accessKeySecret, dateStamp, region, "s3");
        var signature = ToHex(HmacSha256(signingKey, stringToSign));
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"AWS4-HMAC-SHA256 Credential={_accessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static string NormalizeCanonicalPath(string path)
    {
        return string.Join("/", path.Split('/').Select(Uri.EscapeDataString)).Replace("%2F", "/", StringComparison.Ordinal);
    }

    private static string EscapePath(string key)
    {
        return string.Join("/", key.TrimStart('/').Split('/').Select(Uri.EscapeDataString));
    }

    private static byte[] GetSignatureKey(string key, string dateStamp, string regionName, string serviceName)
    {
        var kDate = HmacSha256(Encoding.UTF8.GetBytes($"AWS4{key}"), dateStamp);
        var kRegion = HmacSha256(kDate, regionName);
        var kService = HmacSha256(kRegion, serviceName);
        return HmacSha256(kService, "aws4_request");
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string ToHex(byte[] data)
    {
        return Convert.ToHexString(data).ToLowerInvariant();
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) || ch is '/' or '\\' ? '_' : ch));
    }
}
