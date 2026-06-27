using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.DependencyInjection;
using AtomBox.Providers.FileTransfer.Ftp;
using AtomBox.Providers.FileTransfer.Sftp;
using AtomBox.Providers.FileTransfer.WebDav;
using AtomBox.Providers.NetDisk.AliyunDrive;
using AtomBox.Providers.NetDisk.BaiduNetDisk;
using AtomBox.Providers.ObjectStorage.AliyunOss;
using AtomBox.Providers.ObjectStorage.BaiduBos;
using AtomBox.Providers.ObjectStorage.HuaweiObs;
using AtomBox.Providers.ObjectStorage.JdCloudOss;
using AtomBox.Providers.ObjectStorage.QiniuKodo;
using AtomBox.Providers.ObjectStorage.QingStor;
using AtomBox.Providers.ObjectStorage.TencentCos;
using AtomBox.Providers.ObjectStorage.Upyun;
using AtomBox.Providers.ObjectStorage.VolcengineTos;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Providers.Tests;

public sealed class ProviderRegistryAndFactoryTests
{
    [Fact]
    public void Registry_ReturnsStableProviderTypeMetadata()
    {
        var registry = new StorageProviderRegistry();

        var descriptors = registry.GetAll();

        Assert.Equal(
            ["aliyun-oss", "tencent-cos", "qiniu-kodo", "upyun", "huawei-obs", "baidu-bos", "jdcloud-oss", "qingstor", "volcengine-tos", "ftp", "sftp", "webdav", "aliyun-drive", "baidu-netdisk"],
            descriptors.Select(item => item.Id.Value).ToArray());
        Assert.All(descriptors, descriptor => Assert.NotEmpty(descriptor.DisplayName));
    }

    [Fact]
    public async Task Factory_RejectsCategoryMismatch()
    {
        var registry = new StorageProviderRegistry();
        var factory = CreateFactory(registry);
        var account = CreateAccount(StorageProviderCategory.FileTransfer, new StorageProviderId("aliyun-oss"));

        var result = await factory.CreateAsync(account);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task FakeObjectStorageProvider_ReturnsBucketsAtRoot()
    {
        var registry = new StorageProviderRegistry();
        var factory = CreateFactory(registry);
        var account = CreateAccount(StorageProviderCategory.ObjectStorage, new StorageProviderId("aliyun-oss"));

        var providerResult = await factory.CreateAsync(account);
        await using var provider = providerResult.GetValueOrThrow();
        var listResult = await provider.ListAsync(RemotePath.Root);

        Assert.True(listResult.IsSuccess);
        Assert.All(listResult.GetValueOrThrow(), item => Assert.Equal(RemoteItemKind.Bucket, item.Kind));
    }

    [Fact]
    public async Task FakeProvider_RejectsRootDeletion()
    {
        var registry = new StorageProviderRegistry();
        var factory = CreateFactory(registry);
        var account = CreateAccount(StorageProviderCategory.FileTransfer, new StorageProviderId("sftp"));

        var providerResult = await factory.CreateAsync(account);
        await using var provider = providerResult.GetValueOrThrow();
        var deleteResult = await provider.DeleteAsync(RemotePath.Root);

        Assert.True(deleteResult.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, deleteResult.Error?.Category);
    }

    [Fact]
    public async Task Factory_RejectsMissingProviderImplementation()
    {
        var registry = new StorageProviderRegistry();
        var factory = new StorageProviderFactory(registry, []);
        var account = CreateAccount(StorageProviderCategory.ObjectStorage, new StorageProviderId("aliyun-oss"));

        var result = await factory.CreateAsync(account);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public void Factory_RejectsDuplicateProviderCreators()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()[0];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new StorageProviderFactory(
                new StorageProviderRegistry([descriptor]),
                [
                    new FakeStorageProviderCreator(descriptor),
                    new FakeStorageProviderCreator(descriptor)
                ]));

        Assert.Contains("Duplicate storage provider creator", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Factory_RejectsMissingRequiredEndpoint()
    {
        var registry = new StorageProviderRegistry();
        var factory = CreateFactory(registry);
        var account = CreateAccount(StorageProviderCategory.ObjectStorage, new StorageProviderId("aliyun-oss"), endpoint: null);

        var result = await factory.CreateAsync(account);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task Factory_ValidatesRequiredProviderConfigFields()
    {
        var descriptor = new ProviderDescriptor(
            new StorageProviderId("custom-object-storage"),
            StorageProviderCategory.ObjectStorage,
            "Custom Object Storage",
            "Custom object storage provider.",
            AtomBox.Core.Capabilities.StorageCapabilitySet.Empty,
            [
                new ProviderConfigFieldDescriptor("endpoint", "Endpoint", ProviderConfigFieldKind.Endpoint, true),
                new ProviderConfigFieldDescriptor("bucket", "Bucket", ProviderConfigFieldKind.Bucket, true)
            ]);
        var registry = new StorageProviderRegistry([descriptor]);
        var factory = new StorageProviderFactory(registry, [new FakeStorageProviderCreator(descriptor)]);
        var missingBucket = CreateAccount(StorageProviderCategory.ObjectStorage, descriptor.Id);

        var rejected = await factory.CreateAsync(missingBucket);
        var accepted = await factory.CreateAsync(CreateAccount(
            StorageProviderCategory.ObjectStorage,
            descriptor.Id,
            providerConfig: new Dictionary<string, string> { ["bucket"] = "assets" }));

        Assert.True(rejected.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, rejected.Error?.Category);
        Assert.True(accepted.IsSuccess);
        await accepted.GetValueOrThrow().DisposeAsync();
    }

    [Fact]
    public async Task Factory_AllowsOptionalAliyunBucketConfig()
    {
        var registry = new StorageProviderRegistry();
        var factory = CreateFactory(registry);
        var account = CreateAccount(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            providerConfig: new Dictionary<string, string> { ["bucket"] = "assets" });

        var providerResult = await factory.CreateAsync(account);

        Assert.True(providerResult.IsSuccess);
        await providerResult.GetValueOrThrow().DisposeAsync();
    }

    [Fact]
    public async Task Factory_MapsCreatorExceptionsToStorageError()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()[0];
        var registry = new StorageProviderRegistry([descriptor]);
        var factory = new StorageProviderFactory(registry, [new ThrowingStorageProviderCreator(descriptor)]);
        var account = CreateAccount(StorageProviderCategory.ObjectStorage, descriptor.Id);

        var result = await factory.CreateAsync(account);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Unknown, result.Error?.Category);
    }

    [Fact]
    public async Task ProviderCredentialResolver_AcquiresCredentialMaterial()
    {
        var credentialRef = new CredentialRef("cred-1");
        var resolver = new ProviderCredentialResolver(new FakeCredentialStore(credentialRef));
        var account = CreateAccount(StorageProviderCategory.ObjectStorage, new StorageProviderId("aliyun-oss"));

        await using var materialLease = (await resolver.AcquireMaterialAsync(account)).GetValueOrThrow();

        Assert.Equal("ak-test", materialLease.Material.GetRequiredValue("accessKeyId"));
    }

    [Fact]
    public async Task ProviderCredentialResolver_ReturnsFailure_WhenCredentialDoesNotExist()
    {
        var resolver = new ProviderCredentialResolver(new FakeCredentialStore(new CredentialRef("other")));
        var account = CreateAccount(StorageProviderCategory.ObjectStorage, new StorageProviderId("aliyun-oss"));

        var result = await resolver.AcquireMaterialAsync(account);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public void DependencyInjection_RegistersRealCreators_ForAllPhase8Providers()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICredentialStore>(new FakeCredentialStore(new CredentialRef("cred-1")));
        services.AddAtomBoxProviders();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var creators = provider.GetRequiredService<IEnumerable<IStorageProviderCreator>>()
            .ToDictionary(creator => creator.Descriptor.Id.Value, StringComparer.Ordinal);

        Assert.IsType<AliyunOssProviderCreator>(creators["aliyun-oss"]);
        Assert.IsType<TencentCosProviderCreator>(creators["tencent-cos"]);
        Assert.IsType<QiniuKodoProviderCreator>(creators["qiniu-kodo"]);
        Assert.IsType<UpyunProviderCreator>(creators["upyun"]);
        Assert.IsType<HuaweiObsProviderCreator>(creators["huawei-obs"]);
        Assert.IsType<BaiduBosProviderCreator>(creators["baidu-bos"]);
        Assert.IsType<JdCloudOssProviderCreator>(creators["jdcloud-oss"]);
        Assert.IsType<QingStorProviderCreator>(creators["qingstor"]);
        Assert.IsType<VolcengineTosProviderCreator>(creators["volcengine-tos"]);
        Assert.IsType<FtpStorageProviderCreator>(creators["ftp"]);
        Assert.IsType<SftpStorageProviderCreator>(creators["sftp"]);
        Assert.IsType<WebDavStorageProviderCreator>(creators["webdav"]);
        Assert.IsType<AliyunDriveProviderCreator>(creators["aliyun-drive"]);
        Assert.IsType<BaiduNetDiskProviderCreator>(creators["baidu-netdisk"]);
        Assert.DoesNotContain(creators.Values, creator => creator is FakeStorageProviderCreator);
    }

    [Fact]
    public void Registry_DeclaresUniformPhase8ProviderCapabilities()
    {
        var descriptors = StorageProviderRegistry.CreateDefaultDescriptors()
            .ToDictionary(descriptor => descriptor.Id.Value, StringComparer.Ordinal);

        foreach (var providerId in new[]
        {
            "aliyun-oss",
            "tencent-cos",
            "qiniu-kodo",
            "upyun",
            "huawei-obs",
            "baidu-bos",
            "jdcloud-oss",
            "qingstor",
            "volcengine-tos",
            "ftp",
            "sftp",
            "webdav",
            "aliyun-drive",
            "baidu-netdisk"
        })
        {
            Assert.True(descriptors[providerId].Capabilities.Supports(StorageCapability.List), providerId);
            Assert.True(descriptors[providerId].Capabilities.Supports(StorageCapability.Upload), providerId);
            Assert.True(descriptors[providerId].Capabilities.Supports(StorageCapability.Download), providerId);
            Assert.True(descriptors[providerId].Capabilities.Supports(StorageCapability.Delete), providerId);
        }

        Assert.True(descriptors["ftp"].Capabilities.Supports(StorageCapability.CreateFolder));
        Assert.True(descriptors["sftp"].Capabilities.Supports(StorageCapability.CreateFolder));
        Assert.True(descriptors["webdav"].Capabilities.Supports(StorageCapability.CreateFolder));
        Assert.True(descriptors["ftp"].Capabilities.Supports(StorageCapability.Rename));
        Assert.True(descriptors["sftp"].Capabilities.Supports(StorageCapability.Rename));
        Assert.True(descriptors["webdav"].Capabilities.Supports(StorageCapability.Rename));
        Assert.True(descriptors["ftp"].Capabilities.Supports(StorageCapability.Move));
        Assert.True(descriptors["sftp"].Capabilities.Supports(StorageCapability.Move));
        Assert.True(descriptors["webdav"].Capabilities.Supports(StorageCapability.Move));

        foreach (var providerId in new[]
        {
            "aliyun-oss",
            "tencent-cos",
            "qiniu-kodo",
            "upyun",
            "huawei-obs",
            "baidu-bos",
            "jdcloud-oss",
            "qingstor",
            "volcengine-tos"
        })
        {
            Assert.True(descriptors[providerId].Capabilities.Supports(StorageCapability.CreateFolder), providerId);
            Assert.True(descriptors[providerId].Capabilities.Supports(StorageCapability.Rename), providerId);
            Assert.True(descriptors[providerId].Capabilities.Supports(StorageCapability.Move), providerId);
        }

        Assert.True(descriptors["aliyun-oss"].Capabilities.Supports(StorageCapability.MultipartUpload));
        Assert.True(descriptors["tencent-cos"].Capabilities.Supports(StorageCapability.MultipartUpload));
        Assert.True(descriptors["qiniu-kodo"].Capabilities.Supports(StorageCapability.MultipartUpload));
        Assert.True(descriptors["baidu-bos"].Capabilities.Supports(StorageCapability.MultipartUpload));
        Assert.True(descriptors["jdcloud-oss"].Capabilities.Supports(StorageCapability.MultipartUpload));
        Assert.True(descriptors["qingstor"].Capabilities.Supports(StorageCapability.MultipartUpload));
        Assert.True(descriptors["upyun"].Capabilities.Supports(StorageCapability.MultipartUpload));
        Assert.True(descriptors["huawei-obs"].Capabilities.Supports(StorageCapability.MultipartUpload));
        Assert.True(descriptors["volcengine-tos"].Capabilities.Supports(StorageCapability.MultipartUpload));
    }

    private static StorageAccount CreateAccount(
        StorageProviderCategory category,
        StorageProviderId providerId,
        string? endpoint = "example.com",
        IReadOnlyDictionary<string, string>? providerConfig = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            category,
            providerId,
            "Test Account",
            endpoint,
            null,
            new CredentialRef("cred-1"),
            now,
            now,
            providerConfig);
    }

    private static StorageProviderFactory CreateFactory(StorageProviderRegistry registry)
    {
        return new StorageProviderFactory(
            registry,
            registry.GetAll().Select(descriptor => new FakeStorageProviderCreator(descriptor)));
    }

    private sealed class ThrowingStorageProviderCreator : IStorageProviderCreator
    {
        public ThrowingStorageProviderCreator(ProviderDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public ProviderDescriptor Descriptor { get; }

        public Task<AtomBox.Core.Results.OperationResult<AtomBox.Core.Providers.IStorageProvider>> CreateAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("SDK exception should not leak.");
        }
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly CredentialRef _credentialRef;

        public FakeCredentialStore(CredentialRef credentialRef)
        {
            _credentialRef = credentialRef;
        }

        public Task<OperationResult<CredentialRef>> SaveAsync(
            CredentialSecretMaterial material,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialRef>.Success(_credentialRef));
        }

        public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Exists(credentialRef)
                ? Task.FromResult(OperationResult<CredentialLease>.Success(new FakeCredentialLease(credentialRef)))
                : Task.FromResult(OperationResult<CredentialLease>.Failure(StorageError.NotFound("Credential reference was not found.")));
        }

        public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            if (!Exists(credentialRef))
            {
                return Task.FromResult(OperationResult<CredentialMaterialLease>.Failure(
                    StorageError.NotFound("Credential reference was not found.")));
            }

            var lease = new CredentialMaterialLease(
                new FakeCredentialLease(credentialRef),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["accessKeyId"] = "ak-test",
                    ["accessKeySecret"] = "secret-test"
                }));

            return Task.FromResult(OperationResult<CredentialMaterialLease>.Success(lease));
        }

        public Task<OperationResult<bool>> ExistsAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<bool>.Success(Exists(credentialRef)));
        }

        public Task<OperationResult> MarkPendingDeleteAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        private bool Exists(CredentialRef credentialRef)
        {
            return credentialRef == _credentialRef;
        }
    }

    private sealed class FakeCredentialLease : CredentialLease
    {
        public FakeCredentialLease(CredentialRef credentialRef)
            : base(credentialRef, "fake")
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
