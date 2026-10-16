namespace AtomBox.Providers.ObjectStorage.S3Compatible;

internal sealed class SynchronousS3CompatibleClientAdapter : IAsyncS3CompatibleClient
{
    private readonly IS3CompatibleClient _client;

    public SynchronousS3CompatibleClientAdapter(IS3CompatibleClient client)
    {
        _client = client;
    }

    public Task<IReadOnlyList<S3CompatibleBucket>> ListBucketsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_client.ListBuckets());
    }

    public Task CreateBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _client.CreateBucket(bucketName);
        return Task.CompletedTask;
    }

    public Task<S3CompatibleObjectListing> ListObjectsAsync(
        string bucketName,
        string prefix,
        string? cursor = null,
        int maxKeys = 1000,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_client.ListObjects(bucketName, prefix, cursor, maxKeys));
    }

    public Task DeleteObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _client.DeleteObject(bucketName, key);
        return Task.CompletedTask;
    }

    public Task CopyObjectAsync(
        string sourceBucketName,
        string sourceKey,
        string destinationBucketName,
        string destinationKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _client.CopyObject(sourceBucketName, sourceKey, destinationBucketName, destinationKey);
        return Task.CompletedTask;
    }

    public Task PutObjectAsync(
        string bucketName,
        string key,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _client.PutObject(bucketName, key, content);
        return Task.CompletedTask;
    }

    public Task PutObjectMultipartAsync(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _client.PutObjectMultipart(bucketName, key, content, contentLength, progress);
        return Task.CompletedTask;
    }

    public Task<S3CompatibleDownloadObject> GetObjectAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_client.GetObject(bucketName, key));
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
