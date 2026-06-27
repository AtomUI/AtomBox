namespace AtomBox.Providers.ObjectStorage.Upyun;

internal interface IUpyunClient : IDisposable
{
    IReadOnlyList<UpyunBucket> ListBuckets();

    UpyunObjectListing ListObjects(string bucketName, string prefix, string? cursor = null, int maxKeys = 1000);

    void DeleteObject(string bucketName, string key);

    void CopyObject(string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey);

    void PutObject(string bucketName, string key, Stream content);

    void PutObjectMultipart(string bucketName, string key, Stream content, long contentLength, Action<long, long>? progress = null);

    UpyunDownloadObject GetObject(string bucketName, string key);
}
