namespace AtomBox.Providers.ObjectStorage.AliyunOss;

internal sealed class AliyunOssNativeException : Exception
{
    public AliyunOssNativeException(int statusCode, string? errorCode, string? requestId)
        : base("Aliyun OSS native service request failed.")
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        RequestId = requestId;
    }

    public int StatusCode { get; }

    public string? ErrorCode { get; }

    public string? RequestId { get; }
}
