namespace AtomBox.Providers.ObjectStorage.QiniuKodo;

internal interface IQiniuKodoClient : IDisposable
{
    IReadOnlyList<QiniuKodoBucket> ListBuckets();

    QiniuKodoObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000);

    void DeleteObject(string bucketName, string key);

    void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey);

    void PutObject(string bucketName, string key, Stream content, Action<long, long>? progress = null);

    void PutObjectMultipart(string bucketName, string key, Stream content, long contentLength, Action<long, long>? progress = null);

    Task<QiniuKodoDownloadObject> GetObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default);
}
