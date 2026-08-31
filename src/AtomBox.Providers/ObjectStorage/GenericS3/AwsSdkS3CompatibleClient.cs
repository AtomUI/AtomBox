using Amazon.S3;
using Amazon.S3.Model;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.GenericS3;

internal sealed class AwsSdkS3CompatibleClient : IS3CompatibleClient, ICancellableS3TransferClient
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);
    private readonly IAmazonS3 _client;

    public AwsSdkS3CompatibleClient(IAmazonS3 client)
    {
        _client = client;
    }

    public IReadOnlyList<S3CompatibleBucket> ListBuckets()
    {
        var response = Wait(_client.ListBucketsAsync());
        return (response.Buckets ?? [])
            .Select(bucket => new S3CompatibleBucket(bucket.BucketName, ToDateTimeOffset(bucket.CreationDate)))
            .ToArray();
    }

    public void CreateBucket(string bucketName)
    {
        Wait(_client.PutBucketAsync(new PutBucketRequest { BucketName = bucketName }));
    }

    public S3CompatibleObjectListing ListObjects(
        string bucketName,
        string prefix,
        string? cursor = null,
        int maxKeys = 1000)
    {
        var response = Wait(_client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = bucketName,
            Prefix = prefix,
            Delimiter = "/",
            ContinuationToken = cursor,
            MaxKeys = maxKeys
        }));

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

    public void DeleteObject(string bucketName, string key)
    {
        Wait(_client.DeleteObjectAsync(bucketName, key));
    }

    public void CopyObject(
        string sourceBucketName,
        string sourceKey,
        string destinationBucketName,
        string destinationKey)
    {
        Wait(_client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = sourceBucketName,
            SourceKey = sourceKey,
            DestinationBucket = destinationBucketName,
            DestinationKey = destinationKey
        }));
    }

    public void PutObject(string bucketName, string key, Stream content)
    {
        PutObjectAsync(bucketName, key, content).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public async Task PutObjectAsync(
        string bucketName,
        string key,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = content,
            AutoCloseStream = false
        }, cancellationToken).ConfigureAwait(false);
    }

    public void PutObjectMultipart(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null)
    {
        PutObjectMultipartAsync(bucketName, key, content, contentLength, progress)
            .ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public async Task PutObjectMultipartAsync(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var initiation = await _client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = bucketName,
            Key = key
        }, cancellationToken).ConfigureAwait(false);
        var parts = new List<PartETag>();
        var transferred = 0L;

        try
        {
            var partNumber = 1;
            while (transferred < contentLength)
            {
                var expectedLength = (int)Math.Min(ObjectStorageUploadPolicy.PartSize, contentLength - transferred);
                var payload = await ReadPartAsync(content, expectedLength, cancellationToken).ConfigureAwait(false);
                if (payload.Length == 0)
                {
                    throw new EndOfStreamException("S3 multipart input ended before contentLength bytes were read.");
                }

                using var partStream = new MemoryStream(payload, writable: false);
                var response = await _client.UploadPartAsync(new UploadPartRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    UploadId = initiation.UploadId,
                    PartNumber = partNumber,
                    PartSize = payload.LongLength,
                    InputStream = partStream
                }, cancellationToken).ConfigureAwait(false);
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
        catch (Exception exception)
        {
            try
            {
                using var cleanup = new CancellationTokenSource(CleanupTimeout);
                await _client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    UploadId = initiation.UploadId
                }, cleanup.Token).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                if (exception is OperationCanceledException || cancellationToken.IsCancellationRequested)
                {
                    throw CreateCleanupWarning(cleanupException, cancellationToken);
                }
            }

            throw;
        }
    }

    public S3CompatibleDownloadObject GetObject(string bucketName, string key)
    {
        return GetObjectAsync(bucketName, key).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public async Task<S3CompatibleDownloadObject> GetObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = bucketName,
            Key = key
        }, cancellationToken).ConfigureAwait(false);
        return new S3CompatibleDownloadObject(response.ResponseStream, response.ContentLength, response);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static T Wait<T>(Task<T> operation)
    {
        return operation.ConfigureAwait(false).GetAwaiter().GetResult();
    }

    private static async Task<byte[]> ReadPartAsync(
        Stream content,
        int expectedLength,
        CancellationToken cancellationToken)
    {
        var payload = new byte[expectedLength];
        var offset = 0;
        while (offset < payload.Length)
        {
            var read = await content.ReadAsync(
                payload.AsMemory(offset, payload.Length - offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset == payload.Length ? payload : payload[..offset];
    }

    private static ProviderOperationCanceledException CreateCleanupWarning(
        Exception cleanupException,
        CancellationToken cancellationToken)
    {
        var reason = cleanupException is OperationCanceledException
            ? "Remote multipart cleanup timed out; incomplete parts may remain."
            : "Remote multipart cleanup failed; incomplete parts may remain.";
        return new ProviderOperationCanceledException(
            $"Provider operation was canceled. {reason}",
            cleanupException,
            cancellationToken);
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        return value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }
}
