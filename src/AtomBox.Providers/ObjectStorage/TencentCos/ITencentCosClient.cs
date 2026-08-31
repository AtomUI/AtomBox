namespace AtomBox.Providers.ObjectStorage.TencentCos;

internal interface ITencentCosClient
{
    IReadOnlyList<TencentCosBucket> ListBuckets();

    void CreateBucket(string bucketName);

    TencentCosObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000);

    void DeleteObject(string bucketName, string key);

    void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey);

    void PutObject(string bucketName, string key, Stream content, Action<long, long>? progress = null);

    Task PutObjectAsync(
        string bucketName,
        string key,
        Stream content,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default);

    void PutObjectMultipart(string bucketName, string key, Stream content, long contentLength, Action<long, long>? progress = null);

    Task PutObjectMultipartAsync(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default);

    TencentCosDownloadObject GetObject(string bucketName, string key);

    Task<TencentCosDownloadObject> GetObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default);
}
