namespace AtomBox.Providers.ObjectStorage.S3Compatible;

internal interface IS3CompatibleClient : IDisposable
{
    IReadOnlyList<S3CompatibleBucket> ListBuckets();

    void CreateBucket(string bucketName);

    S3CompatibleObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000);

    void DeleteObject(string bucketName, string key);

    void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey);

    void PutObject(string bucketName, string key, Stream content);

    void PutObjectMultipart(string bucketName, string key, Stream content, long contentLength, Action<long, long>? progress = null);

    S3CompatibleDownloadObject GetObject(string bucketName, string key);
}
