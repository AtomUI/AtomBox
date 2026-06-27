using COSXML;
using COSXML.Model.Bucket;
using COSXML.Model.Object;
using COSXML.Model.Service;
using AtomBox.Providers.ObjectStorage;

namespace AtomBox.Providers.ObjectStorage.TencentCos;

internal sealed class TencentCosSdkClient : ITencentCosClient
{
    private readonly CosXmlServer _client;
    private readonly string _region;

    public TencentCosSdkClient(CosXmlServer client, string region)
    {
        _client = client;
        _region = region.Trim();
    }

    public IReadOnlyList<TencentCosBucket> ListBuckets()
    {
        var result = _client.GetService(new GetServiceRequest());
        return result.listAllMyBuckets?.buckets?
            .Select(bucket => new TencentCosBucket(
                bucket.name,
                bucket.location,
                TryParseDate(bucket.createDate)))
            .Where(bucket => !string.IsNullOrWhiteSpace(bucket.Name))
            .ToArray() ?? [];
    }

    public void CreateBucket(string bucketName)
    {
        _client.PutBucket(new PutBucketRequest(bucketName));
    }

    public TencentCosObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
    {
        var request = new GetBucketRequest(bucketName);
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            request.SetPrefix(prefix);
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            request.SetMarker(cursor);
        }

        request.SetDelimiter("/");
        request.SetMaxKeys(maxKeys.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var result = _client.GetBucket(request);
        var listBucket = result.listBucket;

        var objects = listBucket?.contentsList?
            .Select(item => new TencentCosObjectSummary(
                item.key,
                item.size,
                TryParseDate(item.lastModified),
                item.eTag))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToArray() ?? [];

        var commonPrefixes = listBucket?.commonPrefixesList?
            .Select(item => item.prefix)
            .Where(prefixItem => !string.IsNullOrWhiteSpace(prefixItem))
            .ToArray() ?? [];

        return new TencentCosObjectListing(
            objects,
            commonPrefixes,
            listBucket is { isTruncated: true } && !string.IsNullOrWhiteSpace(listBucket.nextMarker)
                ? listBucket.nextMarker
                : null);
    }

    public void DeleteObject(string bucketName, string key)
    {
        _client.DeleteObject(new DeleteObjectRequest(bucketName, key));
    }

    public void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey)
    {
        var request = new CopyObjectRequest(destinationBucketName, destinationKey);
        request.SetCopySource(new COSXML.Model.Tag.CopySourceStruct(
            ExtractAppId(sourceBucketName),
            StripAppId(sourceBucketName),
            _region,
            sourceKey));
        _client.CopyObject(request);
    }

    public void PutObject(string bucketName, string key, Stream content, Action<long, long>? progress = null)
    {
        var request = new PutObjectRequest(bucketName, key, content);
        if (progress is not null)
        {
            request.SetCosProgressCallback((completed, total) => progress(completed, total));
        }

        _client.PutObject(request);
    }

    public void PutObjectMultipart(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null)
    {
        var initResult = _client.InitMultipartUpload(new InitMultipartUploadRequest(bucketName, key));
        var uploadId = initResult.initMultipartUpload?.uploadId;
        if (string.IsNullOrWhiteSpace(uploadId))
        {
            throw new InvalidOperationException("COS multipart upload id was empty.");
        }

        var parts = new Dictionary<int, string>();
        var transferred = 0L;
        var partNumber = 1;
        try
        {
            foreach (var part in ReadParts(content, ObjectStorageUploadPolicy.PartSize))
            {
                var request = new UploadPartRequest(bucketName, key, partNumber, uploadId, part);
                request.SetCosProgressCallback((completed, _) =>
                    progress?.Invoke(Math.Min(contentLength, transferred + completed), contentLength));
                var result = _client.UploadPart(request);
                if (string.IsNullOrWhiteSpace(result.eTag))
                {
                    throw new InvalidOperationException("COS multipart part etag was empty.");
                }

                parts[partNumber] = result.eTag;
                transferred += part.LongLength;
                progress?.Invoke(Math.Min(contentLength, transferred), contentLength);
                partNumber++;
            }

            var complete = new CompleteMultipartUploadRequest(bucketName, key, uploadId);
            foreach (var part in parts)
            {
                complete.SetPartNumberAndETag(part.Key, part.Value);
            }

            _client.CompleteMultiUpload(complete);
            progress?.Invoke(contentLength, contentLength);
        }
        catch
        {
            _client.AbortMultiUpload(new AbortMultipartUploadRequest(bucketName, key, uploadId));
            throw;
        }
    }

    public TencentCosDownloadObject GetObject(string bucketName, string key)
    {
        var result = _client.GetObject(new GetObjectBytesRequest(bucketName, key));
        var content = result.content ?? [];
        return new TencentCosDownloadObject(new MemoryStream(content, writable: false), content.LongLength);
    }

    private static DateTimeOffset? TryParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string ExtractAppId(string bucketName)
    {
        var index = bucketName.LastIndexOf("-", StringComparison.Ordinal);
        return index >= 0 && index < bucketName.Length - 1 ? bucketName[(index + 1)..] : string.Empty;
    }

    private static string StripAppId(string bucketName)
    {
        var index = bucketName.LastIndexOf("-", StringComparison.Ordinal);
        return index > 0 ? bucketName[..index] : bucketName;
    }

    private static IEnumerable<byte[]> ReadParts(Stream content, long partSize)
    {
        var buffer = new byte[partSize];
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
                buffer = new byte[partSize];
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
