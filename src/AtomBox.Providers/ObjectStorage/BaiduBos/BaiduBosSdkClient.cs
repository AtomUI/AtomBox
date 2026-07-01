using BaiduBce;
using BaiduBce.Services.Bos;
using BaiduBce.Services.Bos.Model;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.BaiduBos;

internal sealed class BaiduBosSdkClient : IS3CompatibleClient
{
    private readonly BceClientConfiguration _baseConfiguration;
    private readonly string _configuredBucketName;
    private readonly string _regionalEndpoint;
    private readonly BosClient _regionalClient;

    public BaiduBosSdkClient(BceClientConfiguration configuration, string? configuredBucketName = null)
    {
        _baseConfiguration = new BceClientConfiguration(configuration);
        _regionalEndpoint = _baseConfiguration.Endpoint.Trim().TrimEnd('/');
        _configuredBucketName = configuredBucketName?.Trim() ?? string.Empty;
        _regionalClient = new BosClient(_baseConfiguration);
    }

    public IReadOnlyList<S3CompatibleBucket> ListBuckets()
    {
        if (!string.IsNullOrWhiteSpace(_configuredBucketName))
        {
            return [new S3CompatibleBucket(_configuredBucketName, null)];
        }

        var response = _regionalClient.ListBuckets();
        return response.Buckets
            .Select(bucket => new S3CompatibleBucket(bucket.Name, ToDateTimeOffset(bucket.CreationDate)))
            .Where(bucket => !string.IsNullOrWhiteSpace(bucket.Name))
            .ToArray();
    }

    public void CreateBucket(string bucketName)
    {
        _regionalClient.CreateBucket(bucketName);
    }

    public S3CompatibleObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
    {
        var request = new ListObjectsRequest
        {
            BucketName = string.Empty,
            Prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix,
            Marker = string.IsNullOrWhiteSpace(cursor) ? null : cursor,
            Delimiter = "/",
            MaxKeys = maxKeys
        };
        var response = CreateBucketClient(bucketName).ListObjects(request);
        var objects = response.Contents
            .Select(item => new S3CompatibleObjectSummary(
                item.Key,
                item.Size,
                ToDateTimeOffset(item.LastModified),
                item.ETag?.Trim('"'),
                null))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToArray();
        var commonPrefixes = response.CommonPrefixes
            .Select(item => item.Prefix)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();

        return new S3CompatibleObjectListing(
            objects,
            commonPrefixes,
            response.IsTruncated && !string.IsNullOrWhiteSpace(response.NextMarker) ? response.NextMarker : null);
    }

    public void DeleteObject(string bucketName, string key)
    {
        CreateBucketClient(bucketName).DeleteObject(string.Empty, key);
    }

    public void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey)
    {
        CreateBucketClient(destinationBucketName).CopyObject(sourceBucketName, sourceKey, string.Empty, destinationKey);
    }

    public void PutObject(string bucketName, string key, Stream content)
    {
        CreateBucketClient(bucketName).PutObject(string.Empty, key, content);
    }

    public void PutObjectMultipart(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null)
    {
        var client = CreateBucketClient(bucketName);
        var uploadId = client.InitiateMultipartUpload(string.Empty, key).UploadId;
        var transferred = 0L;
        var partEtags = new List<PartETag>();
        try
        {
            var partNumber = 1;
            foreach (var part in ReadParts(content, ObjectStorageUploadPolicy.PartSize))
            {
                using var partStream = new MemoryStream(part, writable: false);
                var request = new UploadPartRequest
                {
                    BucketName = string.Empty,
                    Key = key,
                    UploadId = uploadId,
                    PartNumber = partNumber,
                    PartSize = part.LongLength,
                    InputStream = partStream
                };
                var response = client.UploadPart(request);
                partEtags.Add(new PartETag
                {
                    PartNumber = partNumber,
                    ETag = response.ETag
                });
                transferred += part.LongLength;
                progress?.Invoke(Math.Min(contentLength, transferred), contentLength);
                partNumber++;
            }

            client.CompleteMultipartUpload(string.Empty, key, uploadId, partEtags);
            progress?.Invoke(contentLength, contentLength);
        }
        catch
        {
            client.AbortMultipartUpload(string.Empty, key, uploadId);
            throw;
        }
    }

    public S3CompatibleDownloadObject GetObject(string bucketName, string key)
    {
        var response = CreateBucketClient(bucketName).GetObject(string.Empty, key);
        return new S3CompatibleDownloadObject(response.ObjectContent, response.ObjectMetadata?.ContentLength);
    }

    public void Dispose()
    {
    }

    private BosClient CreateBucketClient(string bucketName)
    {
        var normalizedBucket = string.IsNullOrWhiteSpace(_configuredBucketName)
            ? bucketName.Trim()
            : _configuredBucketName;
        var configuration = new BceClientConfiguration(_baseConfiguration)
        {
            Endpoint = ToBucketEndpoint(normalizedBucket)
        };
        return new BosClient(configuration);
    }

    private string ToBucketEndpoint(string bucketName)
    {
        if (string.IsNullOrWhiteSpace(bucketName) ||
            !Uri.TryCreate(_regionalEndpoint, UriKind.Absolute, out var uri))
        {
            return _regionalEndpoint;
        }

        var bucketPrefix = $"{bucketName}.";
        if (uri.Host.StartsWith(bucketPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return _regionalEndpoint;
        }

        var builder = new UriBuilder(uri)
        {
            Host = bucketPrefix + uri.Host
        };
        return builder.Uri.ToString().TrimEnd('/');
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

    private static DateTimeOffset? ToDateTimeOffset(DateTime value)
    {
        return value == default ? null : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}