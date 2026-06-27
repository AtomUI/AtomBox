using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.AliyunOss;
using AtomBox.Providers.ObjectStorage.BaiduBos;
using AtomBox.Providers.ObjectStorage.HuaweiObs;
using AtomBox.Providers.ObjectStorage.JdCloudOss;
using AtomBox.Providers.ObjectStorage.QingStor;
using AtomBox.Providers.ObjectStorage.QiniuKodo;
using AtomBox.Providers.ObjectStorage.TencentCos;
using AtomBox.Providers.ObjectStorage.Upyun;
using AtomBox.Providers.ObjectStorage.VolcengineTos;

namespace AtomBox.Providers.Tests;

public sealed class ObjectStorageMultipartIntegrationTests
{
    private const long MultipartFileSize = 10L * 1024L * 1024L;

    [Fact]
    [Trait("Category", "ManualIntegration")]
    public async Task FilledMultipartCapableObjectStorageAccounts_Upload10MBMultipartAndKeepObject()
    {
        var environments = MultipartTestEnvironment.ReadFilledEnvironments();
        if (environments.Count == 0)
        {
            return;
        }

        var localFilePath = EnsureMultipartFile();
        foreach (var environment in environments)
        {
            await RunProviderAsync(environment, localFilePath);
        }
    }

    private static async Task RunProviderAsync(MultipartTestEnvironment environment, string localFilePath)
    {
        await using var provider = await CreateProviderAsync(environment);
        Assert.True(
            provider.Capabilities.Supports(StorageCapability.MultipartUpload),
            $"{environment.DisplayName} does not declare MultipartUpload.");

        var prefix = environment.CreateRunPrefix();
        var targetPath = new RemotePath(
            $"{environment.Bucket}/{prefix}multipart-10mb.bin",
            RemotePathKind.ObjectPath);

        await using (var upload = File.OpenRead(localFilePath))
        {
            var result = await provider.UploadAsync(targetPath, upload, upload.Length);
            Assert.True(
                result.IsSuccess,
                ObjectStorageMultipartTestSupport.FormatError(
                    $"Expected {environment.DisplayName} 10MB multipart upload to succeed.",
                    result.Error));
        }

        var parent = targetPath.GetParent();
        Assert.NotNull(parent);
        var list = await provider.ListAsync(parent.Value);
        Assert.True(
            list.IsSuccess,
            ObjectStorageMultipartTestSupport.FormatError(
                $"Expected {environment.DisplayName} multipart target parent list to succeed.",
                list.Error));
        Assert.Contains(
            list.GetValueOrThrow(),
            item => item.Name == "multipart-10mb.bin" && item.Size is null or MultipartFileSize);

        RecordResult(environment, prefix);
    }

    private static async Task<IStorageProvider> CreateProviderAsync(MultipartTestEnvironment environment)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == environment.ProviderId);
        var resolver = new ProviderCredentialResolver(new MultipartCredentialStore(environment));
        IStorageProviderCreator creator = environment.ProviderId.Value switch
        {
            "aliyun-oss" => new AliyunOssProviderCreator(descriptor, resolver),
            "tencent-cos" => new TencentCosProviderCreator(descriptor, resolver),
            "qiniu-kodo" => new QiniuKodoProviderCreator(descriptor, resolver),
            "volcengine-tos" => new VolcengineTosProviderCreator(descriptor, resolver),
            "huawei-obs" => new HuaweiObsProviderCreator(descriptor, resolver),
            "upyun" => new UpyunProviderCreator(descriptor, resolver),
            "baidu-bos" => new BaiduBosProviderCreator(descriptor, resolver),
            "jdcloud-oss" => new JdCloudOssProviderCreator(descriptor, resolver),
            "qingstor" => new QingStorProviderCreator(descriptor, resolver),
            _ => throw new InvalidOperationException($"Unsupported provider {environment.ProviderId.Value}.")
        };

        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            environment.ProviderId,
            $"{environment.DisplayName} Multipart",
            environment.Endpoint,
            environment.Region,
            new CredentialRef(environment.CredentialName),
            now,
            now,
            new Dictionary<string, string>
            {
                ["bucket"] = environment.Bucket,
                ["region"] = environment.Region
            });

        var result = await creator.CreateAsync(account);
        Assert.True(
            result.IsSuccess,
            ObjectStorageMultipartTestSupport.FormatError(
                $"Expected {environment.DisplayName} provider creation to succeed.",
                result.Error));
        return result.GetValueOrThrow();
    }

    private static string EnsureMultipartFile()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, ".artifacts", "manual-integration");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "multipart-10mb.bin");
        if (!File.Exists(path) || new FileInfo(path).Length != MultipartFileSize)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            stream.SetLength(MultipartFileSize);
        }

        return path;
    }

    private static void RecordResult(MultipartTestEnvironment environment, string prefix)
    {
        var outputPath = Path.Combine(Environment.CurrentDirectory, ".artifacts", "provider-multipart-results.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.AppendAllText(
            outputPath,
            $"{DateTimeOffset.UtcNow:O} {environment.DisplayName} bucket={environment.Bucket} prefix={prefix}{Environment.NewLine}");
    }
}

internal sealed record MultipartTestEnvironment(
    StorageProviderId ProviderId,
    string DisplayName,
    string EnvironmentPrefix,
    string Endpoint,
    string Region,
    string Bucket,
    string AccessKeyId,
    string AccessKeySecret,
    string TestPrefix)
{
    public string CredentialName => $"{EnvironmentPrefix.ToLowerInvariant().Replace('_', '-')}-multipart";

    public static IReadOnlyList<MultipartTestEnvironment> ReadFilledEnvironments()
    {
        var items = new List<MultipartTestEnvironment>();
        AddIfFilled(items, TryReadAliyun());
        AddIfFilled(items, TryReadTencent());
        AddIfFilled(items, TryReadQiniu());
        AddIfFilled(items, TryReadVolcengine());
        AddIfFilled(items, TryReadHuawei());
        AddIfFilled(items, TryReadUpyun());
        AddIfFilled(items, TryReadS3Compatible(StorageProviderRegistry.BaiduBosProviderId, "Baidu BOS", "BAIDU_BOS"));
        AddIfFilled(items, TryReadS3Compatible(StorageProviderRegistry.JdCloudOssProviderId, "JDCloud OSS", "JDCLOUD_OSS"));
        AddIfFilled(items, TryReadS3Compatible(StorageProviderRegistry.QingStorProviderId, "QingStor", "QINGSTOR", "ZONE"));
        return items;
    }

    public string CreateRunPrefix()
    {
        var safePrefix = TestPrefix.Contains("atombox", StringComparison.OrdinalIgnoreCase)
            ? "provider-tests/"
            : TestPrefix;
        return $"{safePrefix}multipart-{ProviderId.Value}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}/";
    }

    private static MultipartTestEnvironment? TryReadAliyun()
    {
        if (!IsEnabled("ATOMBOX_TEST_ALIYUN_OSS"))
        {
            return null;
        }

        return TryCreate(
            StorageProviderRegistry.AliyunOssProviderId,
            "Aliyun OSS",
            "ALIYUN_OSS",
            "ATOMBOX_OSS_ENDPOINT",
            "ATOMBOX_OSS_REGION",
            "ATOMBOX_OSS_BUCKET",
            "ATOMBOX_OSS_ACCESS_KEY_ID",
            "ATOMBOX_OSS_ACCESS_KEY_SECRET",
            "ATOMBOX_OSS_TEST_PREFIX");
    }

    private static MultipartTestEnvironment? TryReadTencent()
    {
        if (!IsEnabled("ATOMBOX_TEST_TENCENT_COS"))
        {
            return null;
        }

        return TryCreate(
            StorageProviderRegistry.TencentCosProviderId,
            "Tencent COS",
            "TENCENT_COS",
            "ATOMBOX_COS_ENDPOINT",
            "ATOMBOX_COS_REGION",
            "ATOMBOX_COS_BUCKET",
            "ATOMBOX_COS_SECRET_ID",
            "ATOMBOX_COS_SECRET_KEY",
            "ATOMBOX_COS_TEST_PREFIX");
    }

    private static MultipartTestEnvironment? TryReadQiniu()
    {
        if (!IsEnabled("ATOMBOX_TEST_QINIU_KODO"))
        {
            return null;
        }

        return TryCreate(
            StorageProviderRegistry.QiniuKodoProviderId,
            "Qiniu Kodo",
            "QINIU_KODO",
            "ATOMBOX_QINIU_KODO_ENDPOINT",
            "ATOMBOX_QINIU_KODO_REGION",
            "ATOMBOX_QINIU_KODO_BUCKET",
            "ATOMBOX_QINIU_KODO_ACCESS_KEY",
            "ATOMBOX_QINIU_KODO_SECRET_KEY",
            "ATOMBOX_QINIU_KODO_TEST_PREFIX");
    }

    private static MultipartTestEnvironment? TryReadVolcengine()
    {
        if (!IsEnabled("ATOMBOX_TEST_VOLCENGINE_TOS"))
        {
            return null;
        }

        return TryCreate(
            StorageProviderRegistry.VolcengineTosProviderId,
            "Volcengine TOS",
            "VOLCENGINE_TOS",
            "ATOMBOX_VOLCENGINE_TOS_ENDPOINT",
            "ATOMBOX_VOLCENGINE_TOS_REGION",
            "ATOMBOX_VOLCENGINE_TOS_BUCKET",
            "ATOMBOX_VOLCENGINE_TOS_ACCESS_KEY_ID",
            "ATOMBOX_VOLCENGINE_TOS_ACCESS_KEY_SECRET",
            "ATOMBOX_VOLCENGINE_TOS_TEST_PREFIX");
    }

    private static MultipartTestEnvironment? TryReadHuawei()
    {
        if (!IsEnabled("ATOMBOX_TEST_HUAWEI_OBS"))
        {
            return null;
        }

        return TryCreate(
            StorageProviderRegistry.HuaweiObsProviderId,
            "Huawei OBS",
            "HUAWEI_OBS",
            "ATOMBOX_HUAWEI_OBS_ENDPOINT",
            "ATOMBOX_HUAWEI_OBS_REGION",
            "ATOMBOX_HUAWEI_OBS_BUCKET",
            "ATOMBOX_HUAWEI_OBS_ACCESS_KEY_ID",
            "ATOMBOX_HUAWEI_OBS_ACCESS_KEY_SECRET",
            "ATOMBOX_HUAWEI_OBS_TEST_PREFIX");
    }

    private static MultipartTestEnvironment? TryReadUpyun()
    {
        if (!IsEnabled("ATOMBOX_TEST_UPYUN"))
        {
            return null;
        }

        return TryCreate(
            StorageProviderRegistry.UpyunProviderId,
            "Upyun USS",
            "UPYUN",
            "ATOMBOX_UPYUN_ENDPOINT",
            null,
            "ATOMBOX_UPYUN_SERVICE",
            "ATOMBOX_UPYUN_OPERATOR",
            "ATOMBOX_UPYUN_PASSWORD",
            "ATOMBOX_UPYUN_TEST_PREFIX");
    }

    private static MultipartTestEnvironment? TryReadS3Compatible(
        StorageProviderId providerId,
        string displayName,
        string environmentPrefix,
        string regionVariableSuffix = "REGION")
    {
        if (!IsEnabled($"ATOMBOX_TEST_{environmentPrefix}"))
        {
            return null;
        }

        return TryCreate(
            providerId,
            displayName,
            environmentPrefix,
            $"ATOMBOX_{environmentPrefix}_ENDPOINT",
            $"ATOMBOX_{environmentPrefix}_{regionVariableSuffix}",
            $"ATOMBOX_{environmentPrefix}_BUCKET",
            $"ATOMBOX_{environmentPrefix}_ACCESS_KEY_ID",
            $"ATOMBOX_{environmentPrefix}_ACCESS_KEY_SECRET",
            $"ATOMBOX_{environmentPrefix}_TEST_PREFIX");
    }

    private static MultipartTestEnvironment? TryCreate(
        StorageProviderId providerId,
        string displayName,
        string environmentPrefix,
        string endpointVariable,
        string? regionVariable,
        string bucketVariable,
        string accessKeyIdVariable,
        string accessKeySecretVariable,
        string prefixVariable)
    {
        var endpoint = Environment.GetEnvironmentVariable(endpointVariable);
        var region = regionVariable is null ? string.Empty : Environment.GetEnvironmentVariable(regionVariable);
        var bucket = Environment.GetEnvironmentVariable(bucketVariable);
        var accessKeyId = Environment.GetEnvironmentVariable(accessKeyIdVariable);
        var accessKeySecret = Environment.GetEnvironmentVariable(accessKeySecretVariable);
        if (string.IsNullOrWhiteSpace(endpoint) ||
            (regionVariable is not null && string.IsNullOrWhiteSpace(region)) ||
            string.IsNullOrWhiteSpace(bucket) ||
            string.IsNullOrWhiteSpace(accessKeyId) ||
            string.IsNullOrWhiteSpace(accessKeySecret))
        {
            return null;
        }

        var prefix = Environment.GetEnvironmentVariable(prefixVariable);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "provider-tests/";
        }

        return new MultipartTestEnvironment(
            providerId,
            displayName,
            environmentPrefix,
            endpoint.Trim(),
            region?.Trim() ?? string.Empty,
            bucket.Trim(),
            accessKeyId.Trim(),
            accessKeySecret.Trim(),
            NormalizePrefix(prefix));
    }

    private static bool IsEnabled(string variableName)
    {
        return string.Equals(Environment.GetEnvironmentVariable(variableName), "1", StringComparison.Ordinal);
    }

    private static void AddIfFilled(List<MultipartTestEnvironment> items, MultipartTestEnvironment? environment)
    {
        if (environment is not null)
        {
            items.Add(environment);
        }
    }

    private static string NormalizePrefix(string prefix)
    {
        return prefix.Trim().TrimStart('/').TrimEnd('/') + "/";
    }
}

internal static class ObjectStorageMultipartTestSupport
{
    public static string FormatError(string message, Core.Errors.StorageError? error)
    {
        return error is null
            ? message
            : $"{message} Code={error.Code}; Category={error.Category}; Retryable={error.IsRetryable}; ProviderErrorCode={error.ProviderErrorCode}; Message={error.Message}";
    }
}

internal sealed class MultipartCredentialStore : ICredentialStore
{
    private readonly MultipartTestEnvironment _environment;

    public MultipartCredentialStore(MultipartTestEnvironment environment)
    {
        _environment = environment;
    }

    public Task<OperationResult<CredentialRef>> SaveAsync(
        CredentialSecretMaterial material,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<CredentialRef>.Success(new CredentialRef(_environment.CredentialName)));
    }

    public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<CredentialLease>.Success(new MultipartCredentialLease(_environment.CredentialName)));
    }

    public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default)
    {
        var lease = new CredentialMaterialLease(
            new MultipartCredentialLease(_environment.CredentialName),
            new CredentialSecretMaterial(new Dictionary<string, string>
            {
                ["accessKeyId"] = _environment.AccessKeyId,
                ["accessKeySecret"] = _environment.AccessKeySecret
            }));

        return Task.FromResult(OperationResult<CredentialMaterialLease>.Success(lease));
    }

    public Task<OperationResult<bool>> ExistsAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<bool>.Success(credentialRef.Value == _environment.CredentialName));
    }

    public Task<OperationResult> MarkPendingDeleteAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success());
    }
}

internal sealed class MultipartCredentialLease : CredentialLease
{
    public MultipartCredentialLease(string credentialName)
        : base(new CredentialRef(credentialName), credentialName)
    {
    }

    public override ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
