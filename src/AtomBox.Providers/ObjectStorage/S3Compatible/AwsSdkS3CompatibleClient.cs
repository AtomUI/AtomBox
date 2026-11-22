using Amazon.S3;
using Amazon.S3.Model;

namespace AtomBox.Providers.ObjectStorage.S3Compatible;

internal enum S3ObjectListingApi
{
    V1,
    V2
}

internal sealed record AwsSdkS3ClientOptions(
    S3ObjectListingApi ObjectListingApi = S3ObjectListingApi.V2,
    bool UseChunkEncoding = true,
    bool DisablePayloadSigning = false,
    long? SingleCopyLimit = null,
    long MultipartCopyPartSize = 256L * 1024 * 1024,
    TimeSpan? CleanupTimeout = null,
    S3CannedACL? CreateBucketAcl = null)
{
    public TimeSpan EffectiveCleanupTimeout => CleanupTimeout ?? TimeSpan.FromSeconds(10);
}

/// <summary>
/// Cancellation-aware S3-compatible transport. Every data-plane call receives the caller token;
/// multipart cleanup deliberately uses a short independent token after cancellation.
/// </summary>
internal sealed class AwsSdkS3CompatibleClient : IAsyncS3CompatibleClient
{
    private readonly IAmazonS3 _client;
    private readonly AwsSdkS3ClientOptions _options;

    public AwsSdkS3CompatibleClient(IAmazonS3 client, AwsSdkS3ClientOptions? options = null)
    {
        _client = client;
        _options = options ?? new AwsSdkS3ClientOptions();
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
        var request = new PutBucketRequest { BucketName = bucketName };
        if (_options.CreateBucketAcl is { } acl)
        {
            request.CannedACL = acl;
        }

        await _client.PutBucketAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<S3CompatibleObjectListing> ListObjectsAsync(
        string bucketName,
        string prefix,
        string? cursor = null,
        int maxKeys = 1000,
        CancellationToken cancellationToken = default)
    {
        return _options.ObjectListingApi == S3ObjectListingApi.V1
            ? ListObjectsV1Async(bucketName, prefix, cursor, maxKeys, cancellationToken)
            : ListObjectsV2Async(bucketName, prefix, cursor, maxKeys, cancellationToken);
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
        if (_options.SingleCopyLimit is not { } copyLimit)
        {
            await CopyObjectDirectAsync(
                sourceBucketName,
                sourceKey,
                destinationBucketName,
                destinationKey,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var metadata = await _client.GetObjectMetadataAsync(
            new GetObjectMetadataRequest
            {
                BucketName = sourceBucketName,
                Key = sourceKey
            },
            cancellationToken).ConfigureAwait(false);
        var contentLength = metadata.Headers.ContentLength;
        if (contentLength <= copyLimit)
        {
            await CopyObjectDirectAsync(
                sourceBucketName,
                sourceKey,
                destinationBucketName,
                destinationKey,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await CopyObjectMultipartAsync(
            sourceBucketName,
            sourceKey,
            destinationBucketName,
            destinationKey,
            contentLength,
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
                AutoCloseStream = false,
                UseChunkEncoding = _options.UseChunkEncoding,
                DisablePayloadSigning = _options.DisablePayloadSigning
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
                var payload = await ReadPartAsync(content, expectedLength, cancellationToken).ConfigureAwait(false);
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
                        InputStream = partStream,
                        UseChunkEncoding = _options.UseChunkEncoding,
                        DisablePayloadSigning = _options.DisablePayloadSigning
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
            await TryAbortMultipartAsync(bucketName, key, initiation.UploadId).ConfigureAwait(false);
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

    private async Task<S3CompatibleObjectListing> ListObjectsV1Async(
        string bucketName,
        string prefix,
        string? cursor,
        int maxKeys,
        CancellationToken cancellationToken)
    {
        var response = await _client.ListObjectsAsync(
            new ListObjectsRequest
            {
                BucketName = bucketName,
                Prefix = prefix,
                Delimiter = "/",
                Marker = cursor,
                MaxKeys = maxKeys
            },
            cancellationToken).ConfigureAwait(false);
        return ToListing(
            response.S3Objects,
            response.CommonPrefixes,
            response.IsTruncated == true ? response.NextMarker : null);
    }

    private async Task<S3CompatibleObjectListing> ListObjectsV2Async(
        string bucketName,
        string prefix,
        string? cursor,
        int maxKeys,
        CancellationToken cancellationToken)
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
        return ToListing(response.S3Objects, response.CommonPrefixes, response.NextContinuationToken);
    }

    private async Task CopyObjectDirectAsync(
        string sourceBucketName,
        string sourceKey,
        string destinationBucketName,
        string destinationKey,
        CancellationToken cancellationToken)
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

    private async Task CopyObjectMultipartAsync(
        string sourceBucketName,
        string sourceKey,
        string destinationBucketName,
        string destinationKey,
        long contentLength,
        CancellationToken cancellationToken)
    {
        var initiation = await _client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest
            {
                BucketName = destinationBucketName,
                Key = destinationKey
            },
            cancellationToken).ConfigureAwait(false);
        var parts = new List<PartETag>();

        try
        {
            var partNumber = 1;
            for (var firstByte = 0L; firstByte < contentLength; firstByte += _options.MultipartCopyPartSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lastByte = Math.Min(firstByte + _options.MultipartCopyPartSize - 1, contentLength - 1);
                var response = await _client.CopyPartAsync(
                    new CopyPartRequest
                    {
                        SourceBucket = sourceBucketName,
                        SourceKey = sourceKey,
                        DestinationBucket = destinationBucketName,
                        DestinationKey = destinationKey,
                        UploadId = initiation.UploadId,
                        PartNumber = partNumber,
                        FirstByte = firstByte,
                        LastByte = lastByte
                    },
                    cancellationToken).ConfigureAwait(false);
                parts.Add(new PartETag(partNumber, response.ETag));
                partNumber++;
            }

            var completion = new CompleteMultipartUploadRequest
            {
                BucketName = destinationBucketName,
                Key = destinationKey,
                UploadId = initiation.UploadId
            };
            completion.AddPartETags(parts);
            await _client.CompleteMultipartUploadAsync(completion, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryAbortMultipartAsync(destinationBucketName, destinationKey, initiation.UploadId).ConfigureAwait(false);
            throw;
        }
    }

    private async Task TryAbortMultipartAsync(string bucketName, string key, string uploadId)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(_options.EffectiveCleanupTimeout);
            await _client.AbortMultipartUploadAsync(
                new AbortMultipartUploadRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    UploadId = uploadId
                },
                cleanup.Token).ConfigureAwait(false);
        }
        catch
        {
            // Cleanup must never replace the original transfer/cancellation failure.
        }
    }

    private static S3CompatibleObjectListing ToListing(
        IEnumerable<S3Object>? s3Objects,
        IEnumerable<string>? commonPrefixes,
        string? nextCursor)
    {
        var objects = (s3Objects ?? [])
            .Select(item => new S3CompatibleObjectSummary(
                item.Key,
                item.Size ?? 0,
                ToDateTimeOffset(item.LastModified),
                item.ETag?.Trim('"'),
                null))
            .ToArray();
        return new S3CompatibleObjectListing(objects, (commonPrefixes ?? []).ToArray(), nextCursor);
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

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        return value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }
}
