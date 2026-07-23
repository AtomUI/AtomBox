using AtomBox.Application.Transfers;
using AtomBox.Core.Errors;
using AtomBox.Core.Fingerprints;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Tests;

public sealed class TransferAppServiceContractTests
{
    private static readonly CancellationToken CT = CancellationToken.None;

    [Fact]
    public async Task CreateUploadAsync_EmptyStorageAccountId_ReturnsValidationFailure()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            default(StorageAccountId),
            [new LocalPath(@"C:\test.txt")],
            RemotePath.Root,
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task CreateUploadAsync_NullLocalPaths_ReturnsValidationFailure()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            null!,
            RemotePath.Root,
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task CreateUploadAsync_EmptyLocalPaths_ReturnsValidationFailure()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            Array.Empty<LocalPath>(),
            RemotePath.Root,
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task CreateUploadAsync_EmptyLocalPathInList_ReturnsValidationFailure()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            new[] { new LocalPath(@"C:\ok.txt"), default(LocalPath) },
            RemotePath.Root,
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task CreateUploadAsync_SingleValidPath_ReturnsSuccessWithOneTask()
    {
        var scheduler = new CountingScheduler();
        var service = new TransferAppService(
            scheduler, new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            [new LocalPath(@"C:\test.txt")],
            RemotePath.Root,
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsSuccess);
        var tasks = result.GetValueOrThrow().Tasks;
        Assert.Single(tasks);
        Assert.Equal(TransferDirection.Upload, tasks[0].Direction);
        Assert.Equal(TransferStatus.Pending, tasks[0].Status);
        Assert.Equal(1, scheduler.SubmitCalls);
        Assert.Equal(1, scheduler.WakeCalls);
    }

    [Fact]
    public async Task CreateUploadAsync_MultiplePaths_ReturnsAllTasks()
    {
        var scheduler = new CountingScheduler();
        var service = new TransferAppService(
            scheduler, new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            [new LocalPath(@"C:\a.txt"), new LocalPath(@"C:\b.txt"), new LocalPath(@"C:\c.txt")],
            new RemotePath("bucket"),
            TransferOverwritePolicy.Overwrite));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.GetValueOrThrow().Tasks.Count);
        Assert.Equal(3, scheduler.SubmitCalls);
    }

    [Fact]
    public async Task CreateUploadAsync_SchedulerSubmitFailure_PropagatesError()
    {
        var scheduler = new FailOnSubmitScheduler();
        var service = new TransferAppService(
            scheduler, new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            [new LocalPath(@"C:\test.txt")],
            RemotePath.Root,
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateUploadAsync_SchedulerWakeFailure_PropagatesError()
    {
        var scheduler = new FailOnWakeScheduler();
        var service = new TransferAppService(
            scheduler, new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            [new LocalPath(@"C:\test.txt")],
            RemotePath.Root,
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task PrepareBatchUploadAsync_SettingsDisabled_ReturnsTargetsWithoutFingerprint()
    {
        var service = new TransferAppService(
            new CountingScheduler(),
            new EmptyStateStore(),
            new EmptyTaskStore(),
            new FixedSettingsRepository(enableFingerprintIndex: false),
            new FixedLocalFileStore([1, 2, 3]),
            new FakeFingerprintIndexStore());

        var result = await service.PrepareBatchUploadTasksAsync(new PrepareBatchUploadTasksRequest(
            StorageAccountId.New(),
            [new UploadTaskTarget(new LocalPath(@"C:\test.txt"), new RemotePath("bucket/test.txt"))]));

        Assert.True(result.IsSuccess);
        var target = Assert.Single(result.GetValueOrThrow().Targets);
        Assert.Null(target.Fingerprint);
        Assert.Empty(target.HistoricalRecords);
    }

    [Fact]
    public async Task PrepareBatchUploadAsync_SettingsEnabled_ComputesFingerprintAndReturnsMatches()
    {
        var accountId = StorageAccountId.New();
        var historical = new FileFingerprintRecord(
            "sha256",
            "ignored",
            5,
            accountId,
            new StorageProviderId("sftp"),
            new RemotePath("backup/test.txt"),
            DateTimeOffset.UtcNow);
        var index = new FakeFingerprintIndexStore([historical]);
        var service = new TransferAppService(
            new CountingScheduler(),
            new EmptyStateStore(),
            new EmptyTaskStore(),
            new FixedSettingsRepository(enableFingerprintIndex: true),
            new FixedLocalFileStore([1, 2, 3, 4, 5]),
            index);

        var result = await service.PrepareBatchUploadTasksAsync(new PrepareBatchUploadTasksRequest(
            accountId,
            [new UploadTaskTarget(new LocalPath(@"C:\test.txt"), new RemotePath("bucket/test.txt"))]));

        Assert.True(result.IsSuccess);
        var target = Assert.Single(result.GetValueOrThrow().Targets);
        Assert.NotNull(target.Fingerprint);
        Assert.Equal("sha256", target.Fingerprint!.HashAlgorithm);
        Assert.Equal(5, target.Fingerprint.FileSize);
        Assert.Single(target.HistoricalRecords);
        Assert.NotNull(index.LastQuery);
        Assert.Equal(accountId, index.LastQuery!.StorageAccountId);
    }

    [Fact]
    public async Task CreateBatchUploadAsync_WithFingerprint_PersistsMetadataOnTask()
    {
        var scheduler = new CountingScheduler();
        var service = new TransferAppService(
            scheduler,
            new EmptyStateStore(),
            new EmptyTaskStore());
        var fingerprint = new UploadTaskFingerprint("sha256", "abcdef", 123, DateTimeOffset.UtcNow);

        var result = await service.CreateBatchUploadTasksAsync(new CreateBatchUploadTasksRequest(
            StorageAccountId.New(),
            [new UploadTaskTarget(new LocalPath(@"C:\test.txt"), new RemotePath("bucket/test.txt"), fingerprint)],
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsSuccess);
        var task = Assert.Single(result.GetValueOrThrow().Tasks);
        Assert.True(task.HasCompleteFingerprintMetadata);
        Assert.Equal("sha256", task.FingerprintHashAlgorithm);
        Assert.Equal("abcdef", task.FingerprintHashValue);
        Assert.Equal(123, task.FingerprintFileSize);
        Assert.Single(scheduler.SubmittedTasks);
        Assert.Equal("abcdef", scheduler.SubmittedTasks[0].FingerprintHashValue);
    }

    [Fact]
    public async Task CreateDownloadAsync_EmptyStorageAccountId_ReturnsValidationFailure()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            default(StorageAccountId),
            [new RemotePath("bucket/file.txt")],
            new LocalPath(@"C:\Downloads"),
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task CreateDownloadAsync_EmptyLocalPath_ReturnsValidationFailure()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            StorageAccountId.New(),
            [new RemotePath("bucket/file.txt")],
            default(LocalPath),
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task CreateDownloadAsync_NullRemotePaths_ReturnsValidationFailure()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            StorageAccountId.New(),
            null!,
            new LocalPath(@"C:\Downloads"),
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task CreateDownloadAsync_EmptyRemotePaths_ReturnsValidationFailure()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            StorageAccountId.New(),
            Array.Empty<RemotePath>(),
            new LocalPath(@"C:\Downloads"),
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task CreateDownloadAsync_RootRemotePath_ReturnsValidationFailure()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            StorageAccountId.New(),
            [RemotePath.Root],
            new LocalPath(@"C:\Downloads"),
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task CreateDownloadAsync_SingleValidPath_ReturnsSuccess()
    {
        var scheduler = new CountingScheduler();
        var service = new TransferAppService(
            scheduler, new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            StorageAccountId.New(),
            [new RemotePath("bucket/file.txt")],
            new LocalPath(@"C:\Downloads"),
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsSuccess);
        Assert.Single(result.GetValueOrThrow().Tasks);
        Assert.Equal(TransferDirection.Download, result.GetValueOrThrow().Tasks[0].Direction);
        Assert.Equal(1, scheduler.SubmitCalls);
        Assert.Equal(1, scheduler.WakeCalls);
    }

    [Fact]
    public async Task CreateDownloadAsync_MultipleTargets_PreservesEachPathMapping()
    {
        var scheduler = new CountingScheduler();
        var service = new TransferAppService(
            scheduler, new EmptyStateStore(), new EmptyTaskStore());
        var firstRemotePath = new RemotePath("bucket/first.txt");
        var secondRemotePath = new RemotePath("bucket/second.txt");
        var firstLocalPath = new LocalPath(@"C:\Downloads\first.txt");
        var secondLocalPath = new LocalPath(@"C:\Downloads\second.txt");

        var result = await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            StorageAccountId.New(),
            [
                new DownloadTaskTarget(firstRemotePath, firstLocalPath),
                new DownloadTaskTarget(secondRemotePath, secondLocalPath)
            ],
            TransferOverwritePolicy.Rename));

        Assert.True(result.IsSuccess);
        var tasks = result.GetValueOrThrow().Tasks;
        Assert.Equal(2, tasks.Count);
        Assert.Collection(
            tasks,
            task =>
            {
                Assert.Equal(firstRemotePath, task.RemotePath);
                Assert.Equal(firstLocalPath, task.LocalPath);
            },
            task =>
            {
                Assert.Equal(secondRemotePath, task.RemotePath);
                Assert.Equal(secondLocalPath, task.LocalPath);
            });
        Assert.Equal(2, scheduler.SubmitCalls);
        Assert.Equal(1, scheduler.WakeCalls);
    }

    [Fact]
    public async Task GetQueueAsync_EmptyQueue_ReturnsEmptyList()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.GetQueueAsync(new GetTransferQueueRequest());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.GetValueOrThrow().Tasks);
    }

    [Fact]
    public async Task GetQueueAsync_WithItems_ReturnsSnapshots()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TransferTask(
            TransferTaskId.New(), StorageAccountId.New(), TransferDirection.Upload,
            new LocalPath(@"C:\test.txt"), new RemotePath("bucket/test.txt"),
            TransferStatus.Running, new TransferOptions(TransferOverwritePolicy.Ask),
            now, now);
        var snapshot = new TransferStateSnapshot(task, new TransferProgress(50, 100, null));
        var stateStore = new StateStoreWithQueue([snapshot]);
        var service = new TransferAppService(
            new CountingScheduler(), stateStore, new EmptyTaskStore());

        var result = await service.GetQueueAsync(new GetTransferQueueRequest());

        Assert.True(result.IsSuccess);
        Assert.Single(result.GetValueOrThrow().Tasks);
        Assert.Equal(task.Id, result.GetValueOrThrow().Tasks[0].Task.Id);
        Assert.Equal(50, result.GetValueOrThrow().Tasks[0].Progress?.BytesTransferred);
    }

    [Fact]
    public async Task GetQueueAsync_StateStoreFailure_PropagatesError()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new FailStateStore(), new EmptyTaskStore());

        var result = await service.GetQueueAsync(new GetTransferQueueRequest());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetHistoryAsync_DefaultPageIndex_ReturnsCorrectIndex()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.GetHistoryAsync(new GetTransferHistoryRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.GetValueOrThrow().PageIndex);
    }

    [Fact]
    public async Task GetHistoryAsync_PageIndexBelowOne_ClampsToOne()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.GetHistoryAsync(new GetTransferHistoryRequest(PageIndex: 0));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.GetValueOrThrow().PageIndex);
    }

    [Fact]
    public async Task GetHistoryAsync_WithItems_ReturnsSnapshots()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TransferTask(
            TransferTaskId.New(), StorageAccountId.New(), TransferDirection.Download,
            new LocalPath(@"C:\dl"), new RemotePath("bucket/file.txt"),
            TransferStatus.Succeeded, new TransferOptions(TransferOverwritePolicy.Ask),
            now, now);
        var snapshot = new TransferStateSnapshot(task, new TransferProgress(100, 100, 1024));
        var stateStore = new StateStoreWithHistory([snapshot]);
        var service = new TransferAppService(
            new CountingScheduler(), stateStore, new EmptyTaskStore());

        var result = await service.GetHistoryAsync(new GetTransferHistoryRequest());

        Assert.True(result.IsSuccess);
        Assert.Single(result.GetValueOrThrow().Tasks);
        Assert.Equal(100, result.GetValueOrThrow().Tasks[0].Progress?.Percent);
    }

    [Fact]
    public async Task GetHistoryAsync_StateStoreFailure_PropagatesError()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new FailStateStore(), new EmptyTaskStore());

        var result = await service.GetHistoryAsync(new GetTransferHistoryRequest());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CancelAsync_EmptyTaskId_ReturnsValidationFailure()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CancelAsync(new CancelTransferTaskRequest(default(TransferTaskId)));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task CancelAsync_ValidTaskId_RoutesToScheduler()
    {
        var scheduler = new CountingScheduler();
        var service = new TransferAppService(
            scheduler, new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CancelAsync(new CancelTransferTaskRequest(TransferTaskId.New()));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, scheduler.CancelCalls);
    }

    [Fact]
    public async Task CancelAsync_SchedulerFailure_PropagatesError()
    {
        var scheduler = new FailOnCancelScheduler();
        var service = new TransferAppService(
            scheduler, new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.CancelAsync(new CancelTransferTaskRequest(TransferTaskId.New()));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task RetryAsync_EmptyTaskId_ReturnsValidationFailure()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.RetryAsync(new RetryTransferTaskRequest(default(TransferTaskId)));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task RetryAsync_ValidTaskId_RoutesToScheduler()
    {
        var scheduler = new CountingScheduler();
        var service = new TransferAppService(
            scheduler, new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.RetryAsync(new RetryTransferTaskRequest(TransferTaskId.New()));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, scheduler.RetryCalls);
        Assert.Equal(1, scheduler.WakeCalls);
    }

    [Fact]
    public async Task RetryAsync_SchedulerRetryFailure_PropagatesError()
    {
        var scheduler = new FailOnRetryScheduler();
        var service = new TransferAppService(
            scheduler, new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.RetryAsync(new RetryTransferTaskRequest(TransferTaskId.New()));

        Assert.True(result.IsFailure);
        Assert.Equal(0, scheduler.WakeCalls);
    }

    [Fact]
    public async Task RetryAsync_SchedulerWakeFailure_PropagatesError()
    {
        var scheduler = new FailOnWakeScheduler();
        var service = new TransferAppService(
            scheduler, new EmptyStateStore(), new EmptyTaskStore());

        var result = await service.RetryAsync(new RetryTransferTaskRequest(TransferTaskId.New()));

        Assert.True(result.IsFailure);
        Assert.Equal(1, scheduler.RetryCalls);
    }

    [Fact]
    public async Task ClearHistoryAsync_EmptyHistory_ReturnsSuccess()
    {
        var taskStore = new CountingTaskStore();
        var service = new TransferAppService(
            new CountingScheduler(), new EmptyStateStore(), taskStore);

        var result = await service.ClearHistoryAsync(new ClearTransferHistoryRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, taskStore.DeleteCalls);
    }

    [Fact]
    public async Task ClearHistoryAsync_WithItems_DeletesAll()
    {
        var now = DateTimeOffset.UtcNow;
        var task1 = new TransferTask(
            TransferTaskId.New(), StorageAccountId.New(), TransferDirection.Upload,
            new LocalPath(@"C:\a.txt"), new RemotePath("bucket/a.txt"),
            TransferStatus.Succeeded, new TransferOptions(TransferOverwritePolicy.Ask),
            now, now);
        var task2 = new TransferTask(
            TransferTaskId.New(), StorageAccountId.New(), TransferDirection.Upload,
            new LocalPath(@"C:\b.txt"), new RemotePath("bucket/b.txt"),
            TransferStatus.Failed, new TransferOptions(TransferOverwritePolicy.Ask),
            now, now);
        var historySnapshots = new[]
        {
            new TransferStateSnapshot(task1, null),
            new TransferStateSnapshot(task2, null),
        };
        var stateStore = new StateStoreWithHistory(historySnapshots);
        var taskStore = new CountingTaskStore();
        var service = new TransferAppService(
            new CountingScheduler(), stateStore, taskStore);

        var result = await service.ClearHistoryAsync(new ClearTransferHistoryRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, taskStore.DeleteCalls);
    }

    [Fact]
    public async Task ClearHistoryAsync_StateStoreFailure_PropagatesError()
    {
        var service = new TransferAppService(
            new CountingScheduler(), new FailStateStore(), new EmptyTaskStore());

        var result = await service.ClearHistoryAsync(new ClearTransferHistoryRequest());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ClearHistoryAsync_DeleteFailure_StopsAndPropagates()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TransferTask(
            TransferTaskId.New(), StorageAccountId.New(), TransferDirection.Upload,
            new LocalPath(@"C:\a.txt"), new RemotePath("bucket/a.txt"),
            TransferStatus.Succeeded, new TransferOptions(TransferOverwritePolicy.Ask),
            now, now);
        var stateStore = new StateStoreWithHistory([new TransferStateSnapshot(task, null)]);
        var taskStore = new FailOnDeleteTaskStore();
        var service = new TransferAppService(
            new CountingScheduler(), stateStore, taskStore);

        var result = await service.ClearHistoryAsync(new ClearTransferHistoryRequest());

        Assert.True(result.IsFailure);
    }

    private sealed class CountingScheduler : ITransferTaskScheduler
    {
        public int SubmitCalls { get; set; }
        public int CancelCalls { get; set; }
        public int RetryCalls { get; set; }
        public int WakeCalls { get; set; }
        public List<TransferTask> SubmittedTasks { get; } = [];

        public Task<OperationResult> SubmitAsync(TransferTask task, CancellationToken cancellationToken = default)
        {
            SubmitCalls++;
            SubmittedTasks.Add(task);
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

    private sealed class FixedSettingsRepository : IApplicationSettingsRepository
    {
        private readonly bool _enableFingerprintIndex;

        public FixedSettingsRepository(bool enableFingerprintIndex)
        {
            _enableFingerprintIndex = enableFingerprintIndex;
        }

        public Task<OperationResult<ApplicationSettings>> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<ApplicationSettings>.Success(
                new ApplicationSettings(3, TransferOverwritePolicy.Ask, true, _enableFingerprintIndex)));
        }

        public Task<OperationResult> SaveAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FixedLocalFileStore : ILocalTransferFileStore
    {
        private readonly byte[] _content;

        public FixedLocalFileStore(byte[] content)
        {
            _content = content;
        }

        public Task<OperationResult<LocalTransferReadHandle>> OpenReadAsync(
            LocalPath path,
            CancellationToken cancellationToken = default)
        {
            var stream = new MemoryStream(_content, writable: false);
            return Task.FromResult(OperationResult<LocalTransferReadHandle>.Success(
                new LocalTransferReadHandle(stream, _content.Length)));
        }

        public Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
            LocalPath path,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<LocalTransferWriteHandle>.Failure(
                StorageError.NotSupported("write not supported")));
        }
    }

    private sealed class FakeFingerprintIndexStore : IFileFingerprintIndexStore
    {
        private readonly IReadOnlyList<FileFingerprintRecord> _records;

        public FakeFingerprintIndexStore()
            : this([])
        {
        }

        public FakeFingerprintIndexStore(IReadOnlyList<FileFingerprintRecord> records)
        {
            _records = records;
        }

        public FileFingerprintQuery? LastQuery { get; private set; }

        public Task<OperationResult<IReadOnlyList<FileFingerprintRecord>>> FindAsync(
            FileFingerprintQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult<OperationResult<IReadOnlyList<FileFingerprintRecord>>>(
                OperationResult<IReadOnlyList<FileFingerprintRecord>>.Success(_records));
        }

        public Task<OperationResult> AddOrUpdateAsync(
            FileFingerprintRecord record,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult<FileFingerprintIndexStatistics>> GetStatisticsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<FileFingerprintIndexStatistics>.Success(
                new FileFingerprintIndexStatistics("index.json", _records.Count, null)));
        }

        public Task<OperationResult> ClearAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FailOnSubmitScheduler : ITransferTaskScheduler
    {
        public int WakeCalls { get; private set; }

        public Task<OperationResult> SubmitAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure(
                new StorageError(StorageErrorCode.Unknown, "submit failed", StorageErrorCategory.Unknown)));

        public Task<OperationResult> CancelAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> RetryAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> WakeAsync(CancellationToken cancellationToken = default)
        {
            WakeCalls++;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FailOnWakeScheduler : ITransferTaskScheduler
    {
        public int SubmitCalls { get; private set; }
        public int RetryCalls { get; private set; }

        public Task<OperationResult> SubmitAsync(TransferTask task, CancellationToken cancellationToken = default)
        {
            SubmitCalls++;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> CancelAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> RetryAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
        {
            RetryCalls++;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> WakeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure(
                new StorageError(StorageErrorCode.Unknown, "wake failed", StorageErrorCategory.Unknown)));
    }

    private sealed class FailOnCancelScheduler : ITransferTaskScheduler
    {
        public Task<OperationResult> SubmitAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> CancelAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure(
                new StorageError(StorageErrorCode.Unknown, "cancel failed", StorageErrorCategory.Unknown)));

        public Task<OperationResult> RetryAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> WakeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class FailOnRetryScheduler : ITransferTaskScheduler
    {
        public int WakeCalls { get; private set; }

        public Task<OperationResult> SubmitAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> CancelAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> RetryAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure(
                new StorageError(StorageErrorCode.Unknown, "retry failed", StorageErrorCategory.Unknown)));

        public Task<OperationResult> WakeAsync(CancellationToken cancellationToken = default)
        {
            WakeCalls++;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class EmptyStateStore : ITransferStateStore
    {
        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferStateSnapshot>>>(
                OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success([]));

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
            int skip, int take, CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferStateSnapshot>>>(
                OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success([]));

        public Task<OperationResult> UpdateStatusAsync(TransferTask task, TransferProgress? progress, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class StateStoreWithQueue : ITransferStateStore
    {
        private readonly IReadOnlyList<TransferStateSnapshot> _snapshots;

        public StateStoreWithQueue(IReadOnlyList<TransferStateSnapshot> snapshots) { _snapshots = snapshots; }

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferStateSnapshot>>>(
                OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(_snapshots));

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
            int skip, int take, CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferStateSnapshot>>>(
                OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success([]));

        public Task<OperationResult> UpdateStatusAsync(TransferTask task, TransferProgress? progress, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class StateStoreWithHistory : ITransferStateStore
    {
        private readonly IReadOnlyList<TransferStateSnapshot> _snapshots;

        public StateStoreWithHistory(IReadOnlyList<TransferStateSnapshot> snapshots) { _snapshots = snapshots; }

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferStateSnapshot>>>(
                OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success([]));

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
            int skip, int take, CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferStateSnapshot>>>(
                OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(_snapshots));

        public Task<OperationResult> UpdateStatusAsync(TransferTask task, TransferProgress? progress, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class FailStateStore : ITransferStateStore
    {
        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferStateSnapshot>>>(
                OperationResult<IReadOnlyList<TransferStateSnapshot>>.Failure(
                    new StorageError(StorageErrorCode.InfrastructureUnavailable, "fail", StorageErrorCategory.Infrastructure)));

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
            int skip, int take, CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferStateSnapshot>>>(
                OperationResult<IReadOnlyList<TransferStateSnapshot>>.Failure(
                    new StorageError(StorageErrorCode.InfrastructureUnavailable, "fail", StorageErrorCategory.Infrastructure)));

        public Task<OperationResult> UpdateStatusAsync(TransferTask task, TransferProgress? progress, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure(
                new StorageError(StorageErrorCode.InfrastructureUnavailable, "fail", StorageErrorCategory.Infrastructure)));
    }

    private sealed class EmptyTaskStore : ITransferTaskStore
    {
        public Task<OperationResult<TransferTask>> GetByIdAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<TransferTask>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferTask>>>(
                OperationResult<IReadOnlyList<TransferTask>>.Success([]));

        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class CountingTaskStore : ITransferTaskStore
    {
        public int DeleteCalls { get; private set; }

        public Task<OperationResult<TransferTask>> GetByIdAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<TransferTask>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferTask>>>(
                OperationResult<IReadOnlyList<TransferTask>>.Success([]));

        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FailOnDeleteTaskStore : ITransferTaskStore
    {
        public Task<OperationResult<TransferTask>> GetByIdAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<TransferTask>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferTask>>>(
                OperationResult<IReadOnlyList<TransferTask>>.Success([]));

        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure(
                new StorageError(StorageErrorCode.InfrastructureUnavailable, "delete failed", StorageErrorCategory.Infrastructure)));
    }
}
