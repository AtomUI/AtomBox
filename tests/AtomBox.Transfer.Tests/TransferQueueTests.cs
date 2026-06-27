using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Transfer.Queue;

namespace AtomBox.Transfer.Tests;

public sealed class TransferQueueTests
{
    [Fact]
    public void SelectPending_ReturnsOnlyPendingTasks()
    {
        var queue = new TransferQueue();
        var tasks = new[]
        {
            TransferTaskFactory.Create(TransferStatus.Pending),
            TransferTaskFactory.Create(TransferStatus.Running),
            TransferTaskFactory.Create(TransferStatus.Succeeded),
            TransferTaskFactory.Create(TransferStatus.Failed),
            TransferTaskFactory.Create(TransferStatus.Canceled),
            TransferTaskFactory.Create(TransferStatus.Interrupted),
            TransferTaskFactory.Create(TransferStatus.Paused),
        };

        var result = queue.SelectPending(tasks);

        var ids = result.Select(t => t.Id).ToHashSet();
        Assert.Single(result);
        Assert.Contains(tasks[0].Id, ids);
    }

    [Fact]
    public void SelectPending_OrdersByCreatedAt_Ascending()
    {
        var queue = new TransferQueue();
        var early = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);
        var mid = TransferTaskFactory.CreateWithDirection(TransferDirection.Download, TransferStatus.Pending);
        var late = TransferTaskFactory.CreateWithDirection(TransferDirection.Upload, TransferStatus.Pending);

        // Manually adjust creation times (since they're all created at Now)
        // We need to create with different timestamps. Use reflection or just
        // create tasks with explicit times.
        var now = DateTimeOffset.UtcNow;
        var t1 = CreateTaskAt(now.AddMinutes(-5));
        var t2 = CreateTaskAt(now.AddMinutes(-2));
        var t3 = CreateTaskAt(now);

        var result = queue.SelectPending([t3, t1, t2]);

        Assert.Equal(3, result.Count);
        Assert.Equal(t1.Id, result[0].Id);
        Assert.Equal(t2.Id, result[1].Id);
        Assert.Equal(t3.Id, result[2].Id);
    }

    [Fact]
    public void SelectPending_EmptyList_ReturnsEmptyArray()
    {
        var queue = new TransferQueue();
        var result = queue.SelectPending([]);
        Assert.Empty(result);
    }

    [Fact]
    public void SelectPending_NullInput_Throws()
    {
        var queue = new TransferQueue();
        Assert.Throws<ArgumentNullException>(() => queue.SelectPending(null!));
    }

    [Fact]
    public void SelectPending_MixedStatuses_OnlyPendingReturned()
    {
        var queue = new TransferQueue();
        var tasks = new[]
        {
            TransferTaskFactory.Create(TransferStatus.Pending),
            TransferTaskFactory.Create(TransferStatus.Running),
            TransferTaskFactory.Create(TransferStatus.Succeeded),
            TransferTaskFactory.Create(TransferStatus.Pending),
            TransferTaskFactory.Create(TransferStatus.Failed),
        };

        var result = queue.SelectPending(tasks);

        Assert.Equal(2, result.Count);
        Assert.All(result, t => Assert.Equal(TransferStatus.Pending, t.Status));
    }

    private static TransferTask CreateTaskAt(DateTimeOffset createdAt)
    {
        return new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\file.txt"),
            new RemotePath("bucket/file.txt"),
            TransferStatus.Pending,
            new TransferOptions(TransferOverwritePolicy.Ask),
            createdAt,
            createdAt);
    }
}
