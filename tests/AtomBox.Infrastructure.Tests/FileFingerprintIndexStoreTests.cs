using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Fingerprints;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class FileFingerprintIndexStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task JsonStore_AddFindStatsAndClear_Roundtrips()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new JsonFileFingerprintIndexStore(paths);
        var accountId = StorageAccountId.New();
        var record = new FileFingerprintRecord(
            "SHA256",
            "ABCDEF",
            123,
            accountId,
            new StorageProviderId("sftp"),
            new RemotePath("backup/a.txt"),
            DateTimeOffset.UtcNow);

        var addResult = await store.AddOrUpdateAsync(record);
        Assert.True(addResult.IsSuccess);
        Assert.True(File.Exists(paths.FileFingerprintIndexFile));

        var findResult = await store.FindAsync(new FileFingerprintQuery("sha256", "abcdef", 123, accountId));
        Assert.True(findResult.IsSuccess);
        var found = Assert.Single(findResult.GetValueOrThrow());
        Assert.Equal(new RemotePath("backup/a.txt"), found.RemotePath);

        var stats = await store.GetStatisticsAsync();
        Assert.True(stats.IsSuccess);
        Assert.Equal(paths.FileFingerprintIndexFile, stats.GetValueOrThrow().IndexFilePath);
        Assert.Equal(1, stats.GetValueOrThrow().RecordCount);
        Assert.NotNull(stats.GetValueOrThrow().LastUpdatedAt);

        var clearResult = await store.ClearAsync();
        Assert.True(clearResult.IsSuccess);
        var clearedStats = await store.GetStatisticsAsync();
        Assert.Equal(0, clearedStats.GetValueOrThrow().RecordCount);
    }

    [Fact]
    public async Task JsonStore_Find_IsScopedToStorageAccountAndCanReturnMultipleTargets()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new JsonFileFingerprintIndexStore(paths);
        var accountId = StorageAccountId.New();
        var otherAccountId = StorageAccountId.New();

        await store.AddOrUpdateAsync(new FileFingerprintRecord(
            "sha256",
            "abcdef",
            123,
            accountId,
            new StorageProviderId("sftp"),
            new RemotePath("backup/a.txt"),
            DateTimeOffset.UtcNow));
        await store.AddOrUpdateAsync(new FileFingerprintRecord(
            "sha256",
            "abcdef",
            123,
            accountId,
            new StorageProviderId("sftp"),
            new RemotePath("backup/b.txt"),
            DateTimeOffset.UtcNow));
        await store.AddOrUpdateAsync(new FileFingerprintRecord(
            "sha256",
            "abcdef",
            123,
            otherAccountId,
            new StorageProviderId("sftp"),
            new RemotePath("backup/other.txt"),
            DateTimeOffset.UtcNow));

        var findResult = await store.FindAsync(new FileFingerprintQuery("sha256", "abcdef", 123, accountId));

        Assert.True(findResult.IsSuccess);
        var matches = findResult.GetValueOrThrow();
        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, item => item.RemotePath == new RemotePath("backup/a.txt"));
        Assert.Contains(matches, item => item.RemotePath == new RemotePath("backup/b.txt"));
        Assert.DoesNotContain(matches, item => item.StorageAccountId == otherAccountId);
    }

    [Fact]
    public async Task JsonStore_CorruptJson_ReturnsFailure()
    {
        var paths = new AtomBoxStoragePaths(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.FileFingerprintIndexFile)!);
        await File.WriteAllTextAsync(paths.FileFingerprintIndexFile, "{ broken json");
        var store = new JsonFileFingerprintIndexStore(paths);

        var result = await store.GetStatisticsAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Decorator_UploadSucceededWithFingerprint_WritesIndex()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var inner = new TransferStateStore(taskStore, paths);
        var fingerprints = new JsonFileFingerprintIndexStore(paths);
        var accountId = StorageAccountId.New();
        var decorator = new FingerprintAwareTransferStateStoreDecorator(
            inner,
            fingerprints,
            new FixedSettingsRepository(enabled: true),
            new FixedAccountRepository(CreateAccount(accountId)));
        var task = CreateTask(accountId, TransferDirection.Upload, TransferStatus.Succeeded)
            .WithFingerprintMetadata("sha256", "abcdef", 10, DateTimeOffset.UtcNow);

        var result = await decorator.UpdateStatusAsync(task, new TransferProgress(10, 10, null));
        Assert.True(result.IsSuccess);

        var matches = await fingerprints.FindAsync(new FileFingerprintQuery("sha256", "abcdef", 10, accountId));
        Assert.True(matches.IsSuccess);
        var record = Assert.Single(matches.GetValueOrThrow());
        Assert.Equal(new StorageProviderId("sftp"), record.ProviderId);
        Assert.Equal(task.RemotePath, record.RemotePath);
    }

    [Fact]
    public async Task Decorator_NonUploadOrSettingsDisabled_DoesNotWriteIndex()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var inner = new TransferStateStore(taskStore, paths);
        var fingerprints = new JsonFileFingerprintIndexStore(paths);
        var accountId = StorageAccountId.New();
        var decorator = new FingerprintAwareTransferStateStoreDecorator(
            inner,
            fingerprints,
            new FixedSettingsRepository(enabled: false),
            new FixedAccountRepository(CreateAccount(accountId)));
        var task = CreateTask(accountId, TransferDirection.Upload, TransferStatus.Succeeded)
            .WithFingerprintMetadata("sha256", "abcdef", 10, DateTimeOffset.UtcNow);

        var result = await decorator.UpdateStatusAsync(task, null);
        Assert.True(result.IsSuccess);

        var matches = await fingerprints.FindAsync(new FileFingerprintQuery("sha256", "abcdef", 10, accountId));
        Assert.True(matches.IsSuccess);
        Assert.Empty(matches.GetValueOrThrow());
    }

    [Fact]
    public async Task Decorator_AccountLookupFailure_DoesNotWriteIndex()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var inner = new TransferStateStore(taskStore, paths);
        var fingerprints = new JsonFileFingerprintIndexStore(paths);
        var accountId = StorageAccountId.New();
        var decorator = new FingerprintAwareTransferStateStoreDecorator(
            inner,
            fingerprints,
            new FixedSettingsRepository(enabled: true),
            new FixedAccountRepository(CreateAccount(StorageAccountId.New())));
        var task = CreateTask(accountId, TransferDirection.Upload, TransferStatus.Succeeded)
            .WithFingerprintMetadata("sha256", "abcdef", 10, DateTimeOffset.UtcNow);

        var result = await decorator.UpdateStatusAsync(task, null);

        Assert.True(result.IsSuccess);
        var matches = await fingerprints.FindAsync(new FileFingerprintQuery("sha256", "abcdef", 10, accountId));
        Assert.True(matches.IsSuccess);
        Assert.Empty(matches.GetValueOrThrow());
    }

    [Fact]
    public async Task Decorator_IndexWriteFailure_DoesNotFailStateUpdate()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var inner = new TransferStateStore(taskStore, paths);
        var fingerprints = new FailingFingerprintIndexStore();
        var accountId = StorageAccountId.New();
        var decorator = new FingerprintAwareTransferStateStoreDecorator(
            inner,
            fingerprints,
            new FixedSettingsRepository(enabled: true),
            new FixedAccountRepository(CreateAccount(accountId)));
        var task = CreateTask(accountId, TransferDirection.Upload, TransferStatus.Succeeded)
            .WithFingerprintMetadata("sha256", "abcdef", 10, DateTimeOffset.UtcNow);

        var result = await decorator.UpdateStatusAsync(task, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fingerprints.AddAttempts);
    }

    [Fact]
    public async Task Decorator_InnerSaveFailure_DoesNotWriteIndex()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var inner = new TransferStateStore(new FailingTransferTaskStore(), paths);
        var fingerprints = new CountingFingerprintIndexStore();
        var accountId = StorageAccountId.New();
        var decorator = new FingerprintAwareTransferStateStoreDecorator(
            inner,
            fingerprints,
            new FixedSettingsRepository(enabled: true),
            new FixedAccountRepository(CreateAccount(accountId)));
        var task = CreateTask(accountId, TransferDirection.Upload, TransferStatus.Succeeded)
            .WithFingerprintMetadata("sha256", "abcdef", 10, DateTimeOffset.UtcNow);

        var result = await decorator.UpdateStatusAsync(task, null);

        Assert.True(result.IsFailure);
        Assert.Equal(0, fingerprints.AddAttempts);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static TransferTask CreateTask(
        StorageAccountId accountId,
        TransferDirection direction,
        TransferStatus status)
    {
        return new TransferTask(
            TransferTaskId.New(),
            accountId,
            direction,
            direction == TransferDirection.Upload ? new LocalPath(@"C:\test.txt") : new LocalPath(@"C:\download.txt"),
            new RemotePath("bucket/test.txt", RemotePathKind.ObjectPath),
            status,
            new TransferOptions(TransferOverwritePolicy.Ask),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private static StorageAccount CreateAccount(StorageAccountId accountId)
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            accountId,
            StorageProviderCategory.FileTransfer,
            new StorageProviderId("sftp"),
            "SFTP",
            "sftp.example.com",
            null,
            new CredentialRef("credential"),
            now,
            now);
    }

    private class CountingFingerprintIndexStore : IFileFingerprintIndexStore
    {
        public int AddAttempts { get; protected set; }

        public Task<OperationResult<IReadOnlyList<FileFingerprintRecord>>> FindAsync(
            FileFingerprintQuery query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OperationResult<IReadOnlyList<FileFingerprintRecord>>>(
                OperationResult<IReadOnlyList<FileFingerprintRecord>>.Success(Array.Empty<FileFingerprintRecord>()));
        }

        public virtual Task<OperationResult> AddOrUpdateAsync(
            FileFingerprintRecord record,
            CancellationToken cancellationToken = default)
        {
            AddAttempts++;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult<FileFingerprintIndexStatistics>> GetStatisticsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<FileFingerprintIndexStatistics>.Success(
                new FileFingerprintIndexStatistics(string.Empty, 0, null)));
        }

        public Task<OperationResult> ClearAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FailingFingerprintIndexStore : CountingFingerprintIndexStore
    {
        public override Task<OperationResult> AddOrUpdateAsync(
            FileFingerprintRecord record,
            CancellationToken cancellationToken = default)
        {
            AddAttempts++;
            return Task.FromResult(OperationResult.Failure(StorageError.Unknown("index write failed")));
        }
    }

    private sealed class FailingTransferTaskStore : ITransferTaskStore
    {
        public Task<OperationResult<TransferTask>> GetByIdAsync(
            TransferTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<TransferTask>.Failure(StorageError.Unknown("task save failed")));
        }

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OperationResult<IReadOnlyList<TransferTask>>>(
                OperationResult<IReadOnlyList<TransferTask>>.Success(Array.Empty<TransferTask>()));
        }

        public Task<OperationResult> SaveAsync(
            TransferTask task,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Failure(StorageError.Unknown("task save failed")));
        }

        public Task<OperationResult> DeleteAsync(
            TransferTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FixedSettingsRepository : IApplicationSettingsRepository
    {
        private readonly bool _enabled;

        public FixedSettingsRepository(bool enabled)
        {
            _enabled = enabled;
        }

        public Task<OperationResult<ApplicationSettings>> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<ApplicationSettings>.Success(
                new ApplicationSettings(3, TransferOverwritePolicy.Ask, true, _enabled)));
        }

        public Task<OperationResult> SaveAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FixedAccountRepository : IStorageAccountRepository
    {
        private readonly StorageAccount _account;

        public FixedAccountRepository(StorageAccount account)
        {
            _account = account;
        }

        public Task<OperationResult<StorageAccount>> GetByIdAsync(
            StorageAccountId accountId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(accountId == _account.Id
                ? OperationResult<StorageAccount>.Success(_account)
                : OperationResult<StorageAccount>.Failure(StorageError.NotFound("not found")));
        }

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(
                OperationResult<IReadOnlyList<StorageAccount>>.Success([_account]));
        }

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }
}
