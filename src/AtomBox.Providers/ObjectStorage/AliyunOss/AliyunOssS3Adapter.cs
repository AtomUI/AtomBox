using AtomBox.Providers.ObjectStorage.S3Compatible;

namespace AtomBox.Providers.ObjectStorage.AliyunOss;

internal sealed class AliyunOssS3Adapter : IAsyncS3CompatibleClient
{
    private readonly IAsyncS3CompatibleClient _s3Client;
    private readonly AliyunOssNativeServiceClient _nativeClient;

    public AliyunOssS3Adapter(IAsyncS3CompatibleClient s3Client, AliyunOssNativeServiceClient nativeClient)
    {
        _s3Client = s3Client;
        _nativeClient = nativeClient;
    }

    public Task<IReadOnlyList<S3CompatibleBucket>> ListBucketsAsync(CancellationToken cancellationToken = default) =>
        _nativeClient.ListBucketsAsync(cancellationToken);

    public Task CreateBucketAsync(string bucketName, CancellationToken cancellationToken = default) =>
        _s3Client.CreateBucketAsync(bucketName, cancellationToken);

    public Task<S3CompatibleObjectListing> ListObjectsAsync(
        string bucketName, string prefix, string? cursor = null, int maxKeys = 1000,
        CancellationToken cancellationToken = default) =>
        _s3Client.ListObjectsAsync(bucketName, prefix, cursor, maxKeys, cancellationToken);

    public Task DeleteObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default) =>
        _s3Client.DeleteObjectAsync(bucketName, key, cancellationToken);

    public Task CopyObjectAsync(
        string sourceBucketName, string sourceKey, string destinationBucketName, string destinationKey,
        CancellationToken cancellationToken = default) =>
        _s3Client.CopyObjectAsync(sourceBucketName, sourceKey, destinationBucketName, destinationKey, cancellationToken);

    public Task PutObjectAsync(string bucketName, string key, Stream content, CancellationToken cancellationToken = default) =>
        _s3Client.PutObjectAsync(bucketName, key, content, cancellationToken);

    public Task PutObjectMultipartAsync(
        string bucketName, string key, Stream content, long contentLength, Action<long, long>? progress = null,
        CancellationToken cancellationToken = default) =>
        _s3Client.PutObjectMultipartAsync(bucketName, key, content, contentLength, progress, cancellationToken);

    public Task<S3CompatibleDownloadObject> GetObjectAsync(
        string bucketName, string key, CancellationToken cancellationToken = default) =>
        _s3Client.GetObjectAsync(bucketName, key, cancellationToken);

    public void Dispose()
    {
        _nativeClient.Dispose();
        _s3Client.Dispose();
    }
}
