using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class TransferStateStoreContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));
    private readonly TransferTaskStore _taskStore;
    private readonly TransferStateStore _stateStore;

    public TransferStateStoreContractTests()
    {
        var paths = new AtomBoxStoragePaths(_root);
        _taskStore = new TransferTaskStore(paths);
        _stateStore = new TransferStateStore(_taskStore, paths);
    }

    [Fact]
    public async Task ListQueue_WhenEmpty_ReturnsEmpty()
    {
        var result = await _stateStore.ListQueueAsync();
        Assert.True(result.IsSuccess);
        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task ListHistory_WhenEmpty_ReturnsEmpty()
    {
        var result = await _stateStore.ListHistoryAsync(0, 50);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task UpdateStatus_PendingInQueue_SucceededInHistory()
    {
        var task = CreateTask(TransferStatus.Pending);
        await _taskStore.SaveAsync(task);

        var running = task.WithStatus(TransferStatus.Running, DateTimeOffset.UtcNow);
        var progress = new TransferProgress(50, 100, null);
        var updateResult = await _stateStore.UpdateStatusAsync(running, progress);
        Assert.True(updateResult.IsSuccess);

        var completed = running.WithStatus(TransferStatus.Succeeded, DateTimeOffset.UtcNow);
        var finalProgress = new TransferProgress(100, 100, null);
        await _stateStore.UpdateStatusAsync(completed, finalProgress);

        var queue = await _stateStore.ListQueueAsync();
        Assert.Empty(queue.GetValueOrThrow());

        var history = await _stateStore.ListHistoryAsync(0, 50);
        var snapshot = Assert.Single(history.GetValueOrThrow());
        Assert.Equal(TransferStatus.Succeeded, snapshot.Task.Status);
        Assert.Equal(100, snapshot.Progress?.BytesTransferred);
    }

    [Fact]
    public async Task ListQueue_FiltersByActiveStatuses()
    {
        var pending = CreateTask(TransferStatus.Pending);
        var running = CreateTask(TransferStatus.Running);
        var succeeded = CreateTask(TransferStatus.Succeeded);
        var failed = CreateTask(TransferStatus.Failed);
        var paused = CreateTask(TransferStatus.Paused);
        var canceled = CreateTask(TransferStatus.Canceled);

        await _taskStore.SaveAsync(pending);
        await _taskStore.SaveAsync(running);
        await _taskStore.SaveAsync(succeeded);
        await _taskStore.SaveAsync(failed);
        await _taskStore.SaveAsync(paused);
        await _taskStore.SaveAsync(canceled);

        var queue = await _stateStore.ListQueueAsync();
        Assert.True(queue.IsSuccess);
        var queueIds = queue.GetValueOrThrow().Select(s => s.Task.Id).ToHashSet();
        Assert.Contains(pending.Id, queueIds);
        Assert.Contains(running.Id, queueIds);
        Assert.Contains(paused.Id, queueIds);
        Assert.DoesNotContain(succeeded.Id, queueIds);
        Assert.DoesNotContain(failed.Id, queueIds);
        Assert.DoesNotContain(canceled.Id, queueIds);
    }

    [Fact]
    public async Task ListHistory_RespectsPaging()
    {
        for (var i = 0; i < 10; i++)
        {
            var task = CreateTask(TransferStatus.Succeeded);
            await _taskStore.SaveAsync(task);
        }

        var page1 = await _stateStore.ListHistoryAsync(0, 3);
        Assert.True(page1.IsSuccess);
        Assert.Equal(3, page1.GetValueOrThrow().Count);

        var page2 = await _stateStore.ListHistoryAsync(3, 3);
        Assert.True(page2.IsSuccess);
        Assert.Equal(3, page2.GetValueOrThrow().Count);

        var allIds = page1.GetValueOrThrow().Union(page2.GetValueOrThrow()).Select(s => s.Task.Id).ToHashSet();
        Assert.Equal(6, allIds.Count);
    }

    [Fact]
    public async Task UpdateStatus_NullProgress_DoesNotWriteProgressRecord()
    {
        var task = CreateTask(TransferStatus.Pending);
        await _taskStore.SaveAsync(task);

        var result = await _stateStore.UpdateStatusAsync(task, progress: null);
        Assert.True(result.IsSuccess);

        var queue = await _stateStore.ListQueueAsync();
        var snapshot = Assert.Single(queue.GetValueOrThrow());
        Assert.Null(snapshot.Progress);
    }

    [Fact]
    public async Task UpdateStatus_ReplacesPreviousProgress()
    {
        var task = CreateTask(TransferStatus.Running);
        await _taskStore.SaveAsync(task);

        await _stateStore.UpdateStatusAsync(task, new TransferProgress(30, 100, null));
        await _stateStore.UpdateStatusAsync(task, new TransferProgress(70, 100, null));

        var queue = await _stateStore.ListQueueAsync();
        var snapshot = Assert.Single(queue.GetValueOrThrow());
        Assert.Equal(70, snapshot.Progress?.BytesTransferred);
    }

    [Fact]
    public async Task ListHistory_OrdersByUpdatedAtDescending()
    {
        var now = DateTimeOffset.UtcNow;
        var t1 = CreateTask(TransferStatus.Succeeded, createdAt: now.AddHours(-4), updatedAt: now.AddHours(-2));
        var t2 = CreateTask(TransferStatus.Succeeded, createdAt: now.AddHours(-3), updatedAt: now.AddHours(-1));
        await _taskStore.SaveAsync(t1);
        await _taskStore.SaveAsync(t2);

        var history = await _stateStore.ListHistoryAsync(0, 10);
        Assert.True(history.IsSuccess);
        var snapshots = history.GetValueOrThrow();
        Assert.Equal(2, snapshots.Count);
        Assert.True(snapshots[0].Task.UpdatedAt >= snapshots[1].Task.UpdatedAt);
    }

    [Fact]
    public async Task ListHistory_IncludesInterruptedAsHistorical()
    {
        var task = CreateTask(TransferStatus.Interrupted);
        await _taskStore.SaveAsync(task);

        var history = await _stateStore.ListHistoryAsync(0, 50);
        Assert.Contains(history.GetValueOrThrow(), s => s.Task.Id == task.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static TransferTask CreateTask(TransferStatus status, DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        var created = createdAt ?? now;
        var updated = updatedAt ?? now;
        return new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\test.txt"),
            new RemotePath("bucket/test.txt", RemotePathKind.ObjectPath),
            status,
            new TransferOptions(TransferOverwritePolicy.Ask),
            created,
            updated);
    }
}
