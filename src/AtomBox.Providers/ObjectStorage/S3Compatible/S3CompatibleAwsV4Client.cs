using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AtomBox.Providers.ObjectStorage;

namespace AtomBox.Providers.ObjectStorage.S3Compatible;

internal sealed class S3CompatibleAwsV4Client : IS3CompatibleClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _region;
    private readonly string _accessKeyId;
    private readonly string _accessKeySecret;

    public S3CompatibleAwsV4Client(
        string endpoint,
        string region,
        string accessKeyId,
        string accessKeySecret,
        HttpClient? httpClient = null)
    {
        _endpoint = NormalizeEndpoint(endpoint);
        _region = region.Trim();
        _accessKeyId = accessKeyId.Trim();
        _accessKeySecret = accessKeySecret;
        _httpClient = httpClient ?? new HttpClient();
    }

    public IReadOnlyList<S3CompatibleBucket> ListBuckets()
    {
        using var response = Send(HttpMethod.Get, "/", string.Empty, null, "UNSIGNED-PAYLOAD");
        var xml = ReadXml(response);
        return xml.Descendants().Where(item => item.Name.LocalName == "Bucket")
            .Select(item => new S3CompatibleBucket(
                item.Elements().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value ?? string.Empty,
                TryParseDate(item.Elements().FirstOrDefault(child => child.Name.LocalName == "CreationDate")?.Value)))
            .Where(bucket => !string.IsNullOrWhiteSpace(bucket.Name))
            .ToArray();
    }

    public void CreateBucket(string bucketName)
    {
        using var response = Send(HttpMethod.Put, $"/{bucketName}", string.Empty, null, "UNSIGNED-PAYLOAD");
        EnsureSuccess(response);
    }

    public S3CompatibleObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
    {
        var query = new Dictionary<string, string>
        {
            ["list-type"] = "2",
            ["delimiter"] = "/",
            ["max-keys"] = maxKeys.ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            query["prefix"] = prefix;
        }
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query["continuation-token"] = cursor;
        }

        using var response = Send(HttpMethod.Get, $"/{bucketName}", BuildQuery(query), null, "UNSIGNED-PAYLOAD");
        var xml = ReadXml(response);
        var objects = xml.Descendants().Where(item => item.Name.LocalName == "Contents")
            .Select(item => new S3CompatibleObjectSummary(
                item.Elements().FirstOrDefault(child => child.Name.LocalName == "Key")?.Value ?? string.Empty,
                TryParseLong(item.Elements().FirstOrDefault(child => child.Name.LocalName == "Size")?.Value),
                TryParseDate(item.Elements().FirstOrDefault(child => child.Name.LocalName == "LastModified")?.Value),
                item.Elements().FirstOrDefault(child => child.Name.LocalName == "ETag")?.Value?.Trim('"'),
                null))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToArray();
        var commonPrefixes = xml.Descendants().Where(item => item.Name.LocalName == "CommonPrefixes")
            .Select(item => item.Elements().FirstOrDefault(child => child.Name.LocalName == "Prefix")?.Value)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();

        var nextCursor = xml.Descendants()
            .FirstOrDefault(item => item.Name.LocalName == "NextContinuationToken")
            ?.Value;

        return new S3CompatibleObjectListing(objects, commonPrefixes, string.IsNullOrWhiteSpace(nextCursor) ? null : nextCursor);
    }

    public void DeleteObject(string bucketName, string key)
    {
        using var response = Send(HttpMethod.Delete, $"/{bucketName}/{EscapePath(key)}", string.Empty, null, "UNSIGNED-PAYLOAD");
        EnsureSuccess(response);
    }

    public void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey)
    {
        var source = $"/{sourceBucketName}/{EscapePath(sourceKey)}";
        using var response = Send(
            HttpMethod.Put,
            $"/{destinationBucketName}/{EscapePath(destinationKey)}",
            string.Empty,
            null,
            "UNSIGNED-PAYLOAD",
            new Dictionary<string, string>
            {
                ["x-amz-copy-source"] = source
            });
        EnsureSuccess(response);
    }

    public void PutObject(string bucketName, string key, Stream content)
    {
        using var requestContent = new StreamContent(content);
        if (content.CanSeek)
        {
            requestContent.Headers.ContentLength = content.Length - content.Position;
        }

        using var response = Send(HttpMethod.Put, $"/{bucketName}/{EscapePath(key)}", string.Empty, requestContent, "UNSIGNED-PAYLOAD");
        EnsureSuccess(response);
    }

    public void PutObjectMultipart(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null)
    {
        var uploadId = InitiateMultipartUpload(bucketName, key);
        var transferred = 0L;
        var parts = new List<(int PartNumber, string ETag)>();
        try
        {
            var partNumber = 1;
            foreach (var part in ReadParts(content, ObjectStorageUploadPolicy.PartSize))
            {
                var etag = UploadPart(bucketName, key, uploadId, partNumber, part);
                parts.Add((partNumber, etag));
                transferred += part.LongLength;
                progress?.Invoke(Math.Min(contentLength, transferred), contentLength);
                partNumber++;
            }

            CompleteMultipartUpload(bucketName, key, uploadId, parts);
            progress?.Invoke(contentLength, contentLength);
        }
        catch
        {
            AbortMultipartUpload(bucketName, key, uploadId);
            throw;
        }
    }

    public S3CompatibleDownloadObject GetObject(string bucketName, string key)
    {
        using var response = Send(HttpMethod.Get, $"/{bucketName}/{EscapePath(key)}", string.Empty, null, "UNSIGNED-PAYLOAD");
        EnsureSuccess(response);
        var payload = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        return new S3CompatibleDownloadObject(new MemoryStream(payload, writable: false), payload.LongLength);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private HttpResponseMessage Send(
        HttpMethod method,
        string path,
        string query,
        HttpContent? content,
        string payloadHash,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var uriBuilder = new UriBuilder(_endpoint)
        {
            Path = path,
            Query = query
        };
        var uri = uriBuilder.Uri;
        var request = new HttpRequestMessage(method, uri)
        {
            Content = content
        };
        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        Sign(request, path, query, payloadHash, extraHeaders);
        var response = _httpClient.Send(request);
        EnsureSuccess(response);
        return response;
    }

    private string InitiateMultipartUpload(string bucketName, string key)
    {
        using var response = Send(
            HttpMethod.Post,
            $"/{bucketName}/{EscapePath(key)}",
            "uploads=",
            null,
            "UNSIGNED-PAYLOAD");
        var uploadId = ReadXml(response).Descendants()
            .FirstOrDefault(item => item.Name.LocalName == "UploadId")
            ?.Value;
        return string.IsNullOrWhiteSpace(uploadId)
            ? throw new InvalidOperationException("S3 compatible multipart upload id was empty.")
            : uploadId;
    }

    private string UploadPart(string bucketName, string key, string uploadId, int partNumber, byte[] payload)
    {
        using var content = new ByteArrayContent(payload);
        using var response = Send(
            HttpMethod.Put,
            $"/{bucketName}/{EscapePath(key)}",
            BuildQuery(new Dictionary<string, string>
            {
                ["partNumber"] = partNumber.ToString(CultureInfo.InvariantCulture),
                ["uploadId"] = uploadId
            }),
            content,
            "UNSIGNED-PAYLOAD");
        var etag = response.Headers.ETag?.Tag?.Trim('"');
        return string.IsNullOrWhiteSpace(etag)
            ? throw new InvalidOperationException("S3 compatible multipart part etag was empty.")
            : etag;
    }

    private void CompleteMultipartUpload(
        string bucketName,
        string key,
        string uploadId,
        IReadOnlyList<(int PartNumber, string ETag)> parts)
    {
        var xml = new XElement("CompleteMultipartUpload",
            parts.Select(part => new XElement("Part",
                new XElement("PartNumber", part.PartNumber),
                new XElement("ETag", part.ETag))));
        using var content = new StringContent(xml.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/xml");
        using var response = Send(
            HttpMethod.Post,
            $"/{bucketName}/{EscapePath(key)}",
            BuildQuery(new Dictionary<string, string> { ["uploadId"] = uploadId }),
            content,
            "UNSIGNED-PAYLOAD");
        EnsureSuccess(response);
    }

    private void AbortMultipartUpload(string bucketName, string key, string uploadId)
    {
        using var response = Send(
            HttpMethod.Delete,
            $"/{bucketName}/{EscapePath(key)}",
            BuildQuery(new Dictionary<string, string> { ["uploadId"] = uploadId }),
            null,
            "UNSIGNED-PAYLOAD");
        EnsureSuccess(response);
    }

    private static IEnumerable<byte[]> ReadParts(Stream content, long partSize)
    {
        var buffer = new byte[checked((int)partSize)];
        while (true)
        {
            var readTotal = 0;
            while (readTotal < buffer.Length)
            {
                var read = content.Read(buffer, readTotal, buffer.Length - readTotal);
                if (read == 0)
                {
                    break;
                }

                readTotal += read;
            }

            if (readTotal == 0)
            {
                yield break;
            }

            if (readTotal == buffer.Length)
            {
                yield return buffer;
                buffer = new byte[checked((int)partSize)];
            }
            else
            {
                var part = new byte[readTotal];
                Array.Copy(buffer, part, readTotal);
                yield return part;
                yield break;
            }
        }
    }

    private void Sign(
        HttpRequestMessage request,
        string path,
        string query,
        string payloadHash,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var now = DateTimeOffset.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var host = request.RequestUri!.Host;

        request.Headers.TryAddWithoutValidation("host", host);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);

        var signedHeaderValues = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate
        };
        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
            {
                signedHeaderValues[name.ToLowerInvariant()] = value.Trim();
            }
        }

        var canonicalHeaders = string.Concat(signedHeaderValues.Select(pair => $"{pair.Key}:{pair.Value}\n"));
        var signedHeaders = string.Join(";", signedHeaderValues.Keys);
        var canonicalRequest = string.Join('\n',
            request.Method.Method,
            NormalizeCanonicalPath(path),
            query,
            canonicalHeaders,
            signedHeaders,
            payloadHash);
        var credentialScope = $"{dateStamp}/{_region}/s3/aws4_request";
        var stringToSign = string.Join('\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            credentialScope,
            ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));
        var signingKey = GetSignatureKey(_accessKeySecret, dateStamp, _region, "s3");
        var signature = ToHex(HmacSha256(signingKey, stringToSign));
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"AWS4-HMAC-SHA256 Credential={_accessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static XDocument ReadXml(HttpResponseMessage response)
    {
        EnsureSuccess(response);
        var payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return XDocument.Parse(payload);
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new HttpRequestException(
            $"S3 compatible request failed with status {(int)response.StatusCode}.",
            null,
            response.StatusCode);
    }

    private static string BuildQuery(IReadOnlyDictionary<string, string> query)
    {
        return string.Join("&", query
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static string NormalizeCanonicalPath(string path)
    {
        return string.Join("/", path.Split('/').Select(Uri.EscapeDataString)).Replace("%2F", "/", StringComparison.Ordinal);
    }

    private static string EscapePath(string key)
    {
        return string.Join("/", key.TrimStart('/').Split('/').Select(Uri.EscapeDataString));
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private static long TryParseLong(string? value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static DateTimeOffset? TryParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
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
}
