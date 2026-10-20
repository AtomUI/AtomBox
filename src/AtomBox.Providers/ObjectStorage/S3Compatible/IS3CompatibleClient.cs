namespace AtomBox.Providers.ObjectStorage.S3Compatible;

internal interface IS3CompatibleClient : IDisposable
{
    IReadOnlyList<S3CompatibleBucket> ListBuckets();

    void CreateBucket(string bucketName);

    S3CompatibleObjectListing ListObjects(
        string bucketName,
        string prefix,
        string? cursor = null,
        int maxKeys = 1000);

    void DeleteObject(string bucketName, string key);

    void CopyObject(
        string sourceBucketName,
        string sourceKey,
        string destinationBucketName,
        string destinationKey);

    void PutObject(string bucketName, string key, Stream content);

    void PutObjectMultipart(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null);

    S3CompatibleDownloadObject GetObject(string bucketName, string key);
}

internal interface IAsyncS3CompatibleClient : IDisposable
{
    Task<IReadOnlyList<S3CompatibleBucket>> ListBucketsAsync(
        CancellationToken cancellationToken = default);

    Task CreateBucketAsync(
        string bucketName,
        CancellationToken cancellationToken = default);

    Task<S3CompatibleObjectListing> ListObjectsAsync(
        string bucketName,
        string prefix,
        string? cursor = null,
        int maxKeys = 1000,
        CancellationToken cancellationToken = default);

    Task DeleteObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default);

    Task CopyObjectAsync(
        string sourceBucketName,
        string sourceKey,
        string destinationBucketName,
        string destinationKey,
        CancellationToken cancellationToken = default);

    Task PutObjectAsync(
        string bucketName,
        string key,
        Stream content,
        CancellationToken cancellationToken = default);

    Task PutObjectMultipartAsync(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default);

    Task<S3CompatibleDownloadObject> GetObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default);
}
