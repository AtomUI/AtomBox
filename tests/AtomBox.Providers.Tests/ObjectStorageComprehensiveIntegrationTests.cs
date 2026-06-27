using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.AliyunOss;
using AtomBox.Providers.ObjectStorage.HuaweiObs;
using AtomBox.Providers.ObjectStorage.QiniuKodo;
using AtomBox.Providers.ObjectStorage.TencentCos;
using AtomBox.Providers.ObjectStorage.Upyun;
using AtomBox.Providers.ObjectStorage.VolcengineTos;
using System.Text;

namespace AtomBox.Providers.Tests;

public sealed class ObjectStorageComprehensiveIntegrationTests
{
    private const long LargeFileSize = 100L * 1024L;

    [Fact]
    [Trait("Category", "ManualIntegration")]
    public async Task FilledObjectStorageAccounts_RunComprehensiveFunctionalTestAndKeepObjects()
    {
        var environments = ObjectStorageTestEnvironment.ReadFilledEnvironments();
        if (environments.Count == 0)
        {
            return;
        }

        var largeFilePath = EnsureLargeFile();
        foreach (var environment in environments)
        {
            await RunProviderAsync(environment, largeFilePath);
        }
    }

    private static async Task RunProviderAsync(ObjectStorageTestEnvironment environment, string largeFilePath)
    {
        await using var provider = await CreateProviderAsync(environment);
        var prefix = environment.CreateRunPrefix();
        var bucketRoot = new RemotePath(environment.Bucket, RemotePathKind.BucketRoot);
        var folderPath = new RemotePath($"{environment.Bucket}/{prefix}created-folder", RemotePathKind.Folder);
        var smallPath = new RemotePath($"{environment.Bucket}/{prefix}small/source.txt", RemotePathKind.ObjectPath);
        var renamedPath = new RemotePath($"{environment.Bucket}/{prefix}small/renamed.txt", RemotePathKind.ObjectPath);
        var movedPath = new RemotePath($"{environment.Bucket}/{prefix}moved/final.txt", RemotePathKind.ObjectPath);
        var largePath = new RemotePath($"{environment.Bucket}/{prefix}large/large-100kb.bin", RemotePathKind.ObjectPath);
        var pageFolder = new RemotePath($"{environment.Bucket}/{prefix}page", RemotePathKind.Folder);
        var searchFolder = new RemotePath($"{environment.Bucket}/{prefix}search", RemotePathKind.Folder);
        var smallPayload = Encoding.UTF8.GetBytes($"provider verification {environment.ProviderId.Value} {DateTimeOffset.UtcNow:O}");

        var rootList = await RetryAsync(() => provider.ListAsync(bucketRoot));
        AssertSuccess(rootList, $"{environment.DisplayName} bucket root list");

        var createFolder = await RetryAsync(() => provider.CreateFolderAsync(folderPath));
        AssertSuccess(createFolder, $"{environment.DisplayName} create folder");

        await using (var upload = new MemoryStream(smallPayload))
        {
            var smallUpload = await RetryAsync(() => provider.UploadAsync(smallPath, upload, upload.Length));
            AssertUploadAccepted(smallUpload, $"{environment.DisplayName} upload small file");
        }

        await using (var download = new MemoryStream())
        {
            var smallDownload = await RetryAsync(() => provider.DownloadAsync(smallPath, download));
            AssertSuccess(smallDownload, $"{environment.DisplayName} download small file");
            Assert.Equal(smallPayload, download.ToArray());
        }

        var rename = await RetryAsync(() => provider.RenameAsync(smallPath, "renamed.txt"));
        AssertSuccess(rename, $"{environment.DisplayName} rename small file");

        var move = await RetryAsync(() => provider.MoveAsync(renamedPath, movedPath));
        AssertSuccess(move, $"{environment.DisplayName} move small file");

        var createPageFolder = await RetryAsync(() => provider.CreateFolderAsync(pageFolder));
        AssertSuccess(createPageFolder, $"{environment.DisplayName} create page folder");
        await UploadPageObjectsAsync(provider, environment, prefix);
        var firstPage = await RetryPageUntilAsync(
            () => provider.ListPageAsync(pageFolder, new RemotePageRequest(2)),
            page => page.Items.Count == 2,
            $"{environment.DisplayName} list first page");
        AssertSuccess(firstPage, $"{environment.DisplayName} list first page");
        Assert.Equal(2, firstPage.GetValueOrThrow().Items.Count);
        Assert.NotNull(firstPage.GetValueOrThrow().NextCursor);

        var secondPage = await RetryAsync(() => provider.ListPageAsync(
            pageFolder,
            new RemotePageRequest(2, firstPage.GetValueOrThrow().NextCursor)));
        AssertSuccess(secondPage, $"{environment.DisplayName} list second page");
        Assert.NotEmpty(secondPage.GetValueOrThrow().Items);

        var createSearchFolder = await RetryAsync(() => provider.CreateFolderAsync(searchFolder));
        AssertSuccess(createSearchFolder, $"{environment.DisplayName} create search folder");
        var createNestedSearchFolder = await RetryAsync(() => provider.CreateFolderAsync(
            new RemotePath($"{environment.Bucket}/{prefix}search/nested", RemotePathKind.Folder)));
        AssertSuccess(createNestedSearchFolder, $"{environment.DisplayName} create nested search folder");
        await UploadSearchObjectsAsync(provider, environment, prefix);
        var searchList = await RetryListUntilAsync(
            () => provider.ListAsync(searchFolder),
            items => items.Any(item => item.Kind == RemoteItemKind.File && item.Name == "match-a.txt") &&
                     items.Any(item => item.Kind == RemoteItemKind.Folder && item.Name == "nested"),
            $"{environment.DisplayName} prefix search folder list");
        AssertSuccess(searchList, $"{environment.DisplayName} prefix search folder list");
        Assert.Contains(searchList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "match-a.txt");
        Assert.Contains(searchList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.Folder && item.Name == "nested");
        Assert.DoesNotContain(searchList.GetValueOrThrow(), item => item.Name == "outside.txt");

        var largeParent = largePath.GetParent();
        Assert.NotNull(largeParent);
        var createLargeFolder = await RetryAsync(() => provider.CreateFolderAsync(largeParent.Value));
        AssertSuccess(createLargeFolder, $"{environment.DisplayName} create large folder");

        await using (var largeUpload = File.OpenRead(largeFilePath))
        {
            var largeResult = await RetryAsync(() => provider.UploadAsync(largePath, largeUpload, largeUpload.Length));
            AssertUploadAccepted(largeResult, $"{environment.DisplayName} upload 100KB file");
        }

        var largeParentList = await RetryListUntilAsync(
            () => provider.ListAsync(largeParent.Value),
            items => items.Any(item => item.Name == "large-100kb.bin"),
            $"{environment.DisplayName} list large file parent");
        AssertSuccess(largeParentList, $"{environment.DisplayName} list large file parent");
        Assert.Contains(largeParentList.GetValueOrThrow(), item => item.Name == "large-100kb.bin");

        if (LargeFileSize >= 5L * 1024L * 1024L &&
            provider.Capabilities.Supports(StorageCapability.MultipartUpload))
        {
            await using var secondLargeUpload = File.OpenRead(largeFilePath);
            var multipartPath = new RemotePath($"{environment.Bucket}/{prefix}large/multipart-100kb.bin", RemotePathKind.ObjectPath);
            var multipartResult = await RetryAsync(() => provider.UploadAsync(multipartPath, secondLargeUpload, secondLargeUpload.Length));
            AssertUploadAccepted(multipartResult, $"{environment.DisplayName} multipart 100KB upload");
        }

        RecordResult(environment, prefix);
    }

    private static async Task UploadPageObjectsAsync(IStorageProvider provider, ObjectStorageTestEnvironment environment, string prefix)
    {
        for (var index = 1; index <= 5; index++)
        {
            var payload = Encoding.UTF8.GetBytes($"page object {index}");
            var path = new RemotePath($"{environment.Bucket}/{prefix}page/page-{index:D2}.txt", RemotePathKind.ObjectPath);
            await using var upload = new MemoryStream(payload);
            var result = await RetryAsync(() => provider.UploadAsync(path, upload, upload.Length));
            AssertUploadAccepted(result, $"{environment.DisplayName} upload page object {index}");
        }
    }

    private static async Task UploadSearchObjectsAsync(IStorageProvider provider, ObjectStorageTestEnvironment environment, string prefix)
    {
        var objects = new[]
        {
            $"{prefix}search/match-a.txt",
            $"{prefix}search/nested/match-b.txt",
            $"{prefix}outside.txt"
        };

        foreach (var key in objects)
        {
            var payload = Encoding.UTF8.GetBytes($"search object {key}");
            await using var upload = new MemoryStream(payload);
            var result = await RetryAsync(() => provider.UploadAsync(
                new RemotePath($"{environment.Bucket}/{key}", RemotePathKind.ObjectPath),
                upload,
                upload.Length));
            AssertUploadAccepted(result, $"{environment.DisplayName} upload search object {key}");
        }
    }

    private static async Task<IStorageProvider> CreateProviderAsync(ObjectStorageTestEnvironment environment)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == environment.ProviderId);
        var resolver = new ProviderCredentialResolver(new EnvironmentCredentialStore(environment));
        IStorageProviderCreator creator = environment.ProviderId.Value switch
        {
            "aliyun-oss" => new AliyunOssProviderCreator(descriptor, resolver),
            "tencent-cos" => new TencentCosProviderCreator(descriptor, resolver),
            "qiniu-kodo" => new QiniuKodoProviderCreator(descriptor, resolver),
            "upyun" => new UpyunProviderCreator(descriptor, resolver),
            "huawei-obs" => new HuaweiObsProviderCreator(descriptor, resolver),
            "volcengine-tos" => new VolcengineTosProviderCreator(descriptor, resolver),
            _ => throw new InvalidOperationException($"Unsupported provider {environment.ProviderId.Value}.")
        };
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            environment.ProviderId,
            environment.DisplayName,
            environment.Endpoint,
            environment.Region,
            new CredentialRef(environment.CredentialName),
            now,
            now,
            environment.ProviderConfig);

        var result = await creator.CreateAsync(account);
        AssertSuccess(result, $"{environment.DisplayName} create provider");
        return result.GetValueOrThrow();
    }

    private static string EnsureLargeFile()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, ".artifacts", "manual-integration");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "large-100kb.bin");
        if (!File.Exists(path) || new FileInfo(path).Length != LargeFileSize)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            stream.SetLength(LargeFileSize);
        }

        return path;
    }

    private static void RecordResult(ObjectStorageTestEnvironment environment, string prefix)
    {
        var outputPath = Path.Combine(Environment.CurrentDirectory, ".artifacts", "provider-comprehensive-results.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.AppendAllText(
            outputPath,
            $"{DateTimeOffset.UtcNow:O} {environment.DisplayName} bucket={environment.Bucket} prefix={prefix}{Environment.NewLine}");
    }

    private static async Task<OperationResult> RetryAsync(Func<Task<OperationResult>> operation)
    {
        OperationResult? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            last = await operation();
            if (last.IsSuccess || last.Error?.IsRetryable != true)
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt));
        }

        return last!;
    }

    private static async Task<OperationResult<T>> RetryAsync<T>(Func<Task<OperationResult<T>>> operation)
    {
        OperationResult<T>? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            last = await operation();
            if (last.IsSuccess || last.Error?.IsRetryable != true)
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt));
        }

        return last!;
    }

    private static async Task<OperationResult<IReadOnlyList<RemoteItem>>> RetryListUntilAsync(
        Func<Task<OperationResult<IReadOnlyList<RemoteItem>>>> operation,
        Func<IReadOnlyList<RemoteItem>, bool> isReady,
        string operationName)
    {
        OperationResult<IReadOnlyList<RemoteItem>>? last = null;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            last = await RetryAsync(operation);
            if (last.IsSuccess && isReady(last.GetValueOrThrow()))
            {
                return last;
            }

            if (last.IsFailure && last.Error?.IsRetryable != true)
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt));
        }

        return last ?? OperationResult<IReadOnlyList<RemoteItem>>.Failure(StorageError.Unknown(
            $"{operationName} did not return expected items before timeout."));
    }

    private static async Task<OperationResult<RemoteItemPage>> RetryPageUntilAsync(
        Func<Task<OperationResult<RemoteItemPage>>> operation,
        Func<RemoteItemPage, bool> isReady,
        string operationName)
    {
        OperationResult<RemoteItemPage>? last = null;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            last = await RetryAsync(operation);
            if (last.IsSuccess && isReady(last.GetValueOrThrow()))
            {
                return last;
            }

            if (last.IsFailure && last.Error?.IsRetryable != true)
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt));
        }

        return last ?? OperationResult<RemoteItemPage>.Failure(StorageError.Unknown(
            $"{operationName} did not return expected items before timeout."));
    }

    private static void AssertSuccess(OperationResult result, string operation)
    {
        Assert.True(result.IsSuccess, FormatError(operation, result.Error));
    }

    private static void AssertUploadAccepted(OperationResult result, string operation)
    {
        Assert.True(
            result.IsSuccess || result.Error?.Category == StorageErrorCategory.Conflict,
            FormatError(operation, result.Error));
    }

    private static void AssertSuccess<T>(OperationResult<T> result, string operation)
    {
        Assert.True(result.IsSuccess, FormatError(operation, result.Error));
    }

    private static string FormatError(string operation, StorageError? error)
    {
        return error is null
            ? $"{operation} failed."
            : $"{operation} failed. Code={error.Code}; Category={error.Category}; Retryable={error.IsRetryable}; ProviderErrorCode={error.ProviderErrorCode}; Message={error.Message}";
    }

    private sealed record ObjectStorageTestEnvironment(
        StorageProviderId ProviderId,
        string DisplayName,
        string EnvironmentPrefix,
        string Endpoint,
        string? Region,
        string Bucket,
        string AccessKeyId,
        string AccessKeySecret,
        string TestPrefix,
        IReadOnlyDictionary<string, string> ProviderConfig)
    {
        public string CredentialName => $"{EnvironmentPrefix.ToLowerInvariant()}-comprehensive";

        public static IReadOnlyList<ObjectStorageTestEnvironment> ReadFilledEnvironments()
        {
            var items = new List<ObjectStorageTestEnvironment>();
            AddIfFilled(items, TryReadAliyun());
            AddIfFilled(items, TryReadTencent());
            AddIfFilled(items, TryReadQiniu());
            AddIfFilled(items, TryReadUpyun());
            AddIfFilled(items, TryReadHuawei());
            AddIfFilled(items, TryReadVolcengine());
            return items;
        }

        public string CreateRunPrefix()
        {
            var safePrefix = TestPrefix.Contains("atombox", StringComparison.OrdinalIgnoreCase)
                ? "provider-tests/"
                : TestPrefix;
            return $"{safePrefix}verify-{ProviderId.Value}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}/";
        }

        private static ObjectStorageTestEnvironment? TryReadAliyun()
        {
            return TryRead(
                StorageProviderRegistry.AliyunOssProviderId,
                "Aliyun OSS",
                "ALIYUN_OSS",
                "ATOMBOX_TEST_ALIYUN_OSS",
                "ATOMBOX_OSS_ENDPOINT",
                "ATOMBOX_OSS_REGION",
                "ATOMBOX_OSS_BUCKET",
                "ATOMBOX_OSS_ACCESS_KEY_ID",
                "ATOMBOX_OSS_ACCESS_KEY_SECRET",
                "ATOMBOX_OSS_TEST_PREFIX");
        }

        private static ObjectStorageTestEnvironment? TryReadTencent()
        {
            return TryRead(
                StorageProviderRegistry.TencentCosProviderId,
                "Tencent COS",
                "TENCENT_COS",
                "ATOMBOX_TEST_TENCENT_COS",
                "ATOMBOX_COS_ENDPOINT",
                "ATOMBOX_COS_REGION",
                "ATOMBOX_COS_BUCKET",
                "ATOMBOX_COS_SECRET_ID",
                "ATOMBOX_COS_SECRET_KEY",
                "ATOMBOX_COS_TEST_PREFIX");
        }

        private static ObjectStorageTestEnvironment? TryReadQiniu()
        {
            return TryRead(
                StorageProviderRegistry.QiniuKodoProviderId,
                "Qiniu Kodo",
                "QINIU_KODO",
                "ATOMBOX_TEST_QINIU_KODO",
                "ATOMBOX_QINIU_KODO_ENDPOINT",
                "ATOMBOX_QINIU_KODO_REGION",
                "ATOMBOX_QINIU_KODO_BUCKET",
                "ATOMBOX_QINIU_KODO_ACCESS_KEY",
                "ATOMBOX_QINIU_KODO_SECRET_KEY",
                "ATOMBOX_QINIU_KODO_TEST_PREFIX");
        }

        private static ObjectStorageTestEnvironment? TryReadUpyun()
        {
            return TryRead(
                StorageProviderRegistry.UpyunProviderId,
                "Upyun USS",
                "UPYUN",
                "ATOMBOX_TEST_UPYUN",
                "ATOMBOX_UPYUN_ENDPOINT",
                null,
                "ATOMBOX_UPYUN_SERVICE",
                "ATOMBOX_UPYUN_OPERATOR",
                "ATOMBOX_UPYUN_PASSWORD",
                "ATOMBOX_UPYUN_TEST_PREFIX");
        }

        private static ObjectStorageTestEnvironment? TryReadHuawei()
        {
            return TryRead(
                StorageProviderRegistry.HuaweiObsProviderId,
                "Huawei OBS",
                "HUAWEI_OBS",
                "ATOMBOX_TEST_HUAWEI_OBS",
                "ATOMBOX_HUAWEI_OBS_ENDPOINT",
                "ATOMBOX_HUAWEI_OBS_REGION",
                "ATOMBOX_HUAWEI_OBS_BUCKET",
                "ATOMBOX_HUAWEI_OBS_ACCESS_KEY_ID",
                "ATOMBOX_HUAWEI_OBS_ACCESS_KEY_SECRET",
                "ATOMBOX_HUAWEI_OBS_TEST_PREFIX");
        }

        private static ObjectStorageTestEnvironment? TryReadVolcengine()
        {
            return TryRead(
                StorageProviderRegistry.VolcengineTosProviderId,
                "Volcengine TOS",
                "VOLCENGINE_TOS",
                "ATOMBOX_TEST_VOLCENGINE_TOS",
                "ATOMBOX_VOLCENGINE_TOS_ENDPOINT",
                "ATOMBOX_VOLCENGINE_TOS_REGION",
                "ATOMBOX_VOLCENGINE_TOS_BUCKET",
                "ATOMBOX_VOLCENGINE_TOS_ACCESS_KEY_ID",
                "ATOMBOX_VOLCENGINE_TOS_ACCESS_KEY_SECRET",
                "ATOMBOX_VOLCENGINE_TOS_TEST_PREFIX");
        }

        private static ObjectStorageTestEnvironment? TryRead(
            StorageProviderId providerId,
            string displayName,
            string environmentPrefix,
            string enabledName,
            string endpointName,
            string? regionName,
            string bucketName,
            string accessKeyIdName,
            string accessKeySecretName,
            string prefixName)
        {
            if (!string.Equals(Environment.GetEnvironmentVariable(enabledName), "1", StringComparison.Ordinal))
            {
                return null;
            }

            var endpoint = Environment.GetEnvironmentVariable(endpointName);
            var region = regionName is null ? null : Environment.GetEnvironmentVariable(regionName);
            var bucket = Environment.GetEnvironmentVariable(bucketName);
            var accessKeyId = Environment.GetEnvironmentVariable(accessKeyIdName);
            var accessKeySecret = Environment.GetEnvironmentVariable(accessKeySecretName);
            if (string.IsNullOrWhiteSpace(endpoint) ||
                string.IsNullOrWhiteSpace(bucket) ||
                string.IsNullOrWhiteSpace(accessKeyId) ||
                string.IsNullOrWhiteSpace(accessKeySecret) ||
                (regionName is not null && string.IsNullOrWhiteSpace(region)))
            {
                return null;
            }

            var prefix = Environment.GetEnvironmentVariable(prefixName);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "provider-tests/";
            }

            return new ObjectStorageTestEnvironment(
                providerId,
                displayName,
                environmentPrefix,
                endpoint.Trim(),
                region?.Trim(),
                bucket.Trim(),
                accessKeyId.Trim(),
                accessKeySecret.Trim(),
                NormalizePrefix(prefix),
                new Dictionary<string, string>
                {
                    ["bucket"] = bucket.Trim(),
                    ["region"] = region?.Trim() ?? string.Empty
                });
        }

        private static void AddIfFilled(List<ObjectStorageTestEnvironment> items, ObjectStorageTestEnvironment? environment)
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

    private sealed class EnvironmentCredentialStore : ICredentialStore
    {
        private readonly ObjectStorageTestEnvironment _environment;

        public EnvironmentCredentialStore(ObjectStorageTestEnvironment environment)
        {
            _environment = environment;
        }

        public Task<OperationResult<CredentialRef>> SaveAsync(CredentialSecretMaterial material, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialRef>.Success(new CredentialRef(_environment.CredentialName)));
        }

        public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(CredentialRef credentialRef, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialLease>.Success(new EnvironmentCredentialLease(_environment.CredentialName)));
        }

        public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(CredentialRef credentialRef, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialMaterialLease>.Success(new CredentialMaterialLease(
                new EnvironmentCredentialLease(_environment.CredentialName),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["accessKeyId"] = _environment.AccessKeyId,
                    ["accessKeySecret"] = _environment.AccessKeySecret
                }))));
        }

        public Task<OperationResult<bool>> ExistsAsync(CredentialRef credentialRef, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<bool>.Success(credentialRef.Value == _environment.CredentialName));
        }

        public Task<OperationResult> MarkPendingDeleteAsync(CredentialRef credentialRef, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class EnvironmentCredentialLease : CredentialLease
    {
        public EnvironmentCredentialLease(string credentialName)
            : base(new CredentialRef(credentialName), credentialName)
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
