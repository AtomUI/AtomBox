using AtomBox.Core.Transfers;

namespace AtomBox.Transfer.Tests;

public sealed class TransferStateSnapshotTests
{
    [Fact]
    public async Task ListQueue_ReturnsPendingAndRunningTasks()
    {
        var store = new MemoryTransferStore();
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Pending));
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Running));
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Paused));

        var result = await store.ListQueueAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.GetValueOrThrow().Count);
    }

    [Fact]
    public async Task ListQueue_ExcludesTerminalStates()
    {
        var store = new MemoryTransferStore();
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Succeeded));
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Failed));
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Canceled));

        var result = await store.ListQueueAsync();

        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task ListQueue_Empty_ReturnsEmptyList()
    {
        var store = new MemoryTransferStore();
        var result = await store.ListQueueAsync();
        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task ListQueue_InterruptedAppearsInQueue()
    {
        var store = new MemoryTransferStore();
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Interrupted));

        var result = await store.ListQueueAsync();

        Assert.Single(result.GetValueOrThrow());
    }

    [Fact]
    public async Task ListQueue_SnapshotCanRetry_MatchesTask()
    {
        var store = new MemoryTransferStore();
        var failedRetryable = TransferTaskFactory.Create(
            TransferStatus.Failed, "err", AtomBox.Core.Errors.StorageErrorCategory.Network, isRetryable: true);
        var failedNonRetryable = TransferTaskFactory.Create(
            TransferStatus.Failed, "err", AtomBox.Core.Errors.StorageErrorCategory.Validation, isRetryable: false);
        await store.SaveAsync(failedRetryable);
        await store.SaveAsync(failedNonRetryable);

        // Failed is terminal, so won't be in queue. Add a pending that is also failed+retryable
        // Actually Let's test with interrupted instead
        await store.SaveAsync(TransferTaskFactory.Create(
            TransferStatus.Interrupted, "crash", AtomBox.Core.Errors.StorageErrorCategory.Unknown, isRetryable: true));

        // Paused (CanCancel but not terminal)
        var paused = TransferTaskFactory.Create(TransferStatus.Paused);
        await store.SaveAsync(paused);

        var result = await store.ListQueueAsync();
        var snapshots = result.GetValueOrThrow();

        // Paused task should have CanRetry=false
        var pausedSnapshot = snapshots.Single(s => s.Task.Id == paused.Id);
        Assert.False(pausedSnapshot.CanRetry);

        // Interrupted task should have CanRetry=true
        var interruptedSnapshot = snapshots.Single(s => s.Task.Status == TransferStatus.Interrupted);
        Assert.True(interruptedSnapshot.CanRetry);
    }

    [Fact]
    public async Task ListHistory_IncludesAllTerminalStates()
    {
        var store = new MemoryTransferStore();
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Succeeded));
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Failed));
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Canceled));

        var result = await store.ListHistoryAsync(0, 100);

        Assert.Equal(3, result.GetValueOrThrow().Count);
    }

    [Fact]
    public async Task ListHistory_Empty_ReturnsEmptyList()
    {
        var store = new MemoryTransferStore();
        var result = await store.ListHistoryAsync(0, 100);
        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task ListHistory_Pagination_SkipZeroTakeDefault()
    {
        var store = new MemoryTransferStore();
        for (var i = 0; i < 5; i++)
        {
            await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Succeeded));
        }

        var result = await store.ListHistoryAsync(0, 3);

        Assert.Equal(3, result.GetValueOrThrow().Count);
    }

    [Fact]
    public async Task ListHistory_Pagination_SkipPage()
    {
        var store = new MemoryTransferStore();
        for (var i = 0; i < 10; i++)
        {
            await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Succeeded));
        }

        var page1 = await store.ListHistoryAsync(0, 5);
        var page2 = await store.ListHistoryAsync(5, 5);

        Assert.Equal(5, page1.GetValueOrThrow().Count);
        Assert.Equal(5, page2.GetValueOrThrow().Count);
        Assert.NotEqual(
            page1.GetValueOrThrow()[0].Task.Id,
            page2.GetValueOrThrow()[0].Task.Id);
    }

    [Fact]
    public async Task ListHistory_Pagination_ExceedsTotal()
    {
        var store = new MemoryTransferStore();
        for (var i = 0; i < 5; i++)
        {
            await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Succeeded));
        }

        var result = await store.ListHistoryAsync(10, 5);

        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task ListHistory_ExcludesNonTerminalStates()
    {
        var store = new MemoryTransferStore();
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Pending));
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Running));
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Paused));

        var result = await store.ListHistoryAsync(0, 100);

        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task ListHistory_InterruptedAppearsInHistory()
    {
        var store = new MemoryTransferStore();
        await store.SaveAsync(TransferTaskFactory.Create(TransferStatus.Interrupted));

        var result = await store.ListHistoryAsync(0, 100);

        Assert.Single(result.GetValueOrThrow());
    }

    [Fact]
    public async Task UpdateStatusAsync_PersistsProgressUpdate()
    {
        var store = new MemoryTransferStore();
        var task = TransferTaskFactory.Create(TransferStatus.Pending);
        await store.SaveAsync(task);

        var running = task.WithStatus(TransferStatus.Running, DateTimeOffset.UtcNow);
        var progress = new TransferProgress(50, 100, 1024);
        await store.UpdateStatusAsync(running, progress);

        Assert.Contains(store.ProgressUpdates,
            u => u.TaskId == task.Id && u.Progress?.BytesTransferred == 50);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Running, saved.Status);
    }

    [Fact]
    public async Task Snapshots_IncludeTasksWithoutProgress()
    {
        var store = new MemoryTransferStore();
        var task = TransferTaskFactory.Create(TransferStatus.Pending);
        await store.SaveAsync(task);

        var queue = await store.ListQueueAsync();

        var snapshot = Assert.Single(queue.GetValueOrThrow());
        Assert.Equal(task.Id, snapshot.Task.Id);
        Assert.Null(snapshot.Progress);
    }
}
