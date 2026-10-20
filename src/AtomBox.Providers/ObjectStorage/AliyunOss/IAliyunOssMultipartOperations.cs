using Aliyun.OSS;

namespace AtomBox.Providers.ObjectStorage.AliyunOss;

internal interface IAliyunOssMultipartOperations
{
    string Initiate(string bucketName, string key);

    PartETag UploadPart(
        string bucketName,
        string key,
        string uploadId,
        int partNumber,
        long partSize,
        Stream content,
        EventHandler<StreamTransferProgressArgs>? progress);

    void Complete(
        string bucketName,
        string key,
        string uploadId,
        IReadOnlyList<PartETag> parts);

    void Abort(string bucketName, string key, string uploadId);
}
