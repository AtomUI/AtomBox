using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.Common;

public sealed class StorageProviderRegistry : IStorageProviderRegistry
{
    public static readonly StorageProviderId AliyunOssProviderId = new("aliyun-oss");
    public static readonly StorageProviderId TencentCosProviderId = new("tencent-cos");
    public static readonly StorageProviderId QiniuKodoProviderId = new("qiniu-kodo");
    public static readonly StorageProviderId UpyunProviderId = new("upyun");
    public static readonly StorageProviderId HuaweiObsProviderId = new("huawei-obs");
    public static readonly StorageProviderId BaiduBosProviderId = new("baidu-bos");
    public static readonly StorageProviderId JdCloudOssProviderId = new("jdcloud-oss");
    public static readonly StorageProviderId QingStorProviderId = new("qingstor");
    public static readonly StorageProviderId VolcengineTosProviderId = new("volcengine-tos");
    public static readonly StorageProviderId FtpProviderId = new("ftp");
    public static readonly StorageProviderId SftpProviderId = new("sftp");
    public static readonly StorageProviderId WebDavProviderId = new("webdav");
    public static readonly StorageProviderId AliyunDriveProviderId = new("aliyun-drive");
    public static readonly StorageProviderId BaiduNetDiskProviderId = new("baidu-netdisk");

    private readonly IReadOnlyList<ProviderDescriptor> _descriptorsInRegistrationOrder;
    private readonly IReadOnlyDictionary<StorageProviderId, ProviderDescriptor> _descriptors;

    public StorageProviderRegistry()
        : this(CreateDefaultDescriptors())
    {
    }

    public StorageProviderRegistry(IReadOnlyList<ProviderDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var duplicate = descriptors
            .GroupBy(item => item.Id)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate storage provider id: {duplicate.Key}");
        }

        _descriptorsInRegistrationOrder = descriptors;
        _descriptors = descriptors.ToDictionary(item => item.Id);
    }

    public static IReadOnlyList<ProviderDescriptor> CreateDefaultDescriptors()
    {
        return
        [
            CreateObjectStorage(AliyunOssProviderId, "阿里云 OSS", supportsObjectMove: true, supportsMultipartUpload: true),
            CreateObjectStorage(TencentCosProviderId, "腾讯 COS", supportsObjectMove: true, supportsMultipartUpload: true),
            CreateObjectStorage(QiniuKodoProviderId, "七牛 Kodo", supportsObjectMove: true, supportsMultipartUpload: true),
            CreateObjectStorage(UpyunProviderId, "又拍云 USS", supportsObjectMove: true, supportsMultipartUpload: true),
            CreateObjectStorage(HuaweiObsProviderId, "华为云 OBS", supportsObjectMove: true, supportsMultipartUpload: true),
            CreateObjectStorage(BaiduBosProviderId, "百度智能云 BOS", supportsObjectMove: true, supportsMultipartUpload: true),
            CreateObjectStorage(JdCloudOssProviderId, "京东云 OSS", supportsObjectMove: true, supportsMultipartUpload: true),
            CreateObjectStorage(QingStorProviderId, "青云 QingStor", supportsObjectMove: true, supportsMultipartUpload: true),
            CreateObjectStorage(VolcengineTosProviderId, "火山引擎 TOS", supportsObjectMove: true, supportsMultipartUpload: true),
            CreateFileTransfer(FtpProviderId, "FTP"),
            CreateFileTransfer(SftpProviderId, "SFTP"),
            CreateFileTransfer(WebDavProviderId, "WebDAV"),
            CreateAliyunDrive(),
            CreateBaiduNetDisk()
        ];
    }

    public IReadOnlyList<ProviderDescriptor> GetAll()
    {
        return _descriptorsInRegistrationOrder;
    }

    public OperationResult<ProviderDescriptor> GetById(StorageProviderId providerId)
    {
        return _descriptors.TryGetValue(providerId, out var descriptor)
            ? OperationResult<ProviderDescriptor>.Success(descriptor)
            : OperationResult<ProviderDescriptor>.Failure(Core.Errors.StorageError.NotFound("Storage provider was not found."));
    }

    private static ProviderDescriptor CreateObjectStorage(
        StorageProviderId id,
        string displayName,
        bool supportsObjectMove = false,
        bool supportsMultipartUpload = false)
    {
        var capabilities = StorageCapability.List |
            StorageCapability.Upload |
            StorageCapability.Download |
            StorageCapability.Delete;
        if (supportsObjectMove)
        {
            capabilities |= StorageCapability.CreateFolder |
                StorageCapability.Rename |
                StorageCapability.Move;
        }
        if (supportsMultipartUpload)
        {
            capabilities |= StorageCapability.MultipartUpload;
        }

        return new ProviderDescriptor(
            id,
            StorageProviderCategory.ObjectStorage,
            displayName,
            "对象存储 provider 类型。",
            new StorageCapabilitySet(capabilities),
            [
                new ProviderConfigFieldDescriptor("endpoint", "Endpoint", ProviderConfigFieldKind.Endpoint, true),
                new ProviderConfigFieldDescriptor("region", "Region", ProviderConfigFieldKind.Region, false),
                new ProviderConfigFieldDescriptor("bucket", "Bucket", ProviderConfigFieldKind.Bucket, false)
            ]);
    }

    private static ProviderDescriptor CreateFileTransfer(StorageProviderId id, string displayName)
    {
        var fields = new List<ProviderConfigFieldDescriptor>
        {
            new("endpoint", "Host", ProviderConfigFieldKind.Endpoint, true),
            new("port", "Port", ProviderConfigFieldKind.Text, false),
            new("authMode", "Authentication", ProviderConfigFieldKind.Text, false),
            new("rootPath", "Root Path", ProviderConfigFieldKind.Text, false)
        };

        if (id == SftpProviderId)
        {
            fields.Add(new ProviderConfigFieldDescriptor("homePath", "Home", ProviderConfigFieldKind.Text, false));
            fields.Add(new ProviderConfigFieldDescriptor("hostKeyPolicy", "Host Key Policy", ProviderConfigFieldKind.Text, false));
            fields.Add(new ProviderConfigFieldDescriptor("hostKeyFingerprint", "Host Key Fingerprint", ProviderConfigFieldKind.Text, false));
            fields.Add(new ProviderConfigFieldDescriptor("timeoutSeconds", "Timeout Seconds", ProviderConfigFieldKind.Text, false));
        }

        if (id == FtpProviderId)
        {
            fields.Add(new ProviderConfigFieldDescriptor("transferMode", "Transfer Mode", ProviderConfigFieldKind.Text, false));
            fields.Add(new ProviderConfigFieldDescriptor("timeoutSeconds", "Timeout Seconds", ProviderConfigFieldKind.Text, false));
        }

        if (id == WebDavProviderId)
        {
            fields.Add(new ProviderConfigFieldDescriptor("timeoutSeconds", "Timeout Seconds", ProviderConfigFieldKind.Text, false));
        }

        return new ProviderDescriptor(
            id,
            StorageProviderCategory.FileTransfer,
            displayName,
            "文件传输 provider 类型。",
            new StorageCapabilitySet(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download | StorageCapability.Delete | StorageCapability.CreateFolder | StorageCapability.Rename | StorageCapability.Move | (id == WebDavProviderId ? StorageCapability.Search : StorageCapability.None)),
            fields);
    }

    private static ProviderDescriptor CreateNetDisk(StorageProviderId id, string displayName)
    {
        return new ProviderDescriptor(
            id,
            StorageProviderCategory.NetDisk,
            displayName,
            "网盘 API provider 类型。",
            new StorageCapabilitySet(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download | StorageCapability.Delete),
            []);
    }

    private static ProviderDescriptor CreateAliyunDrive()
    {
        return new ProviderDescriptor(
            AliyunDriveProviderId,
            StorageProviderCategory.NetDisk,
            "阿里云盘",
            "网盘 API provider 类型。",
            new StorageCapabilitySet(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download | StorageCapability.Delete),
            [
                new ProviderConfigFieldDescriptor("endpoint", "Endpoint", ProviderConfigFieldKind.Endpoint, false),
                new ProviderConfigFieldDescriptor("driveId", "Drive ID", ProviderConfigFieldKind.Text, true),
                new ProviderConfigFieldDescriptor("rootFileId", "Root File ID", ProviderConfigFieldKind.Text, false)
            ]);
    }

    private static ProviderDescriptor CreateBaiduNetDisk()
    {
        return new ProviderDescriptor(
            BaiduNetDiskProviderId,
            StorageProviderCategory.NetDisk,
            "百度网盘",
            "网盘 API provider 类型。",
            new StorageCapabilitySet(StorageCapability.List | StorageCapability.Upload | StorageCapability.Download | StorageCapability.Delete),
            [
                new ProviderConfigFieldDescriptor("endpoint", "Endpoint", ProviderConfigFieldKind.Endpoint, false),
                new ProviderConfigFieldDescriptor("contentEndpoint", "Content Endpoint", ProviderConfigFieldKind.Endpoint, false),
                new ProviderConfigFieldDescriptor("rootPath", "Root Path", ProviderConfigFieldKind.Text, false)
            ]);
    }
}
