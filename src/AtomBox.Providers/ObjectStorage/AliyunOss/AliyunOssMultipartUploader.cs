using Aliyun.OSS;
using AtomBox.Providers.ObjectStorage;

namespace AtomBox.Providers.ObjectStorage.AliyunOss;

internal sealed class AliyunOssMultipartUploader
{
    private const int MaximumPartCount = 10_000;
    private const long MaximumPartSize = 5L * 1024L * 1024L * 1024L;

    private readonly IAliyunOssMultipartOperations _operations;

    public AliyunOssMultipartUploader(IAliyunOssMultipartOperations operations)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public void Upload(
        string bucketName,
        string key,
        Stream content,
        long contentLength,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanSeek)
        {
            throw new ArgumentException(
                "Aliyun OSS multipart upload requires a seekable content stream.",
                nameof(content));
        }

        var partSize = GetPartSize(contentLength);
        string? uploadId = null;
        var uploadCompleted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            uploadId = _operations.Initiate(bucketName, key);
            cancellationToken.ThrowIfCancellationRequested();

            var parts = new List<PartETag>();
            long completedBytes = 0;
            var partNumber = 1;
            while (completedBytes < contentLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var currentPartSize = Math.Min(partSize, contentLength - completedBytes);
                var completedBeforePart = completedBytes;
                var part = _operations.UploadPart(
                    bucketName,
                    key,
                    uploadId,
                    partNumber,
                    currentPartSize,
                    content,
                    (_, args) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Invoke(
                            Math.Min(contentLength, completedBeforePart + args.TransferredBytes),
                            contentLength);
                    });

                cancellationToken.ThrowIfCancellationRequested();
                parts.Add(part);
                completedBytes += currentPartSize;
                partNumber++;
                progress?.Invoke(completedBytes, contentLength);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _operations.Complete(bucketName, key, uploadId, parts);
            uploadCompleted = true;
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            if (!uploadCompleted && !string.IsNullOrWhiteSpace(uploadId))
            {
                TryAbort(bucketName, key, uploadId);
            }

            throw;
        }
    }

    private static long GetPartSize(long contentLength)
    {
        if (contentLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentLength),
                "Multipart content length must be positive.");
        }

        var minimumPartSize = contentLength / MaximumPartCount;
        if (contentLength % MaximumPartCount != 0)
        {
            minimumPartSize++;
        }
        var partSize = Math.Max(ObjectStorageUploadPolicy.PartSize, minimumPartSize);
        if (partSize > MaximumPartSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentLength),
                "Aliyun OSS multipart upload exceeds the supported object size.");
        }

        return partSize;
    }

    private void TryAbort(string bucketName, string key, string uploadId)
    {
        try
        {
            _operations.Abort(bucketName, key, uploadId);
        }
        catch
        {
            // Preserve the original upload or cancellation failure. Abort is best-effort cleanup.
        }
    }
}
