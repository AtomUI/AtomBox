using System.Text.Json;
using AtomBox.Application.Transfers;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Application.Tests;

public sealed class TransferAppServiceDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.App.TransferData", Guid.NewGuid().ToString("N"));
    private static readonly CancellationToken CT = CancellationToken.None;

    [Fact]
    public async Task CreateUploadTasksAsync_CreatesTasksFile()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var scheduler = new StoreBackedScheduler(taskStore, stateStore);
        var service = new TransferAppService(scheduler, stateStore, taskStore);

        var result = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            [new LocalPath(@"C:\test.txt")],
            RemotePath.Root,
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(paths.TransferTasksFile));
        var raw = await File.ReadAllTextAsync(paths.TransferTasksFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
        Assert.True(parsed.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task CreateDownloadTasksAsync_CreatesTasksFile()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var scheduler = new StoreBackedScheduler(taskStore, stateStore);
        var service = new TransferAppService(scheduler, stateStore, taskStore);

        var result = await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            StorageAccountId.New(),
            [new RemotePath("bucket/file.txt")],
            new LocalPath(@"C:\Downloads"),
            TransferOverwritePolicy.Overwrite));

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(paths.TransferTasksFile));
        var tasks = result.GetValueOrThrow().Tasks;
        Assert.Single(tasks);
        Assert.Equal(TransferDirection.Download, tasks[0].Direction);
    }

    [Fact]
    public async Task CreateThenGetQueueAsync_ReturnsQueuedTask()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var scheduler = new StoreBackedScheduler(taskStore, stateStore);
        var service = new TransferAppService(scheduler, stateStore, taskStore);

        var createResult = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            [new LocalPath(@"C:\queued.txt")],
            RemotePath.Root,
            TransferOverwritePolicy.Ask));

        var queueResult = await service.GetQueueAsync(new GetTransferQueueRequest());

        Assert.True(queueResult.IsSuccess);
        var queue = queueResult.GetValueOrThrow().Tasks;
        Assert.NotEmpty(queue);
        Assert.Equal(createResult.GetValueOrThrow().Tasks[0].Id, queue[0].Task.Id);
        Assert.Equal(TransferStatus.Pending, queue[0].Task.Status);
    }

    [Fact]
    public async Task GetQueueAsync_WhenNoTasks_ReturnsEmpty()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var scheduler = new CountingScheduler();
        var service = new TransferAppService(scheduler, stateStore, taskStore);

        var result = await service.GetQueueAsync(new GetTransferQueueRequest());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.GetValueOrThrow().Tasks);
    }

    [Fact]
    public async Task CreateThenGetHistoryAsync_ReturnsCompletedTask()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var scheduler = new CompletingScheduler(taskStore, stateStore);
        var service = new TransferAppService(scheduler, stateStore, taskStore);

        await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            StorageAccountId.New(),
            [new RemotePath("bucket/file.txt")],
            new LocalPath(@"C:\Downloads"),
            TransferOverwritePolicy.Ask));

        var historyResult = await service.GetHistoryAsync(new GetTransferHistoryRequest());

        Assert.True(historyResult.IsSuccess);
        var history = historyResult.GetValueOrThrow().Tasks;
        Assert.NotEmpty(history);
        Assert.Equal(TransferStatus.Succeeded, history[0].Task.Status);
    }

    [Fact]
    public async Task GetHistoryAsync_PageIndex2_ReturnsSecondPage()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var scheduler = new CompletingScheduler(taskStore, stateStore);
        var service = new TransferAppService(scheduler, stateStore, taskStore);

        for (var i = 0; i < 5; i++)
        {
            await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
                StorageAccountId.New(),
                [new RemotePath($"bucket/file{i}.txt")],
                new LocalPath(@"C:\Downloads"),
                TransferOverwritePolicy.Ask));
        }

        var page1 = await service.GetHistoryAsync(new GetTransferHistoryRequest(PageIndex: 1));
        Assert.True(page1.IsSuccess);
        var r1 = page1.GetValueOrThrow();
        Assert.Equal(5, r1.Tasks.Count);
        Assert.Equal(1, r1.PageIndex);

        var page2 = await service.GetHistoryAsync(new GetTransferHistoryRequest(PageIndex: 2));
        Assert.True(page2.IsSuccess);
        var r2 = page2.GetValueOrThrow();
        Assert.Empty(r2.Tasks);
        Assert.Equal(2, r2.PageIndex);
    }

    [Fact]
    public async Task CancelAsync_RoutesToScheduler()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var scheduler = new CountingScheduler();
        var service = new TransferAppService(scheduler, stateStore, taskStore);

        var result = await service.CancelAsync(new CancelTransferTaskRequest(TransferTaskId.New()));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, scheduler.CancelCalls);
    }

    [Fact]
    public async Task RetryAsync_RoutesToScheduler()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var scheduler = new CountingScheduler();
        var service = new TransferAppService(scheduler, stateStore, taskStore);

        var result = await service.RetryAsync(new RetryTransferTaskRequest(TransferTaskId.New()));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, scheduler.RetryCalls);
        Assert.Equal(1, scheduler.WakeCalls);
    }

    [Fact]
    public async Task ClearHistoryAsync_RemovesFromFiles()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var now = DateTimeOffset.UtcNow;
        var succeededTask = new TransferTask(
            TransferTaskId.New(), StorageAccountId.New(), TransferDirection.Upload,
            new LocalPath(@"C:\old.txt"), new RemotePath("bucket/old.txt"),
            TransferStatus.Succeeded, new TransferOptions(TransferOverwritePolicy.Ask),
            now, now);
        await taskStore.SaveAsync(succeededTask);
        await stateStore.UpdateStatusAsync(succeededTask, new TransferProgress(100, 100, null));

        var service = new TransferAppService(
            new CountingScheduler(), stateStore, taskStore);

        var clearResult = await service.ClearHistoryAsync(new ClearTransferHistoryRequest());
        Assert.True(clearResult.IsSuccess);

        var historyResult = await service.GetHistoryAsync(new GetTransferHistoryRequest());
        Assert.Empty(historyResult.GetValueOrThrow().Tasks);

        var raw = await File.ReadAllTextAsync(paths.TransferTasksFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
        Assert.Equal(0, parsed.GetArrayLength());
    }

    [Fact]
    public async Task CreateUploadTasksAsync_MultiplePaths_CreatesAllTasks()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var taskStore = new TransferTaskStore(paths);
        var stateStore = new TransferStateStore(taskStore, paths);
        var scheduler = new StoreBackedScheduler(taskStore, stateStore);
        var service = new TransferAppService(scheduler, stateStore, taskStore);

        var result = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            [
                new LocalPath(@"C:\a.txt"),
                new LocalPath(@"C:\b.txt"),
                new LocalPath(@"C:\c.txt"),
            ],
            new RemotePath("bucket"),
            TransferOverwritePolicy.Overwrite));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.GetValueOrThrow().Tasks.Count);

        var allTasks = await taskStore.ListAsync();
        Assert.True(allTasks.IsSuccess);
        Assert.Equal(3, allTasks.GetValueOrThrow().Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class StoreBackedScheduler : ITransferTaskScheduler
    {
        private readonly ITransferTaskStore _taskStore;
        private readonly ITransferStateStore _stateStore;

        public StoreBackedScheduler(ITransferTaskStore taskStore, ITransferStateStore stateStore)
        {
            _taskStore = taskStore;
            _stateStore = stateStore;
        }

        public async Task<OperationResult> SubmitAsync(TransferTask task, CancellationToken cancellationToken = default)
        {
            return await _taskStore.SaveAsync(task, cancellationToken);
        }

        public Task<OperationResult> CancelAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> RetryAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> WakeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class CompletingScheduler : ITransferTaskScheduler
    {
        private readonly ITransferTaskStore _taskStore;
        private readonly ITransferStateStore _stateStore;

        public CompletingScheduler(ITransferTaskStore taskStore, ITransferStateStore stateStore)
        {
            _taskStore = taskStore;
            _stateStore = stateStore;
        }

        public async Task<OperationResult> SubmitAsync(TransferTask task, CancellationToken cancellationToken = default)
        {
            var saveResult = await _taskStore.SaveAsync(task, cancellationToken);
            if (saveResult.IsFailure) return saveResult;

            var completed = task.WithStatus(TransferStatus.Succeeded, DateTimeOffset.UtcNow);
            return await _stateStore.UpdateStatusAsync(completed, new TransferProgress(1, 1, null), cancellationToken);
        }

        public Task<OperationResult> CancelAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> RetryAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> WakeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class CountingScheduler : ITransferTaskScheduler
    {
        public int SubmitCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public int RetryCalls { get; private set; }
        public int WakeCalls { get; private set; }

        public Task<OperationResult> SubmitAsync(TransferTask task, CancellationToken cancellationToken = default)
        {
            SubmitCalls++;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> CancelAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
        {
            CancelCalls++;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> RetryAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
        {
            RetryCalls++;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> WakeAsync(CancellationToken cancellationToken = default)
        {
            WakeCalls++;
            return Task.FromResult(OperationResult.Success());
        }
    }
}
