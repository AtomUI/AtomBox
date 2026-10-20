using Aliyun.OSS;

namespace AtomBox.Providers.ObjectStorage.AliyunOss;

internal sealed class AliyunOssSdkMultipartOperations : IAliyunOssMultipartOperations
{
    private readonly IOss _client;

    public AliyunOssSdkMultipartOperations(IOss client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public string Initiate(string bucketName, string key)
    {
        return _client.InitiateMultipartUpload(
            new InitiateMultipartUploadRequest(bucketName, key)).UploadId;
    }

    public PartETag UploadPart(
        string bucketName,
        string key,
        string uploadId,
        int partNumber,
        long partSize,
        Stream content,
        EventHandler<StreamTransferProgressArgs>? progress)
    {
        var request = new UploadPartRequest(bucketName, key, uploadId)
        {
            InputStream = content,
            PartNumber = partNumber,
            PartSize = partSize,
            StreamTransferProgress = progress
        };

        return _client.UploadPart(request).PartETag;
    }

    public void Complete(
        string bucketName,
        string key,
        string uploadId,
        IReadOnlyList<PartETag> parts)
    {
        var request = new CompleteMultipartUploadRequest(bucketName, key, uploadId);
        foreach (var part in parts)
        {
            request.PartETags.Add(part);
        }

        _client.CompleteMultipartUpload(request);
    }

    public void Abort(string bucketName, string key, string uploadId)
    {
        _client.AbortMultipartUpload(new AbortMultipartUploadRequest(bucketName, key, uploadId));
    }
}
