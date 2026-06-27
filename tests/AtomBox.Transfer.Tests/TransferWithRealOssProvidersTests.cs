using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.AliyunOss;
using AtomBox.Providers.ObjectStorage.HuaweiObs;
using AtomBox.Providers.ObjectStorage.QiniuKodo;
using AtomBox.Providers.ObjectStorage.TencentCos;
using AtomBox.Providers.ObjectStorage.Upyun;
using AtomBox.Providers.ObjectStorage.VolcengineTos;
using AtomBox.Transfer.Workers;
using System.Text;

namespace AtomBox.Transfer.Tests;

public sealed class TransferWithRealOssProvidersTests
{
    [Fact]
    public async Task UploadViaTransfer_Succeeds()
    {
        var configs = TransferOssTestConfig.ReadFilled();
        if (configs.Count == 0) return;

        foreach (var config in configs)
        {
            var store = new MemoryTransferStore();
            var payload = Encoding.UTF8.GetBytes($"Upload test {config.DisplayName} {Guid.NewGuid():N}");
            var key = config.CreateObjectKey();

            await using (var provider = await config.CreateProviderAsync())
            {
                var worker = new TransferWorker(
                    new AnyAccountRepository(),
                    new FixedProviderFactory(provider),
                    new FixedContentLocalFileStore(payload),
                    store);

                var task = CreateUploadTask(config, key);
                var result = await worker.ExecuteAsync(task);

                if (!result.IsSuccess)
                {
                    var err = result.Error;
                    System.Console.Error.WriteLine($"{config.DisplayName}: Upload FAILED. Category={err?.Category} Message={err?.Message} Code={err?.Code} ProviderErrorCode={err?.ProviderErrorCode} IsRetryable={err?.IsRetryable}");
                    // Peek at the store for the task status
                    var failedTask = store.Tasks.SingleOrDefault(t => t.Id == task.Id);
                    System.Console.Error.WriteLine($"  Task status={failedTask?.Status} reason={failedTask?.StatusReason} errorCategory={failedTask?.ErrorCategory} retryable={failedTask?.IsRetryable}");
                }
                Assert.True(result.IsSuccess, $"{config.DisplayName}: Upload failed. Error: {result.Error?.Category} - {result.Error?.Message} (code={result.Error?.Code} providerErrorCode={result.Error?.ProviderErrorCode})");
                var saved = store.Tasks.Single(t => t.Id == task.Id);
                Assert.Equal(TransferStatus.Succeeded, saved.Status);
            }
        }
    }

    [Fact]
    public async Task DownloadViaTransfer_Succeeds()
    {
        var configs = TransferOssTestConfig.ReadFilled();
        if (configs.Count == 0) return;

        foreach (var config in configs)
        {
            var payload = Encoding.UTF8.GetBytes($"Download test {config.DisplayName} {Guid.NewGuid():N}");
            var key = config.CreateObjectKey();
            var objectPath = new RemotePath($"{config.Bucket}/{key}", RemotePathKind.ObjectPath);

            await using (var provider = await config.CreateProviderAsync())
            {
                await using (var upload = new MemoryStream(payload))
                {
                    var uploadResult = await provider.UploadAsync(objectPath, upload, upload.Length);
                    Assert.True(uploadResult.IsSuccess, $"{config.DisplayName}: Pre-upload failed. Error: {uploadResult.Error?.Category}");
                }

                var store = new MemoryTransferStore();
                using var downloadStream = new MemoryStream();
                var worker = new TransferWorker(
                    new AnyAccountRepository(),
                    new FixedProviderFactory(provider),
                    new CapturingWriteLocalFileStore(downloadStream),
                    store);

                var task = CreateDownloadTask(config, key);
                var result = await worker.ExecuteAsync(task);

                Assert.True(result.IsSuccess, $"{config.DisplayName}: Download failed. Error: {result.Error?.Category} - {result.Error?.Message}");
                Assert.Equal(payload, downloadStream.ToArray());
            }

            await using (var cleanup = await config.CreateProviderAsync())
            {
                await cleanup.DeleteAsync(objectPath);
            }
        }
    }

    [Fact]
    public async Task Upload_ReportsProgress()
    {
        var configs = TransferOssTestConfig.ReadFilled();
        if (configs.Count == 0) return;

        foreach (var config in configs)
        {
            var store = new MemoryTransferStore();
            var payload = Encoding.UTF8.GetBytes($"Progress upload {config.DisplayName} {Guid.NewGuid():N}");
            var key = config.CreateObjectKey();
            var objectPath = new RemotePath($"{config.Bucket}/{key}", RemotePathKind.ObjectPath);

            await using (var provider = await config.CreateProviderAsync())
            {
                var worker = new TransferWorker(
                    new AnyAccountRepository(),
                    new FixedProviderFactory(provider),
                    new FixedContentLocalFileStore(payload),
                    store);

                var task = CreateUploadTask(config, key);
                var result = await worker.ExecuteAsync(task);

                Assert.True(result.IsSuccess, $"{config.DisplayName}: Upload failed. Error: {result.Error?.Category} - {result.Error?.Message} (code={result.Error?.Code} providerErrorCode={result.Error?.ProviderErrorCode})");
                var update = store.ProgressUpdates.LastOrDefault(u => u.TaskId == task.Id);
                Assert.NotNull(update.Progress);
                Assert.Equal(payload.Length, update.Progress.BytesTransferred);
            }

            // Clean up with a fresh provider (worker disposes its own)
            await using (var cleanup = await config.CreateProviderAsync())
            {
                await cleanup.DeleteAsync(objectPath);
            }
        }
    }

    [Fact]
    public async Task Download_ReportsProgress()
    {
        var configs = TransferOssTestConfig.ReadFilled();
        if (configs.Count == 0) return;

        foreach (var config in configs)
        {
            var payload = Encoding.UTF8.GetBytes($"Progress download {config.DisplayName} {Guid.NewGuid():N}");
            var key = config.CreateObjectKey();
            var objectPath = new RemotePath($"{config.Bucket}/{key}", RemotePathKind.ObjectPath);

            await using (var provider = await config.CreateProviderAsync())
            {
                await using (var upload = new MemoryStream(payload))
                {
                    var uploadResult = await provider.UploadAsync(objectPath, upload, upload.Length);
                    Assert.True(uploadResult.IsSuccess, $"{config.DisplayName}: Pre-upload failed. Error: {uploadResult.Error?.Category}");
                }

                var store = new MemoryTransferStore();
                using var downloadStream = new MemoryStream();
                var worker = new TransferWorker(
                    new AnyAccountRepository(),
                    new FixedProviderFactory(provider),
                    new CapturingWriteLocalFileStore(downloadStream),
                    store);

                var task = CreateDownloadTask(config, key);
                var result = await worker.ExecuteAsync(task);

                Assert.True(result.IsSuccess, $"{config.DisplayName}: Download failed. Error: {result.Error?.Category} - {result.Error?.Message}");
                var update = store.ProgressUpdates.LastOrDefault(u => u.TaskId == task.Id);
                Assert.NotNull(update.Progress);
                Assert.Equal(payload.Length, update.Progress.BytesTransferred);
            }

            await using (var cleanup = await config.CreateProviderAsync())
            {
                await cleanup.DeleteAsync(objectPath);
            }
        }
    }

    [Fact]
    public async Task UploadThenDownload_ContentMatches()
    {
        var configs = TransferOssTestConfig.ReadFilled();
        if (configs.Count == 0) return;

        foreach (var config in configs)
        {
            var store = new MemoryTransferStore();
            var original = Encoding.UTF8.GetBytes($"Roundtrip {config.DisplayName} {Guid.NewGuid():N}");
            var key = config.CreateObjectKey();
            var objectPath = new RemotePath($"{config.Bucket}/{key}", RemotePathKind.ObjectPath);

            var uploadProvider = await config.CreateProviderAsync();
            var uploadWorker = new TransferWorker(
                new AnyAccountRepository(),
                new FixedProviderFactory(uploadProvider),
                new FixedContentLocalFileStore(original),
                store);

            var uploadTask = CreateUploadTask(config, key);
            var uploadResult = await uploadWorker.ExecuteAsync(uploadTask);
            if (!uploadResult.IsSuccess)
            {
                var err = uploadResult.Error;
                System.Console.Error.WriteLine($"{config.DisplayName}: UploadThenDownload Upload FAILED. Category={err?.Category} Message={err?.Message} Code={err?.Code} ProviderErrorCode={err?.ProviderErrorCode} IsRetryable={err?.IsRetryable}");
            }
            Assert.True(uploadResult.IsSuccess, $"{config.DisplayName}: Upload failed. Error: {uploadResult.Error?.Category} - {uploadResult.Error?.Message}");

            try
            {
                var downloadStore = new MemoryTransferStore();
                using var downloaded = new MemoryStream();
                var downloadProvider = await config.CreateProviderAsync();
                var downloadWorker = new TransferWorker(
                    new AnyAccountRepository(),
                    new FixedProviderFactory(downloadProvider),
                    new CapturingWriteLocalFileStore(downloaded),
                    downloadStore);

                var downloadTask = CreateDownloadTask(config, key);
                var downloadResult = await downloadWorker.ExecuteAsync(downloadTask);

                Assert.True(downloadResult.IsSuccess, $"{config.DisplayName}: Download failed. Error: {downloadResult.Error?.Category} - {downloadResult.Error?.Message}");
                Assert.Equal(original, downloaded.ToArray());

                // Clean up remote object using a fresh provider (worker disposes its own)
                var cleanup = await config.CreateProviderAsync();
                await cleanup.DeleteAsync(objectPath);
                await cleanup.DisposeAsync();
            }
            catch
            {
                var cleanup = await config.CreateProviderAsync();
                await cleanup.DeleteAsync(objectPath);
                await cleanup.DisposeAsync();
                throw;
            }
        }
    }

    private static TransferTask CreateUploadTask(TransferOssTestConfig config, string key)
    {
        return new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\upload.txt"),
            new RemotePath($"{config.Bucket}/{key}", RemotePathKind.ObjectPath),
            TransferStatus.Pending,
            new TransferOptions(TransferOverwritePolicy.Overwrite),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private static TransferTask CreateDownloadTask(TransferOssTestConfig config, string key)
    {
        return new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Download,
            new LocalPath(@"C:\download.txt"),
            new RemotePath($"{config.Bucket}/{key}", RemotePathKind.ObjectPath),
            TransferStatus.Pending,
            new TransferOptions(TransferOverwritePolicy.Overwrite),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private sealed class FixedContentLocalFileStore : ILocalTransferFileStore
    {
        private readonly byte[] _content;
        public FixedContentLocalFileStore(byte[] content) => _content = content;

        public Task<OperationResult<LocalTransferReadHandle>> OpenReadAsync(
            LocalPath path, CancellationToken ct = default)
        {
            return Task.FromResult(OperationResult<LocalTransferReadHandle>.Success(
                new LocalTransferReadHandle(new MemoryStream(_content), _content.Length)));
        }

        public Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
            LocalPath path, CancellationToken ct = default)
        {
            return Task.FromResult(OperationResult<LocalTransferWriteHandle>.Success(
                new LocalTransferWriteHandle(new MemoryStream())));
        }
    }

    private sealed class CapturingWriteLocalFileStore : ILocalTransferFileStore
    {
        private readonly MemoryStream _target;
        public CapturingWriteLocalFileStore(MemoryStream target) => _target = target;

        public Task<OperationResult<LocalTransferReadHandle>> OpenReadAsync(
            LocalPath path, CancellationToken ct = default)
        {
            return Task.FromResult(OperationResult<LocalTransferReadHandle>.Failure(
                StorageError.NotFound("Not supported")));
        }

        public Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
            LocalPath path, CancellationToken ct = default)
        {
            return Task.FromResult(OperationResult<LocalTransferWriteHandle>.Success(
                new LocalTransferWriteHandle(_target)));
        }
    }
}

public sealed record TransferOssTestConfig(
    StorageProviderId ProviderId,
    string DisplayName,
    string Endpoint,
    string? Region,
    string Bucket,
    string TestFolderPrefix,
    Func<ICredentialStore> CredentialStoreFactory,
    Func<ProviderDescriptor, ProviderCredentialResolver, IStorageProviderCreator> CreatorFactory)
{
    public string CreateObjectKey()
    {
        return $"{TestFolderPrefix}transfer-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.txt";
    }

    public async Task<IStorageProvider> CreateProviderAsync()
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == ProviderId);
        var credentialStore = CredentialStoreFactory();
        var resolver = new ProviderCredentialResolver(credentialStore);
        var creator = CreatorFactory(descriptor, resolver);

        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            ProviderId,
            $"{DisplayName} Transfer {now:O}",
            Endpoint,
            Region,
            new CredentialRef($"transfer-{DisplayName.ToLowerInvariant().Replace(' ', '-')}"),
            now,
            now,
            new Dictionary<string, string> { ["bucket"] = Bucket });

        var result = await creator.CreateAsync(account).ConfigureAwait(false);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to create {DisplayName} provider: {result.Error?.Category} - {result.Error?.Message}");
        }

        return result.GetValueOrThrow();
    }

    public static IReadOnlyList<TransferOssTestConfig> ReadFilled()
    {
        var items = new List<TransferOssTestConfig>();
        AddIfFilled(items, TryReadAliyun());
        AddIfFilled(items, TryReadTencent());
        AddIfFilled(items, TryReadQiniu());
        AddIfFilled(items, TryReadUpyun());
        AddIfFilled(items, TryReadHuawei());
        AddIfFilled(items, TryReadVolcengine());
        return items;
    }

    private static TransferOssTestConfig? TryReadAliyun()
    {
        var raw = TryReadEnvVars("ATOMBOX_TEST_ALIYUN_OSS",
            "ATOMBOX_OSS_ENDPOINT", "ATOMBOX_OSS_REGION", "ATOMBOX_OSS_BUCKET",
            "ATOMBOX_OSS_ACCESS_KEY_ID", "ATOMBOX_OSS_ACCESS_KEY_SECRET",
            "ATOMBOX_OSS_TEST_PREFIX", hasRegion: true);
        if (raw is null) return null;

        return new TransferOssTestConfig(
            StorageProviderRegistry.AliyunOssProviderId, "Aliyun OSS",
            raw.Endpoint, raw.Region, raw.Bucket, raw.Prefix,
            () => new OssSecretStore("aliyun-oss-transfer",
                ("accessKeyId", raw.AccessKeyId), ("accessKeySecret", raw.AccessKeySecret)),
            (desc, resolver) => new AliyunOssProviderCreator(desc, resolver));
    }

    private static TransferOssTestConfig? TryReadTencent()
    {
        var raw = TryReadEnvVars("ATOMBOX_TEST_TENCENT_COS",
            "ATOMBOX_COS_ENDPOINT", "ATOMBOX_COS_REGION", "ATOMBOX_COS_BUCKET",
            "ATOMBOX_COS_SECRET_ID", "ATOMBOX_COS_SECRET_KEY",
            "ATOMBOX_COS_TEST_PREFIX", hasRegion: true);
        if (raw is null) return null;

        return new TransferOssTestConfig(
            StorageProviderRegistry.TencentCosProviderId, "Tencent COS",
            raw.Endpoint, raw.Region, raw.Bucket, raw.Prefix,
            () => new OssSecretStore("tencent-cos-transfer",
                ("accessKeyId", raw.AccessKeyId), ("accessKeySecret", raw.AccessKeySecret)),
            (desc, resolver) => new TencentCosProviderCreator(desc, resolver));
    }

    private static TransferOssTestConfig? TryReadQiniu()
    {
        var raw = TryReadEnvVars("ATOMBOX_TEST_QINIU_KODO",
            "ATOMBOX_QINIU_KODO_ENDPOINT", "ATOMBOX_QINIU_KODO_REGION", "ATOMBOX_QINIU_KODO_BUCKET",
            "ATOMBOX_QINIU_KODO_ACCESS_KEY", "ATOMBOX_QINIU_KODO_SECRET_KEY",
            "ATOMBOX_QINIU_KODO_TEST_PREFIX", hasRegion: false);
        if (raw is null) return null;

        return new TransferOssTestConfig(
            StorageProviderRegistry.QiniuKodoProviderId, "Qiniu Kodo",
            raw.Endpoint, raw.Region, raw.Bucket, raw.Prefix,
            () => new OssSecretStore("qiniu-kodo-transfer",
                ("accessKeyId", raw.AccessKeyId), ("accessKeySecret", raw.AccessKeySecret)),
            (desc, resolver) => new QiniuKodoProviderCreator(desc, resolver));
    }

    private static TransferOssTestConfig? TryReadUpyun()
    {
        var raw = TryReadEnvVars("ATOMBOX_TEST_UPYUN",
            "ATOMBOX_UPYUN_ENDPOINT", null, "ATOMBOX_UPYUN_SERVICE",
            "ATOMBOX_UPYUN_OPERATOR", "ATOMBOX_UPYUN_PASSWORD",
            "ATOMBOX_UPYUN_TEST_PREFIX", hasRegion: false);
        if (raw is null) return null;

        return new TransferOssTestConfig(
            StorageProviderRegistry.UpyunProviderId, "Upyun USS",
            raw.Endpoint, null, raw.Bucket, raw.Prefix,
            () => new OssSecretStore("upyun-transfer",
                ("accessKeyId", raw.AccessKeyId), ("accessKeySecret", raw.AccessKeySecret)),
            (desc, resolver) => new UpyunProviderCreator(desc, resolver));
    }

    private static TransferOssTestConfig? TryReadHuawei()
    {
        var raw = TryReadEnvVars("ATOMBOX_TEST_HUAWEI_OBS",
            "ATOMBOX_HUAWEI_OBS_ENDPOINT", "ATOMBOX_HUAWEI_OBS_REGION", "ATOMBOX_HUAWEI_OBS_BUCKET",
            "ATOMBOX_HUAWEI_OBS_ACCESS_KEY_ID", "ATOMBOX_HUAWEI_OBS_ACCESS_KEY_SECRET",
            "ATOMBOX_HUAWEI_OBS_TEST_PREFIX", hasRegion: true);
        if (raw is null) return null;

        return new TransferOssTestConfig(
            StorageProviderRegistry.HuaweiObsProviderId, "Huawei OBS",
            raw.Endpoint, raw.Region, raw.Bucket, raw.Prefix,
            () => new OssSecretStore("huawei-obs-transfer",
                ("accessKeyId", raw.AccessKeyId), ("accessKeySecret", raw.AccessKeySecret)),
            (desc, resolver) => new HuaweiObsProviderCreator(desc, resolver));
    }

    private static TransferOssTestConfig? TryReadVolcengine()
    {
        var raw = TryReadEnvVars("ATOMBOX_TEST_VOLCENGINE_TOS",
            "ATOMBOX_VOLCENGINE_TOS_ENDPOINT", "ATOMBOX_VOLCENGINE_TOS_REGION", "ATOMBOX_VOLCENGINE_TOS_BUCKET",
            "ATOMBOX_VOLCENGINE_TOS_ACCESS_KEY_ID", "ATOMBOX_VOLCENGINE_TOS_ACCESS_KEY_SECRET",
            "ATOMBOX_VOLCENGINE_TOS_TEST_PREFIX", hasRegion: true);
        if (raw is null) return null;

        return new TransferOssTestConfig(
            StorageProviderRegistry.VolcengineTosProviderId, "Volcengine TOS",
            raw.Endpoint, raw.Region, raw.Bucket, raw.Prefix,
            () => new OssSecretStore("volcengine-tos-transfer",
                ("accessKeyId", raw.AccessKeyId), ("accessKeySecret", raw.AccessKeySecret)),
            (desc, resolver) => new VolcengineTosProviderCreator(desc, resolver));
    }

    private sealed record RawEnvVars(
        string Endpoint, string? Region, string Bucket,
        string AccessKeyId, string AccessKeySecret, string Prefix);

    private static RawEnvVars? TryReadEnvVars(
        string enabledName, string endpointName, string? regionName,
        string bucketName, string accessKeyIdName, string accessKeySecretName,
        string prefixName, bool hasRegion)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(enabledName), "1", StringComparison.Ordinal))
        {
            return null;
        }

        var endpoint = Environment.GetEnvironmentVariable(endpointName);
        var region = hasRegion && regionName is not null ? Environment.GetEnvironmentVariable(regionName) : null;
        var bucket = Environment.GetEnvironmentVariable(bucketName);
        var accessKeyId = Environment.GetEnvironmentVariable(accessKeyIdName);
        var accessKeySecret = Environment.GetEnvironmentVariable(accessKeySecretName);
        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(bucket) ||
            string.IsNullOrWhiteSpace(accessKeyId) ||
            string.IsNullOrWhiteSpace(accessKeySecret) ||
            (hasRegion && string.IsNullOrWhiteSpace(region)))
        {
            return null;
        }

        var prefix = Environment.GetEnvironmentVariable(prefixName);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "transfer-integration-tests/";
        }

        return new RawEnvVars(
            endpoint.Trim(),
            region?.Trim(),
            bucket.Trim(),
            accessKeyId.Trim(),
            accessKeySecret.Trim(),
            NormalizePrefix(prefix));
    }

    private static void AddIfFilled(List<TransferOssTestConfig> items, TransferOssTestConfig? item)
    {
        if (item is not null) items.Add(item);
    }

    private static string NormalizePrefix(string prefix)
    {
        return prefix.Trim().TrimStart('/').TrimEnd('/') + "/";
    }
}

internal sealed class OssSecretStore : ICredentialStore
{
    private readonly CredentialRef _ref;
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public OssSecretStore(string credentialName, params (string key, string value)[] secrets)
    {
        _ref = new CredentialRef(credentialName);
        foreach (var (key, value) in secrets)
        {
            _secrets[key] = value;
        }
    }

    public Task<OperationResult<CredentialRef>> SaveAsync(
        CredentialSecretMaterial material, CancellationToken ct = default)
    {
        return Task.FromResult(OperationResult<CredentialRef>.Success(_ref));
    }

    public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
        CredentialRef credentialRef, CancellationToken ct = default)
    {
        return Task.FromResult(OperationResult<CredentialLease>.Success(
            new TransferCredentialLease(_ref)));
    }

    public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
        CredentialRef credentialRef, CancellationToken ct = default)
    {
        var material = new CredentialSecretMaterial(new Dictionary<string, string>(_secrets));
        return Task.FromResult(OperationResult<CredentialMaterialLease>.Success(
            new CredentialMaterialLease(new TransferCredentialLease(_ref), material)));
    }

    public Task<OperationResult<bool>> ExistsAsync(
        CredentialRef credentialRef, CancellationToken ct = default)
    {
        return Task.FromResult(OperationResult<bool>.Success(credentialRef == _ref));
    }

    public Task<OperationResult> MarkPendingDeleteAsync(
        CredentialRef credentialRef, CancellationToken ct = default)
    {
        return Task.FromResult(OperationResult.Success());
    }
}

internal sealed class TransferCredentialLease : CredentialLease
{
    public TransferCredentialLease(CredentialRef credentialRef)
        : base(credentialRef, "transfer-test")
    {
    }

    public override ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
