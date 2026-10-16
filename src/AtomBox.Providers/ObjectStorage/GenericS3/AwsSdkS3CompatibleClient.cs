using Amazon.S3;
using Amazon.S3.Model;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.GenericS3;

internal sealed class AwsSdkS3CompatibleClient : IAsyncS3CompatibleClient
{
    private readonly IAmazonS3 _client;

    public AwsSdkS3CompatibleClient(IAmazonS3 client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<S3CompatibleBucket>> ListBucketsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _client.ListBucketsAsync(cancellationToken).ConfigureAwait(false);
        return (response.Buckets ?? [])
            .Select(bucket => new S3CompatibleBucket(bucket.BucketName, ToDateTimeOffset(bucket.CreationDate)))
            .ToArray();
    }

    public async Task CreateBucketAsync(
        string bucketName,
        CancellationToken cancellationToken = default)
    {
        await _client.PutBucketAsync(
            new PutBucketRequest { BucketName = bucketName },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<S3CompatibleObjectListing> ListObjectsAsync(
        string bucketName,
        string prefix,
        string? cursor = null,
        int maxKeys = 1000,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.ListObjectsV2Async(
            new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = prefix,
                Delimiter = "/",
                ContinuationToken = cursor,
                MaxKeys = maxKeys
            },
            cancellationToken).ConfigureAwait(false);

        var objects = (response.S3Objects ?? [])
            .Select(item => new S3CompatibleObjectSummary(
                item.Key,
                item.Size ?? 0,
                ToDateTimeOffset(item.LastModified),
                item.ETag?.Trim('"'),
                null))
            .ToArray();
        return new S3CompatibleObjectListing(objects, response.CommonPrefixes ?? [], response.NextContinuationToken);
    }

    public async Task DeleteObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        await _client.DeleteObjectAsync(bucketName, key, cancellationToken).ConfigureAwait(false);
    }

    public async Task CopyObjectAsync(
        string sourceBucketName,
        string sourceKey,
        string destinationBucketName,
        string destinationKey,
        CancellationToken cancellationToken = default)
    {
        await _client.CopyObjectAsync(
            new CopyObjectRequest
            {
                SourceBucket = sourceBucketName,
                SourceKey = sourceKey,
                DestinationBucket = destinationBucketName,
                DestinationKey = destinationKey
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task PutObjectAsync(
        string bucketName,
        string key,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                InputStream = content,
                AutoCloseStream = false
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task PutObjectMultipartAsync(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var initiation = await _client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest
            {
                BucketName = bucketName,
                Key = key
            },
            cancellationToken).ConfigureAwait(false);
        var parts = new List<PartETag>();
        var transferred = 0L;

        try
        {
            var partNumber = 1;
            while (transferred < contentLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var expectedLength = (int)Math.Min(ObjectStorageUploadPolicy.PartSize, contentLength - transferred);
                var payload = ReadPart(content, expectedLength);
                if (payload.Length == 0)
                {
                    throw new EndOfStreamException("S3 multipart input ended before contentLength bytes were read.");
                }

                using var partStream = new MemoryStream(payload, writable: false);
                var response = await _client.UploadPartAsync(
                    new UploadPartRequest
                    {
                        BucketName = bucketName,
                        Key = key,
                        UploadId = initiation.UploadId,
                        PartNumber = partNumber,
                        PartSize = payload.LongLength,
                        InputStream = partStream
                    },
                    cancellationToken).ConfigureAwait(false);
                parts.Add(new PartETag(partNumber, response.ETag));
                transferred += payload.LongLength;
                progress?.Invoke(transferred, contentLength);
                partNumber++;
            }

            var completion = new CompleteMultipartUploadRequest
            {
                BucketName = bucketName,
                Key = key,
                UploadId = initiation.UploadId
            };
            completion.AddPartETags(parts);
            await _client.CompleteMultipartUploadAsync(completion, cancellationToken).ConfigureAwait(false);
            progress?.Invoke(contentLength, contentLength);
        }
        catch
        {
            try
            {
                await _client.AbortMultipartUploadAsync(
                    new AbortMultipartUploadRequest
                    {
                        BucketName = bucketName,
                        Key = key,
                        UploadId = initiation.UploadId
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original multipart failure.
            }

            throw;
        }
    }

    public async Task<S3CompatibleDownloadObject> GetObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.GetObjectAsync(bucketName, key, cancellationToken).ConfigureAwait(false);
        return new S3CompatibleDownloadObject(response.ResponseStream, response.ContentLength, response);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static byte[] ReadPart(Stream content, int expectedLength)
    {
        var payload = new byte[expectedLength];
        var offset = 0;
        while (offset < payload.Length)
        {
            var read = content.Read(payload, offset, payload.Length - offset);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset == payload.Length ? payload : payload[..offset];
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        return value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }
}
