namespace AtomBox.Providers.ObjectStorage.S3Compatible;

internal interface ICancellableS3TransferClient
{
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
