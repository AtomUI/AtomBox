namespace AtomBox.Providers.NetDisk.AliyunDrive;

internal sealed class AliyunDriveApiException : Exception
{
    public AliyunDriveApiException(int statusCode, string? providerErrorCode)
        : base("Aliyun Drive API request failed.")
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public int StatusCode { get; }

    public string? ProviderErrorCode { get; }
}
