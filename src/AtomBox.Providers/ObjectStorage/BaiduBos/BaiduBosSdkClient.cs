using BaiduBce.Services.Bos;
using BaiduBce.Services.Bos.Model;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.BaiduBos;

internal sealed class BaiduBosSdkClient : IS3CompatibleClient
{
    private readonly BosClient _client;
    private readonly string _configuredBucketName;
    private readonly bool _useBucketEndpoint;

    public BaiduBosSdkClient(BosClient client, string? configuredBucketName = null, bool useBucketEndpoint = false)
    {
        _client = client;
        _configuredBucketName = configuredBucketName?.Trim() ?? string.Empty;
        _useBucketEndpoint = useBucketEndpoint;
    }

    public IReadOnlyList<S3CompatibleBucket> ListBuckets()
    {
        if (_useBucketEndpoint && !string.IsNullOrWhiteSpace(_configuredBucketName))
        {
            return [new S3CompatibleBucket(_configuredBucketName, null)];
        }

        var response = _client.ListBuckets();
        return response.Buckets
            .Select(bucket => new S3CompatibleBucket(bucket.Name, ToDateTimeOffset(bucket.CreationDate)))
            .Where(bucket => !string.IsNullOrWhiteSpace(bucket.Name))
            .ToArray();
    }

    public void CreateBucket(string bucketName)
    {
        _client.CreateBucket(ToSdkBucketName(bucketName));
    }

    public S3CompatibleObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
    {
        var request = new ListObjectsRequest
        {
            BucketName = ToSdkBucketName(bucketName),
            Prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix,
            Marker = string.IsNullOrWhiteSpace(cursor) ? null : cursor,
            Delimiter = "/",
            MaxKeys = maxKeys
        };
        var response = _client.ListObjects(request);
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
        _client.DeleteObject(ToSdkBucketName(bucketName), key);
    }

    public void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey)
    {
        _client.CopyObject(sourceBucketName, sourceKey, ToSdkBucketName(destinationBucketName), destinationKey);
    }

    public void PutObject(string bucketName, string key, Stream content)
    {
        _client.PutObject(ToSdkBucketName(bucketName), key, content);
    }

    public void PutObjectMultipart(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null)
    {
        var sdkBucketName = ToSdkBucketName(bucketName);
        var uploadId = _client.InitiateMultipartUpload(sdkBucketName, key).UploadId;
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
                    BucketName = ToSdkBucketName(bucketName),
                    Key = key,
                    UploadId = uploadId,
                    PartNumber = partNumber,
                    PartSize = part.LongLength,
                    InputStream = partStream
                };
                var response = _client.UploadPart(request);
                partEtags.Add(new PartETag
                {
                    PartNumber = partNumber,
                    ETag = response.ETag
                });
                transferred += part.LongLength;
                progress?.Invoke(Math.Min(contentLength, transferred), contentLength);
                partNumber++;
            }

            _client.CompleteMultipartUpload(sdkBucketName, key, uploadId, partEtags);
            progress?.Invoke(contentLength, contentLength);
        }
        catch
        {
            _client.AbortMultipartUpload(sdkBucketName, key, uploadId);
            throw;
        }
    }

    public S3CompatibleDownloadObject GetObject(string bucketName, string key)
    {
        var response = _client.GetObject(ToSdkBucketName(bucketName), key);
        return new S3CompatibleDownloadObject(response.ObjectContent, response.ObjectMetadata?.ContentLength);
    }

    public void Dispose()
    {
    }

    private string ToSdkBucketName(string bucketName)
    {
        return _useBucketEndpoint &&
               (string.IsNullOrWhiteSpace(_configuredBucketName) || bucketName.Equals(_configuredBucketName, StringComparison.Ordinal))
            ? string.Empty
            : bucketName;
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
