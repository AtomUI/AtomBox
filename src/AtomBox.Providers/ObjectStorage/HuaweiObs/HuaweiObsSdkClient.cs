using AtomBox.Providers.ObjectStorage.S3Compatible;
using OBS;
using OBS.Model;

namespace AtomBox.Providers.ObjectStorage.HuaweiObs;

internal sealed class HuaweiObsSdkClient : IS3CompatibleClient
{
    private readonly ObsClient _client;

    public HuaweiObsSdkClient(
        string endpoint,
        string accessKeyId,
        string accessKeySecret)
    {
        var config = new ObsConfig
        {
            Endpoint = NormalizeEndpoint(endpoint)
        };
        _client = new ObsClient(accessKeyId.Trim(), accessKeySecret, config);
    }

    public IReadOnlyList<S3CompatibleBucket> ListBuckets()
    {
        return _client.ListBuckets(new ListBucketsRequest()).Buckets
            .Select(bucket => new S3CompatibleBucket(bucket.BucketName, bucket.CreationDate))
            .Where(bucket => !string.IsNullOrWhiteSpace(bucket.Name))
            .ToArray();
    }

    public void CreateBucket(string bucketName)
    {
        _client.CreateBucket(new CreateBucketRequest
        {
            BucketName = bucketName
        });
    }

    public S3CompatibleObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
    {
        var response = _client.ListObjects(new ListObjectsRequest
        {
            BucketName = bucketName,
            Prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix,
            Delimiter = "/",
            MaxKeys = maxKeys,
            Marker = cursor
        });
        var objects = response.ObsObjects
            .Select(item => new S3CompatibleObjectSummary(
                item.ObjectKey,
                item.Size,
                item.LastModified is null ? null : new DateTimeOffset(DateTime.SpecifyKind(item.LastModified.Value, DateTimeKind.Utc)),
                item.ETag?.Trim('"'),
                null))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToArray();
        var commonPrefixes = response.CommonPrefixes
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        return new S3CompatibleObjectListing(
            objects,
            commonPrefixes,
            response.IsTruncated && !string.IsNullOrWhiteSpace(response.NextMarker) ? response.NextMarker : null);
    }

    public void DeleteObject(string bucketName, string key)
    {
        _client.DeleteObject(new DeleteObjectRequest
        {
            BucketName = bucketName,
            ObjectKey = key
        });
    }

    public void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey)
    {
        _client.CopyObject(new CopyObjectRequest
        {
            SourceBucketName = sourceBucketName,
            SourceObjectKey = sourceKey,
            BucketName = destinationBucketName,
            ObjectKey = destinationKey
        });
    }

    public void PutObject(string bucketName, string key, Stream content)
    {
        _client.PutObject(new PutObjectRequest
        {
            BucketName = bucketName,
            ObjectKey = key,
            InputStream = content,
            AutoClose = false
        });
    }

    public void PutObjectMultipart(string bucketName, string key, Stream content, long contentLength, Action<long, long>? progress = null)
    {
        var initiate = _client.InitiateMultipartUpload(new InitiateMultipartUploadRequest
        {
            BucketName = bucketName,
            ObjectKey = key
        });
        if (string.IsNullOrWhiteSpace(initiate.UploadId))
        {
            throw new InvalidOperationException("Huawei OBS multipart upload id was empty.");
        }

        var transferred = 0L;
        var partETags = new List<PartETag>();
        try
        {
            var partNumber = 1;
            foreach (var part in ReadParts(content, ObjectStorageUploadPolicy.PartSize))
            {
                using var partStream = new MemoryStream(part, writable: false);
                var response = _client.UploadPart(new UploadPartRequest
                {
                    BucketName = bucketName,
                    ObjectKey = key,
                    UploadId = initiate.UploadId,
                    PartNumber = partNumber,
                    PartSize = part.LongLength,
                    InputStream = partStream,
                    AutoClose = false
                });
                if (string.IsNullOrWhiteSpace(response.ETag))
                {
                    throw new InvalidOperationException("Huawei OBS multipart part etag was empty.");
                }

                partETags.Add(new PartETag(partNumber, response.ETag));
                transferred += part.LongLength;
                progress?.Invoke(Math.Min(contentLength, transferred), contentLength);
                partNumber++;
            }

            _client.CompleteMultipartUpload(new CompleteMultipartUploadRequest
            {
                BucketName = bucketName,
                ObjectKey = key,
                UploadId = initiate.UploadId,
                PartETags = partETags.OrderBy(item => item.PartNumber).ToArray()
            });
            progress?.Invoke(contentLength, contentLength);
        }
        catch
        {
            TryAbortMultipartUpload(bucketName, key, initiate.UploadId);
            throw;
        }
    }

    public S3CompatibleDownloadObject GetObject(string bucketName, string key)
    {
        var response = _client.GetObject(new GetObjectRequest
        {
            BucketName = bucketName,
            ObjectKey = key
        });

        return new S3CompatibleDownloadObject(response.OutputStream, response.ContentLength);
    }

    public void Dispose()
    {
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private void TryAbortMultipartUpload(string bucketName, string key, string uploadId)
    {
        try
        {
            _client.AbortMultipartUpload(new AbortMultipartUploadRequest
            {
                BucketName = bucketName,
                ObjectKey = key,
                UploadId = uploadId
            });
        }
        catch
        {
        }
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
}
