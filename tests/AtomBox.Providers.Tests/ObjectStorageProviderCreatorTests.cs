using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.AliyunOss;
using AtomBox.Providers.ObjectStorage.BaiduBos;
using AtomBox.Providers.ObjectStorage.HuaweiObs;
using AtomBox.Providers.ObjectStorage.JdCloudOss;
using AtomBox.Providers.ObjectStorage.QiniuKodo;
using AtomBox.Providers.ObjectStorage.S3Compatible;
using AtomBox.Providers.ObjectStorage.TencentCos;
using AtomBox.Providers.ObjectStorage.Upyun;
using AtomBox.Providers.ObjectStorage.VolcengineTos;

namespace AtomBox.Providers.Tests;

public sealed class ObjectStorageProviderCreatorTests
{
    [Theory]
    [InlineData("aliyun-oss", "oss-cn-hangzhou.aliyuncs.com", "cn-hangzhou", null)]
    [InlineData("tencent-cos", "https://cos.ap-shanghai.myqcloud.com", "ap-shanghai", null)]
    [InlineData("qiniu-kodo", null, "z0", "qiniu-bucket")]
    [InlineData("upyun", null, null, "upyun-service")]
    [InlineData("huawei-obs", "https://obs.cn-north-4.myhuaweicloud.com", null, null)]
    [InlineData("baidu-bos", "https://bj.bcebos.com", "bj", null)]
    [InlineData("jdcloud-oss", "https://s3.cn-north-1.jdcloud-oss.com", "cn-north-1", null)]
    [InlineData("volcengine-tos", "https://tos-cn-beijing.volces.com", "cn-beijing", null)]
    public async Task MigratedCreator_BuildsAsyncCancellationAwareSharedProvider(
        string providerId,
        string? endpoint,
        string? region,
        string? bucket)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id.Value == providerId);
        var resolver = new ProviderCredentialResolver(new FixedCredentialStore());
        var creator = CreateCreator(providerId, descriptor, resolver);
        var now = DateTimeOffset.UtcNow;
        var providerConfig = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(bucket))
        {
            providerConfig["bucket"] = bucket;
        }

        if (providerId == "upyun")
        {
            providerConfig["credentialMode"] = "s3";
        }

        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            descriptor.Id,
            $"{providerId} test",
            endpoint,
            region,
            new CredentialRef("object-storage-test"),
            now,
            now,
            providerConfig);

        var result = await creator.CreateAsync(account);

        Assert.True(result.IsSuccess, result.Error?.Message);
        await using var provider = Assert.IsType<S3CompatibleProvider>(result.GetValueOrThrow());
        Assert.Equal(TransferIoExecutionKind.Asynchronous, provider.TransferCapabilities.UploadExecution);
        Assert.Equal(TransferIoExecutionKind.Asynchronous, provider.TransferCapabilities.DownloadExecution);
        Assert.True(provider.TransferCapabilities.SupportsCooperativeCancellation);
        Assert.False(provider.TransferCapabilities.SupportsActiveAbort);
        Assert.Equal(providerId != "upyun", provider.Capabilities.Supports(StorageCapability.CreateBucket));
        Assert.True(provider.Capabilities.Supports(StorageCapability.CreateFolder));

        if (providerId == "tencent-cos")
        {
            var invalidBucket = await provider.CreateFolderAsync(new RemotePath("missing-appid", RemotePathKind.BucketRoot));
            Assert.Equal(StorageErrorCode.ValidationFailed, invalidBucket.Error?.Code);
        }
    }

    [Fact]
    public async Task UpyunCreator_RejectsLegacyOperatorCredentialsBeforeCredentialAcquisition()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.UpyunProviderId);
        var store = new FixedCredentialStore();
        var creator = new UpyunProviderCreator(descriptor, new ProviderCredentialResolver(store));
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            descriptor.Id,
            "legacy upyun",
            "https://v0.api.upyun.com",
            null,
            new CredentialRef("legacy-upyun"),
            now,
            now,
            new Dictionary<string, string> { ["bucket"] = "service-a" });

        var result = await creator.CreateAsync(account);

        Assert.True(result.IsFailure);
        Assert.Contains("S3 AccessKey", result.Error?.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.AcquireMaterialCalls);
    }

    private static IStorageProviderCreator CreateCreator(
        string providerId,
        ProviderDescriptor descriptor,
        ProviderCredentialResolver resolver)
    {
        return providerId switch
        {
            "aliyun-oss" => new AliyunOssProviderCreator(descriptor, resolver),
            "tencent-cos" => new TencentCosProviderCreator(descriptor, resolver),
            "qiniu-kodo" => new QiniuKodoProviderCreator(descriptor, resolver),
            "upyun" => new UpyunProviderCreator(descriptor, resolver),
            "huawei-obs" => new HuaweiObsProviderCreator(descriptor, resolver),
            "baidu-bos" => new BaiduBosProviderCreator(descriptor, resolver),
            "jdcloud-oss" => new JdCloudOssProviderCreator(descriptor, resolver),
            "volcengine-tos" => new VolcengineTosProviderCreator(descriptor, resolver),
            _ => throw new ArgumentOutOfRangeException(nameof(providerId), providerId, null)
        };
    }

    private sealed class FixedCredentialStore : ICredentialStore
    {
        public int AcquireMaterialCalls { get; private set; }

        public Task<OperationResult<CredentialRef>> SaveAsync(
            CredentialSecretMaterial material,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<CredentialRef>.Success(new CredentialRef("object-storage-test")));

        public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<CredentialLease>.Success(new FixedCredentialLease(credentialRef)));

        public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            AcquireMaterialCalls++;
            return Task.FromResult(OperationResult<CredentialMaterialLease>.Success(
                new CredentialMaterialLease(
                    new FixedCredentialLease(credentialRef),
                    new CredentialSecretMaterial(new Dictionary<string, string>
                    {
                        ["accessKeyId"] = "access",
                        ["accessKeySecret"] = "secret"
                    }))));
        }

        public Task<OperationResult<bool>> ExistsAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<bool>.Success(true));

        public Task<OperationResult> MarkPendingDeleteAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());
    }

    private sealed class FixedCredentialLease : CredentialLease
    {
        public FixedCredentialLease(CredentialRef credentialRef)
            : base(credentialRef, "object-storage-creator-test")
        {
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
