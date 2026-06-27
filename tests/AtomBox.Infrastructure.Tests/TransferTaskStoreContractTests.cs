using AtomBox.Core.Errors;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class TransferTaskStoreContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));
    private readonly TransferTaskStore _store;

    public TransferTaskStoreContractTests()
    {
        _store = new TransferTaskStore(new AtomBoxStoragePaths(_root));
    }

    [Fact]
    public async Task List_WhenEmpty_ReturnsEmptyList()
    {
        var result = await _store.ListAsync();
        Assert.True(result.IsSuccess);
        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task SaveAndGetById_Roundtrips()
    {
        var task = CreateTask(TransferStatus.Pending);
        var saveResult = await _store.SaveAsync(task);
        Assert.True(saveResult.IsSuccess);

        var getResult = await _store.GetByIdAsync(task.Id);
        Assert.True(getResult.IsSuccess);
        Assert.Equal(task.Id, getResult.GetValueOrThrow().Id);
        Assert.Equal(TransferStatus.Pending, getResult.GetValueOrThrow().Status);
    }

    [Fact]
    public async Task Save_ReplaceExisting_UpdatesInPlace()
    {
        var task = CreateTask(TransferStatus.Pending);
        await _store.SaveAsync(task);

        var updated = task.WithStatus(TransferStatus.Running, DateTimeOffset.UtcNow);
        var replaceResult = await _store.SaveAsync(updated);
        Assert.True(replaceResult.IsSuccess);

        var getResult = await _store.GetByIdAsync(task.Id);
        Assert.True(getResult.IsSuccess);
        Assert.Equal(TransferStatus.Running, getResult.GetValueOrThrow().Status);
    }

    [Fact]
    public async Task List_ReturnsAllSavedTasks()
    {
        var t1 = CreateTask(TransferStatus.Pending);
        var t2 = CreateTask(TransferStatus.Succeeded);
        await _store.SaveAsync(t1);
        await _store.SaveAsync(t2);

        var listResult = await _store.ListAsync();
        Assert.True(listResult.IsSuccess);
        Assert.Equal(2, listResult.GetValueOrThrow().Count);
    }

    [Fact]
    public async Task Delete_ExistingTask_RemovesIt()
    {
        var task = CreateTask(TransferStatus.Pending);
        await _store.SaveAsync(task);

        var delResult = await _store.DeleteAsync(task.Id);
        Assert.True(delResult.IsSuccess);

        var getResult = await _store.GetByIdAsync(task.Id);
        Assert.True(getResult.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, getResult.Error?.Category);
    }

    [Fact]
    public async Task Delete_NonExistentTask_ReturnsNotFound()
    {
        var result = await _store.DeleteAsync(TransferTaskId.New());
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task GetById_NonExistentTask_ReturnsNotFound()
    {
        var result = await _store.GetByIdAsync(TransferTaskId.New());
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task SaveMultipleThenList_ContainsAll()
    {
        var tasks = Enumerable.Range(0, 5).Select(i => CreateTask((TransferStatus)(i % 4))).ToArray();
        foreach (var t in tasks)
            await _store.SaveAsync(t);

        var listResult = await _store.ListAsync();
        Assert.True(listResult.IsSuccess);
        Assert.Equal(5, listResult.GetValueOrThrow().Count);
    }

    [Fact]
    public async Task SaveAfterDelete_SameId_Succeeds()
    {
        var task = CreateTask(TransferStatus.Pending);
        await _store.SaveAsync(task);
        await _store.DeleteAsync(task.Id);

        var reSaveResult = await _store.SaveAsync(task);
        Assert.True(reSaveResult.IsSuccess);

        var getResult = await _store.GetByIdAsync(task.Id);
        Assert.True(getResult.IsSuccess);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static TransferTask CreateTask(TransferStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\test.txt"),
            new RemotePath("bucket/test.txt", RemotePathKind.ObjectPath),
            status,
            new TransferOptions(TransferOverwritePolicy.Ask),
            now,
            now);
    }
}
