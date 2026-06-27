namespace AtomBox.Providers.NetDisk.BaiduNetDisk;

internal sealed class BaiduNetDiskApiException : Exception
{
    public BaiduNetDiskApiException(int errno)
        : base("Baidu Netdisk API request failed.")
    {
        Errno = errno;
    }

    public int Errno { get; }
}
