using AtomBox.Providers.ObjectStorage.S3Compatible;
using TOS;
using TOS.Model;

namespace AtomBox.Providers.ObjectStorage.VolcengineTos;

internal sealed class VolcengineTosSdkClient : IS3CompatibleClient
{
    private readonly ITosClient _client;

    public VolcengineTosSdkClient(
        string endpoint,
        string region,
        string accessKeyId,
        string accessKeySecret)
    {
        _client = TosClientBuilder.Builder()
            .SetEndpoint(NormalizeEndpoint(endpoint))
            .SetRegion(region.Trim())
            .SetAk(accessKeyId.Trim())
            .SetSk(accessKeySecret)
            .Build();
    }

    public IReadOnlyList<S3CompatibleBucket> ListBuckets()
    {
        return _client.ListBuckets().Buckets
            .Select(bucket => new S3CompatibleBucket(
                bucket.Name,
                TryParseDate(bucket.CreationDate)))
            .Where(bucket => !string.IsNullOrWhiteSpace(bucket.Name))
            .ToArray();
    }

    public void CreateBucket(string bucketName)
    {
        _client.CreateBucket(new CreateBucketInput
        {
            Bucket = bucketName
        });
    }

    public S3CompatibleObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
    {
        var output = _client.ListObjects(new ListObjectsInput
        {
            Bucket = bucketName,
            Prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix,
            Delimiter = "/",
            MaxKeys = maxKeys,
            Marker = cursor
        });
        var objects = output.Contents
            .Select(item => new S3CompatibleObjectSummary(
                item.Key,
                item.Size,
                item.LastModified is null ? null : new DateTimeOffset(DateTime.SpecifyKind(item.LastModified.Value, DateTimeKind.Utc)),
                item.ETag?.Trim('"'),
                null))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToArray();
        var commonPrefixes = output.CommonPrefixes
            .Select(item => item.Prefix)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();

        return new S3CompatibleObjectListing(
            objects,
            commonPrefixes,
            output.IsTruncated == true && !string.IsNullOrWhiteSpace(output.NextMarker) ? output.NextMarker : null);
    }

    public void DeleteObject(string bucketName, string key)
    {
        _client.DeleteObject(new DeleteObjectInput
        {
            Bucket = bucketName,
            Key = key
        });
    }

    public void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey)
    {
        _client.CopyObject(new CopyObjectInput
        {
            SrcBucket = sourceBucketName,
            SrcKey = sourceKey,
            Bucket = destinationBucketName,
            Key = destinationKey
        });
    }

    public void PutObject(string bucketName, string key, Stream content)
    {
        _client.PutObject(new PutObjectInput
        {
            Bucket = bucketName,
            Key = key,
            Content = content
        });
    }

    public void PutObjectMultipart(string bucketName, string key, Stream content, long contentLength, Action<long, long>? progress = null)
    {
        var createOutput = _client.CreateMultipartUpload(new CreateMultipartUploadInput
        {
            Bucket = bucketName,
            Key = key
        });
        if (string.IsNullOrWhiteSpace(createOutput.UploadID))
        {
            throw new InvalidOperationException("Volcengine TOS multipart upload id was empty.");
        }

        var transferred = 0L;
        var parts = new List<UploadedPart>();
        try
        {
            var partNumber = 1;
            foreach (var part in ReadParts(content, ObjectStorageUploadPolicy.PartSize))
            {
                using var partStream = new MemoryStream(part, writable: false);
                var output = _client.UploadPart(new UploadPartInput
                {
                    Bucket = bucketName,
                    Key = key,
                    UploadID = createOutput.UploadID,
                    PartNumber = partNumber,
                    ContentLength = part.LongLength,
                    Content = partStream
                });
                if (string.IsNullOrWhiteSpace(output.ETag))
                {
                    throw new InvalidOperationException("Volcengine TOS multipart part etag was empty.");
                }

                parts.Add(new UploadedPart
                {
                    PartNumber = partNumber,
                    ETag = output.ETag
                });
                transferred += part.LongLength;
                progress?.Invoke(Math.Min(contentLength, transferred), contentLength);
                partNumber++;
            }

            _client.CompleteMultipartUpload(new CompleteMultipartUploadInput
            {
                Bucket = bucketName,
                Key = key,
                UploadID = createOutput.UploadID,
                Parts = parts.OrderBy(item => item.PartNumber).ToArray()
            });
            progress?.Invoke(contentLength, contentLength);
        }
        catch
        {
            TryAbortMultipartUpload(bucketName, key, createOutput.UploadID);
            throw;
        }
    }

    public S3CompatibleDownloadObject GetObject(string bucketName, string key)
    {
        var output = _client.GetObject(new GetObjectInput
        {
            Bucket = bucketName,
            Key = key
        });

        return new S3CompatibleDownloadObject(output.Content, null);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private static DateTimeOffset? TryParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private void TryAbortMultipartUpload(string bucketName, string key, string uploadId)
    {
        try
        {
            _client.AbortMultipartUpload(new AbortMultipartUploadInput
            {
                Bucket = bucketName,
                Key = key,
                UploadID = uploadId
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
