using System.Text.Json;
using AtomBox.Application.Accounts;
using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Application.Tests;

public sealed class AccountAppServiceDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.App.DataTests", Guid.NewGuid().ToString("N"));
    private static readonly CancellationToken CT = CancellationToken.None;

    [Fact]
    public async Task AddAsync_PersistsAccountToJsonFile()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var service = new AccountAppService(
            new StorageAccountRepository(paths),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "My OSS",
            "oss-cn-hangzhou.aliyuncs.com",
            "cn-hangzhou",
            new CredentialRef("cred-1"),
            new Dictionary<string, string> { ["bucket"] = "assets" }));

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(paths.AccountsFile));
        var raw = await File.ReadAllTextAsync(paths.AccountsFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
        Assert.True(parsed.GetArrayLength() >= 1);
        Assert.Equal("My OSS", parsed[0].GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task AddThenGetByIdAsync_MatchesPersistedData()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var service = new AccountAppService(
            repo, new EmptyTransferTaskStore(), new ThrowingProviderFactory());

        var addResult = await service.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Persisted OSS",
            "endpoint.com",
            "region-1",
            new CredentialRef("cred-1")));

        Assert.True(addResult.IsSuccess);
        var summary = addResult.GetValueOrThrow();

        var getResult = await repo.GetByIdAsync(summary.Id);
        Assert.True(getResult.IsSuccess);
        Assert.Equal("Persisted OSS", getResult.GetValueOrThrow().DisplayName);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesJsonFile()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var service = new AccountAppService(
            repo, new EmptyTransferTaskStore(), new ThrowingProviderFactory());

        var addResult = await service.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Original",
            "old-endpoint.com",
            null,
            new CredentialRef("cred-1")));

        var accountId = addResult.GetValueOrThrow().Id;

        var updateResult = await service.UpdateAsync(new UpdateStorageAccountRequest(
            accountId,
            "Updated",
            "new-endpoint.com",
            "new-region",
            new CredentialRef("cred-2")));

        Assert.True(updateResult.IsSuccess);
        Assert.Equal("Updated", updateResult.GetValueOrThrow().DisplayName);
        Assert.Equal("new-endpoint.com", updateResult.GetValueOrThrow().Endpoint);

        var raw = await File.ReadAllTextAsync(paths.AccountsFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal("Updated", parsed[0].GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task DeleteAsync_WithFinishedTasksOnly_RemovesFromJson()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var accountId = StorageAccountId.New();
        var now = DateTimeOffset.UtcNow;
        var finishedTask = new TransferTask(
            TransferTaskId.New(), accountId, TransferDirection.Upload,
            new LocalPath(@"C:\old.txt"), new RemotePath("bucket/old.txt"),
            TransferStatus.Succeeded, new TransferOptions(TransferOverwritePolicy.Ask),
            now, now);
        var taskStore = new SingleTaskStore(finishedTask);
        var service = new AccountAppService(
            repo, taskStore, new ThrowingProviderFactory());

        await repo.AddAsync(new StorageAccount(
            accountId, StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"), "To Delete",
            null, null, new CredentialRef("cred-1"),
            now, now));

        var result = await service.DeleteAsync(new DeleteStorageAccountRequest(accountId));

        Assert.True(result.IsSuccess);
        var listResult = await repo.ListAsync();
        Assert.Empty(listResult.GetValueOrThrow());
    }

    [Fact]
    public async Task DeleteAsync_WithUnfinishedTasks_ReturnsConflict()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var accountId = StorageAccountId.New();
        var now = DateTimeOffset.UtcNow;
        var runningTask = new TransferTask(
            TransferTaskId.New(), accountId, TransferDirection.Upload,
            new LocalPath(@"C:\active.txt"), new RemotePath("bucket/active.txt"),
            TransferStatus.Running, new TransferOptions(TransferOverwritePolicy.Ask),
            now, now);
        var taskStore = new SingleTaskStore(runningTask);
        var service = new AccountAppService(
            repo, taskStore, new ThrowingProviderFactory());

        await repo.AddAsync(new StorageAccount(
            accountId, StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"), "Active Account",
            null, null, new CredentialRef("cred-1"), now, now));

        var result = await service.DeleteAsync(new DeleteStorageAccountRequest(accountId));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task ListAsync_FilterByCategory_PersistedAccounts()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var service = new AccountAppService(
            repo, new EmptyTransferTaskStore(), new ThrowingProviderFactory());
        var now = DateTimeOffset.UtcNow;

        await repo.AddAsync(new StorageAccount(
            StorageAccountId.New(), StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"), "OSS",
            null, null, new CredentialRef("cred-1"), now, now));
        await repo.AddAsync(new StorageAccount(
            StorageAccountId.New(), StorageProviderCategory.FileTransfer,
            new StorageProviderId("sftp"), "SFTP",
            null, null, new CredentialRef("cred-2"), now, now));

        var allResult = await service.ListAsync(new ListStorageAccountsRequest());
        Assert.True(allResult.IsSuccess);
        Assert.Equal(2, allResult.GetValueOrThrow().Count);

        var filteredResult = await service.ListAsync(
            new ListStorageAccountsRequest(StorageProviderCategory.ObjectStorage));
        Assert.True(filteredResult.IsSuccess);
        Assert.Single(filteredResult.GetValueOrThrow());
        Assert.Equal(StorageProviderCategory.ObjectStorage,
            filteredResult.GetValueOrThrow()[0].ProviderCategory);
    }

    [Fact]
    public async Task UpdateAsync_WithProviderConfig_PersistsToJson()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var service = new AccountAppService(
            repo, new EmptyTransferTaskStore(), new ThrowingProviderFactory());

        var addResult = await service.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "With Config",
            null,
            null,
            new CredentialRef("cred-1"),
            new Dictionary<string, string> { ["bucket"] = "old-bucket", ["region"] = "old-region" }));

        var accountId = addResult.GetValueOrThrow().Id;

        var updateResult = await service.UpdateAsync(new UpdateStorageAccountRequest(
            accountId,
            "With Config Updated",
            "new-endpoint.com",
            "new-region",
            new CredentialRef("cred-2"),
            new Dictionary<string, string> { ["bucket"] = "new-bucket", ["customKey"] = "customVal" }));

        Assert.True(updateResult.IsSuccess);
        var summary = updateResult.GetValueOrThrow();
        Assert.Equal("new-bucket", summary.ProviderConfig["bucket"]);
        Assert.Equal("customVal", summary.ProviderConfig["customKey"]);
        Assert.Equal("new-endpoint.com", summary.Endpoint);
        Assert.Equal("new-region", summary.Region);

        var raw = await File.ReadAllTextAsync(paths.AccountsFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        var providerConfig = parsed[0].GetProperty("providerConfig");
        Assert.Equal("new-bucket", providerConfig.GetProperty("bucket").GetString());
        Assert.Equal("customVal", providerConfig.GetProperty("customKey").GetString());
        Assert.Equal("new-endpoint.com", parsed[0].GetProperty("endpoint").GetString());
    }

    [Fact]
    public async Task TestConnectionDraftAsync_WithPersistedRepo_ProbesViaDraft()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var providerFactory = new ProbeProviderFactory(
            OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
        var service = new AccountAppService(
            repo, new EmptyTransferTaskStore(), providerFactory);

        var result = await service.TestConnectionDraftAsync(new TestConnectionDraftRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Draft Account",
            "draft-endpoint.com",
            "draft-region",
            new CredentialRef("cred-draft"),
            new Dictionary<string, string> { ["bucket"] = "draft-bucket" }));

        Assert.True(result.IsSuccess);
        Assert.True(result.GetValueOrThrow().IsAvailable);
        Assert.Equal(1, providerFactory.Provider.ListCalls);
        Assert.Equal("draft-bucket", providerFactory.Provider.LastPath?.ToString());
        Assert.Equal("draft-endpoint.com", result.GetValueOrThrow().Endpoint);
        Assert.Equal("draft-region", result.GetValueOrThrow().Region);

        Assert.False(File.Exists(paths.AccountsFile));
    }

    [Fact]
    public async Task TestConnectionAsync_WithPersistedAccount_ProbesProvider()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var accountId = StorageAccountId.New();
        var now = DateTimeOffset.UtcNow;
        await repo.AddAsync(new StorageAccount(
            accountId, StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"), "OSS",
            "oss-cn-hangzhou.aliyuncs.com", null,
            new CredentialRef("cred-1"), now, now));

        var providerFactory = new ProbeProviderFactory(
            OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
        var service = new AccountAppService(
            repo, new EmptyTransferTaskStore(), providerFactory);

        var result = await service.TestConnectionAsync(new TestConnectionRequest(accountId));

        Assert.True(result.IsSuccess);
        Assert.True(result.GetValueOrThrow().IsAvailable);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class EmptyTransferTaskStore : ITransferTaskStore
    {
        public Task<OperationResult<TransferTask>> GetByIdAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<TransferTask>.Failure(StorageError.NotFound("not found")));
        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferTask>>>(OperationResult<IReadOnlyList<TransferTask>>.Success([]));
        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class SingleTaskStore : ITransferTaskStore
    {
        private readonly TransferTask _task;
        public SingleTaskStore(TransferTask task) { _task = task; }

        public Task<OperationResult<TransferTask>> GetByIdAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(taskId == _task.Id
                ? OperationResult<TransferTask>.Success(_task)
                : OperationResult<TransferTask>.Failure(StorageError.NotFound("not found")));
        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferTask>>>(
                OperationResult<IReadOnlyList<TransferTask>>.Success([_task]));
        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class ThrowingProviderFactory : IStorageProviderFactory
    {
        public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Should not be called.");
    }

    private sealed class ProbeProviderFactory : IStorageProviderFactory
    {
        public ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>> probeResult)
        {
            Provider = new ProbeStorageProvider(probeResult);
        }
        public ProbeStorageProvider Provider { get; }

        public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IStorageProvider>.Success(Provider));
    }

    private sealed class ProbeStorageProvider : IStorageProvider
    {
        private readonly OperationResult<IReadOnlyList<RemoteItem>> _probeResult;
        public ProbeStorageProvider(OperationResult<IReadOnlyList<RemoteItem>> probeResult) { _probeResult = probeResult; }
        public int ListCalls { get; private set; }
        public RemotePath? LastPath { get; private set; }
        public StorageCapabilitySet Capabilities => StorageCapabilitySet.Empty;

        public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(RemotePath path, CancellationToken cancellationToken = default)
        {
            ListCalls++;
            LastPath = path;
            return Task.FromResult(_probeResult);
        }
        public Task<OperationResult> DeleteAsync(RemotePath path, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> UploadAsync(RemotePath path, Stream content, long? contentLength,
            IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> DownloadAsync(RemotePath path, Stream destination,
            IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
