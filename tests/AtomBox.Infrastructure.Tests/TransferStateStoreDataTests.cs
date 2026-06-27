using System.Text.Json;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class TransferStateStoreDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProgressFile_IsCreated_WhenProgressUpdated()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var task = CreateTask(TransferStatus.Running);
        await taskStore.SaveAsync(task);

        await stateStore.UpdateStatusAsync(task, new TransferProgress(100, 200, null));

        Assert.True(File.Exists(paths.TransferProgressFile));
    }

    [Fact]
    public async Task ProgressFile_ContainsValidProgress()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var task = CreateTask(TransferStatus.Running);
        await taskStore.SaveAsync(task);

        await stateStore.UpdateStatusAsync(task, new TransferProgress(50, 100, 1024));

        var raw = await File.ReadAllTextAsync(paths.TransferProgressFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(JsonValueKind.Array, entries.ValueKind);
        Assert.NotEmpty(entries.EnumerateArray());

        var entry = entries[0];
        Assert.True(entry.TryGetProperty("taskId", out var taskId));
        Assert.Equal(task.Id.Value, taskId.GetGuid());
        Assert.True(entry.TryGetProperty("progress", out var progress));
        Assert.Equal(50, progress.GetProperty("bytesTransferred").GetInt64());
        Assert.Equal(100, progress.GetProperty("totalBytes").GetInt64());
        Assert.Equal(1024, progress.GetProperty("speedBytesPerSecond").GetDouble());
    }

    [Fact]
    public async Task Progress_UpdatedMultipleTimes_ReplacesInPlace()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var task = CreateTask(TransferStatus.Running);
        await taskStore.SaveAsync(task);

        await stateStore.UpdateStatusAsync(task, new TransferProgress(10, 100, null));
        await stateStore.UpdateStatusAsync(task, new TransferProgress(90, 100, null));

        var raw = await File.ReadAllTextAsync(paths.TransferProgressFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Single(entries.EnumerateArray());
        Assert.Equal(90, entries[0].GetProperty("progress").GetProperty("bytesTransferred").GetInt64());
    }

    [Fact]
    public async Task TaskAndProgressFiles_AreSeparate()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var task = CreateTask(TransferStatus.Running);
        await taskStore.SaveAsync(task);

        await stateStore.UpdateStatusAsync(task, new TransferProgress(50, 100, null));

        Assert.True(File.Exists(paths.TransferTasksFile));
        Assert.True(File.Exists(paths.TransferProgressFile));
        Assert.NotEqual(paths.TransferTasksFile, paths.TransferProgressFile);
    }

    [Fact]
    public async Task UpdateStatus_OnUnsavedTask_UpsertsTask()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var task = CreateTask(TransferStatus.Running);
        var progress = new TransferProgress(50, 100, null);

        var result = await stateStore.UpdateStatusAsync(task, progress);
        Assert.True(result.IsSuccess);

        var taskFromStore = (await taskStore.GetByIdAsync(task.Id)).GetValueOrThrow();
        Assert.Equal(TransferStatus.Running, taskFromStore.Status);

        var queue = await stateStore.ListQueueAsync();
        Assert.Contains(queue.GetValueOrThrow(), s => s.Task.Id == task.Id);
        var snapshot = queue.GetValueOrThrow().First(s => s.Task.Id == task.Id);
        Assert.NotNull(snapshot.Progress);
        Assert.Equal(50, snapshot.Progress!.BytesTransferred);
    }

    [Fact]
    public async Task UpdateStatus_WithNullProgress_OnUnsavedTask_StillCreatesTask()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var task = CreateTask(TransferStatus.Pending);

        var result = await stateStore.UpdateStatusAsync(task, progress: null);
        Assert.True(result.IsSuccess);

        var taskFromStore = (await taskStore.GetByIdAsync(task.Id)).GetValueOrThrow();
        Assert.Equal(TransferStatus.Pending, taskFromStore.Status);
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
