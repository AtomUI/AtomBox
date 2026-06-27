using System.Text;
using System.Text.Json;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class TransferTaskStoreDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));
    private static readonly CancellationToken CT = CancellationToken.None;

    [Fact]
    public async Task TransferTasksFile_IsValidJsonArray()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new TransferTaskStore(paths);

        await store.SaveAsync(CreateTask(TransferStatus.Pending), CT);

        var raw = await File.ReadAllTextAsync(paths.TransferTasksFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
        Assert.True(parsed.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task TransferTasksFile_ContainsRequiredFields()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new TransferTaskStore(paths);
        var task = CreateTask(TransferStatus.Pending);

        await store.SaveAsync(task, CT);

        var raw = await File.ReadAllTextAsync(paths.TransferTasksFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);
        var entry = entries[0];

        Assert.True(entry.TryGetProperty("id", out _));
        Assert.True(entry.TryGetProperty("storageAccountId", out _));
        Assert.True(entry.TryGetProperty("direction", out _));
        Assert.True(entry.TryGetProperty("localPath", out _));
        Assert.True(entry.TryGetProperty("remotePath", out _));
        Assert.True(entry.TryGetProperty("status", out _));
        Assert.True(entry.TryGetProperty("options", out _));
        Assert.True(entry.TryGetProperty("createdAt", out _));
        Assert.True(entry.TryGetProperty("updatedAt", out _));
    }

    [Fact]
    public async Task MultipleTasks_AreStoredInOneArray()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new TransferTaskStore(paths);

        for (var i = 0; i < 5; i++)
        {
            await store.SaveAsync(CreateTask((TransferStatus)(i % 4)), CT);
        }

        var raw = await File.ReadAllTextAsync(paths.TransferTasksFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(5, entries.GetArrayLength());
    }

    [Fact]
    public async Task DeleteTask_RemovesFromArray()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new TransferTaskStore(paths);
        var task = CreateTask(TransferStatus.Pending);
        await store.SaveAsync(task, CT);

        await store.DeleteAsync(task.Id, CT);

        var raw = await File.ReadAllTextAsync(paths.TransferTasksFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Empty(entries.EnumerateArray());
    }

    [Fact]
    public async Task SaveUpdates_ReplaceInPlace()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new TransferTaskStore(paths);
        var task = CreateTask(TransferStatus.Pending);
        await store.SaveAsync(task, CT);

        var updated = task.WithStatus(TransferStatus.Running, DateTimeOffset.UtcNow);
        await store.SaveAsync(updated, CT);

        var raw = await File.ReadAllTextAsync(paths.TransferTasksFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Single(entries.EnumerateArray());
        Assert.Equal(1, entries[0].GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task StatusReason_IsPersistedWhenSet()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new TransferTaskStore(paths);
        var now = DateTimeOffset.UtcNow;
        var task = CreateTask(TransferStatus.Failed, "Connection timeout");
        await store.SaveAsync(task, CT);

        var raw = await File.ReadAllTextAsync(paths.TransferTasksFile);
        Assert.Contains("Connection timeout", raw);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static TransferTask CreateTask(TransferStatus status, string? statusReason = null)
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
            now,
            statusReason);
    }
}
