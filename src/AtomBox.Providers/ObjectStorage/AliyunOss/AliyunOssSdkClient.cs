using Aliyun.OSS;
using AtomBox.Providers.ObjectStorage;

namespace AtomBox.Providers.ObjectStorage.AliyunOss;

internal sealed class AliyunOssSdkClient : IAliyunOssClient
{
    private readonly IOss _client;

    public AliyunOssSdkClient(IOss client)
    {
        _client = client;
    }

    public IReadOnlyList<AliyunOssBucket> ListBuckets()
    {
        return _client.ListBuckets()
            .Select(bucket => new AliyunOssBucket(
                bucket.Name,
                bucket.CreationDate == default ? null : new DateTimeOffset(bucket.CreationDate)))
            .ToArray();
    }

    public void CreateBucket(string bucketName)
    {
        _client.CreateBucket(bucketName);
    }

    public AliyunOssObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000)
    {
        var request = new ListObjectsRequest(bucketName)
        {
            Prefix = prefix,
            Delimiter = "/",
            MaxKeys = maxKeys
        };
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            request.Marker = cursor;
        }

        var listing = _client.ListObjects(request);
        var objects = listing.ObjectSummaries
            .Select(item => new AliyunOssObjectSummary(
                item.Key,
                item.Size,
                item.LastModified == default ? null : new DateTimeOffset(item.LastModified),
                item.ETag,
                ContentType: null))
            .ToArray();

        var commonPrefixes = listing.CommonPrefixes.ToArray();
        return new AliyunOssObjectListing(
            objects,
            commonPrefixes,
            listing.IsTruncated && !string.IsNullOrWhiteSpace(listing.NextMarker) ? listing.NextMarker : null);
    }

    public void DeleteObject(string bucketName, string key)
    {
        _client.DeleteObject(bucketName, key);
    }

    public void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey)
    {
        _client.CopyObject(new CopyObjectRequest(sourceBucketName, sourceKey, destinationBucketName, destinationKey));
    }

    public void PutObject(string bucketName, string key, Stream content)
    {
        _client.PutObject(bucketName, key, content);
    }

    public void PutObjectMultipart(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null)
    {
        var checkpointDir = Path.Combine(Path.GetTempPath(), "AtomBox", "aliyun-oss-checkpoints");
        Directory.CreateDirectory(checkpointDir);
        var request = new UploadObjectRequest(bucketName, key, content)
        {
            PartSize = ObjectStorageUploadPolicy.PartSize,
            CheckpointDir = checkpointDir,
            StreamTransferProgress = (_, args) => progress?.Invoke(args.TransferredBytes, args.TotalBytes)
        };

        _client.ResumableUploadObject(request);
        progress?.Invoke(contentLength, contentLength);
    }

    public AliyunOssDownloadObject GetObject(string bucketName, string key)
    {
        var ossObject = _client.GetObject(bucketName, key);
        return new AliyunOssDownloadObject(
            ossObject.Content,
            ossObject.ContentLength,
            ossObject);
    }
}
