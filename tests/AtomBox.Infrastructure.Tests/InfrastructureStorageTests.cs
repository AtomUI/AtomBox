using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class InfrastructureStorageTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), "AtomBox.Infrastructure.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplicationSettingsRepository_ReturnsDefaultSettings_WhenFileDoesNotExist()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        var repository = new ApplicationSettingsRepository(paths);

        var result = await repository.GetAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.GetValueOrThrow().DefaultConcurrency);
    }

    [Fact]
    public async Task ApplicationSettingsRepository_CreatesBackup_WhenOverwritingExistingFile()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        var repository = new ApplicationSettingsRepository(paths);
        await repository.SaveAsync(new ApplicationSettings(3, TransferOverwritePolicy.Ask, true));

        var result = await repository.SaveAsync(new ApplicationSettings(5, TransferOverwritePolicy.Overwrite, false));

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(paths.SettingsFile));
        Assert.True(File.Exists($"{paths.SettingsFile}.bak"));
    }

    [Fact]
    public async Task ApplicationSettingsRepository_BacksUpCorruptJson_AndReturnsFailure()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        await File.WriteAllTextAsync(paths.SettingsFile, "{ broken json");
        var repository = new ApplicationSettingsRepository(paths);

        var result = await repository.GetAsync();

        Assert.True(result.IsFailure);
        Assert.Contains(Directory.GetFiles(paths.ConfigurationDirectory, "settings.json.*.corrupt"), File.Exists);
    }

    [Fact]
    public async Task LocalTransferFileStore_ReturnsNotFound_WhenReadFileDoesNotExist()
    {
        var store = new LocalTransferFileStore();

        var result = await store.OpenReadAsync(new LocalPath(Path.Combine(_rootDirectory, "missing.txt")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task LocalTransferFileStore_CreatesDirectoryAndWritableFile()
    {
        var store = new LocalTransferFileStore();
        var path = Path.Combine(_rootDirectory, "nested", "download.txt");

        await using var handle = (await store.OpenWriteAsync(new LocalPath(path))).GetValueOrThrow();
        await handle.Stream.WriteAsync(new byte[] { 1, 2, 3 });

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task LocalTransferFileStore_ReturnsConflict_WhenWriteTargetAlreadyExists()
    {
        var store = new LocalTransferFileStore();
        var path = Path.Combine(_rootDirectory, "download.txt");
        Directory.CreateDirectory(_rootDirectory);
        await File.WriteAllTextAsync(path, "existing");

        var result = await store.OpenWriteAsync(new LocalPath(path));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
        Assert.Equal(StorageErrorCode.Conflict, result.Error?.Code);
    }

    [Fact]
    public async Task TransferStateStore_PersistsProgressSnapshotSeparatelyFromTask()
    {
        var paths = CreatePaths();
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var task = CreateTransferTask(TransferStatus.Running);
        var progress = new TransferProgress(512, 1024, 2048);

        var updateResult = await stateStore.UpdateStatusAsync(task, progress);
        var queueResult = await stateStore.ListQueueAsync();

        Assert.True(updateResult.IsSuccess);
        Assert.True(queueResult.IsSuccess);
        var snapshot = Assert.Single(queueResult.GetValueOrThrow());
        Assert.Equal(task.Id, snapshot.Task.Id);
        Assert.Equal(50, snapshot.Progress?.Percent);
        Assert.Equal(2048, snapshot.Progress?.SpeedBytesPerSecond);
        Assert.True(File.Exists(paths.TransferProgressFile));
    }

    [Fact]
    public async Task StorageAccountRepository_PersistsProviderConfig()
    {
        var paths = CreatePaths();
        var repository = new StorageAccountRepository(paths);
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Aliyun OSS",
            "oss-cn-hangzhou.aliyuncs.com",
            "cn-hangzhou",
            new CredentialRef("cred-1"),
            now,
            now,
            new Dictionary<string, string> { ["bucket"] = "assets" });

        var addResult = await repository.AddAsync(account);
        var readResult = await repository.GetByIdAsync(account.Id);

        Assert.True(addResult.IsSuccess);
        Assert.True(readResult.IsSuccess);
        Assert.Equal("assets", readResult.GetValueOrThrow().GetProviderConfigValue("bucket"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private AtomBoxStoragePaths CreatePaths()
    {
        return new AtomBoxStoragePaths(_rootDirectory);
    }

    private static TransferTask CreateTransferTask(TransferStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\upload.txt"),
            new RemotePath("bucket/upload.txt"),
            status,
            new TransferOptions(TransferOverwritePolicy.Ask),
            now,
            now);
    }
}
