using AtomBox.Application.Accounts;
using AtomBox.Application.Browsing;
using AtomBox.Application.Transfers;
using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Tests;

public sealed class ApplicationValidationTests
{
    [Fact]
    public async Task AccountAdd_PreservesProviderConfig()
    {
        var accounts = new CapturingStorageAccountRepository();
        var service = new AccountAppService(accounts, new EmptyTransferTaskStore(), new ThrowingProviderFactory());

        var result = await service.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Aliyun OSS",
            "oss-cn-hangzhou.aliyuncs.com",
            "cn-hangzhou",
            new CredentialRef("cred-1"),
            new Dictionary<string, string> { ["bucket"] = "assets" }));

        Assert.True(result.IsSuccess);
        Assert.Equal("assets", accounts.SavedAccount?.GetProviderConfigValue("bucket"));
        Assert.Equal("assets", result.GetValueOrThrow().ProviderConfig["bucket"]);
    }

    [Fact]
    public async Task AccountUpdate_ReturnsValidationFailure_BeforeRepositoryCall()
    {
        var accounts = new CountingStorageAccountRepository();
        var service = new AccountAppService(accounts, new EmptyTransferTaskStore(), new ThrowingProviderFactory());

        var result = await service.UpdateAsync(new UpdateStorageAccountRequest(
            StorageAccountId.New(),
            " ",
            null,
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
        Assert.Equal(0, accounts.GetByIdCalls);
    }

    [Fact]
    public async Task TestConnection_ProbesProviderRootList()
    {
        var account = CreateAccount();
        var accounts = new SingleStorageAccountRepository(account);
        var providerFactory = new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
        var service = new AccountAppService(accounts, new EmptyTransferTaskStore(), providerFactory);

        var result = await service.TestConnectionAsync(new TestConnectionRequest(account.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, providerFactory.Provider.ListCalls);
        Assert.Equal(RemotePath.Root, providerFactory.Provider.LastPath);
        Assert.Equal(account.ProviderId, result.GetValueOrThrow().ProviderId);
        Assert.Equal(RemotePath.Root.ToString(), result.GetValueOrThrow().TargetSummary);
        Assert.Equal(account.Endpoint, result.GetValueOrThrow().Endpoint);
        Assert.Equal(account.Region, result.GetValueOrThrow().Region);
        Assert.Null(result.GetValueOrThrow().BucketName);
    }

    [Fact]
    public async Task TestConnection_ProbesConfiguredBucket_WhenObjectStorageBucketExists()
    {
        var account = CreateAccount(new Dictionary<string, string> { ["bucket"] = "assets" });
        var accounts = new SingleStorageAccountRepository(account);
        var providerFactory = new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
        var service = new AccountAppService(accounts, new EmptyTransferTaskStore(), providerFactory);

        var result = await service.TestConnectionAsync(new TestConnectionRequest(account.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(new RemotePath("assets", RemotePathKind.BucketRoot), providerFactory.Provider.LastPath);
        Assert.Equal(new RemotePath("assets", RemotePathKind.BucketRoot).ToString(), result.GetValueOrThrow().TargetSummary);
        Assert.Equal("assets", result.GetValueOrThrow().BucketName);
    }

    [Fact]
    public async Task TestConnection_ReturnsProbeFailure()
    {
        var account = CreateAccount();
        var accounts = new SingleStorageAccountRepository(account);
        var providerFactory = new ProbeProviderFactory(
            OperationResult<IReadOnlyList<RemoteItem>>.Failure(new StorageError(
                StorageErrorCode.ProviderUnavailable,
                "probe failed",
                StorageErrorCategory.Provider,
                isRetryable: true)));
        var service = new AccountAppService(accounts, new EmptyTransferTaskStore(), providerFactory);

        var result = await service.TestConnectionAsync(new TestConnectionRequest(account.Id));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Provider, result.Error?.Category);
        Assert.Equal(1, providerFactory.Provider.ListCalls);
    }

    [Fact]
    public async Task TestConnectionDraft_UsesFormSnapshotWithoutAccountRepositoryLookup()
    {
        var accounts = new CountingStorageAccountRepository();
        var providerFactory = new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
        var service = new AccountAppService(accounts, new EmptyTransferTaskStore(), providerFactory);

        var result = await service.TestConnectionDraftAsync(new TestConnectionDraftRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Aliyun OSS",
            "oss-cn-hangzhou.aliyuncs.com",
            null,
            new CredentialRef("cred-1"),
            new Dictionary<string, string> { ["bucket"] = "assets" }));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, accounts.GetByIdCalls);
        Assert.Equal(new RemotePath("assets", RemotePathKind.BucketRoot), providerFactory.Provider.LastPath);
    }

    [Fact]
    public async Task RemoteDelete_FolderUsesAccountLookup_BeforeProviderCreation()
    {
        var factory = new ThrowingProviderFactory();
        var service = new RemoteBrowserAppService(new CountingStorageAccountRepository(), factory);

        var result = await service.DeleteRemoteItemAsync(new DeleteRemoteItemRequest(
            StorageAccountId.New(),
            new RemotePath("folder", RemotePathKind.Folder),
            RemoteItemKind.Folder));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
        Assert.Equal(0, factory.CreateCalls);
    }

    [Fact]
    public async Task ListRemoteItems_ReturnsPageCursors()
    {
        var account = CreateAccount();
        var accounts = new SingleStorageAccountRepository(account);
        var providerFactory = new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success(
        [
            CreateRemoteItem("a.txt"),
            CreateRemoteItem("b.txt"),
            CreateRemoteItem("c.txt")
        ]));
        var service = new RemoteBrowserAppService(accounts, providerFactory);

        var first = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            account.Id,
            RemotePath.Root,
            new RemotePageRequest(2)));
        var second = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            account.Id,
            RemotePath.Root,
            new RemotePageRequest(2, first.GetValueOrThrow().NextCursor)));

        Assert.True(first.IsSuccess);
        Assert.Equal(["a.txt", "b.txt"], first.GetValueOrThrow().Items.Select(item => item.Name).ToArray());
        Assert.True(first.GetValueOrThrow().HasNextPage);
        Assert.False(first.GetValueOrThrow().HasPreviousPage);
        Assert.Equal(first.GetValueOrThrow().HasNextPage, first.GetValueOrThrow().Context.HasNextPage);
        Assert.Equal(first.GetValueOrThrow().HasPreviousPage, first.GetValueOrThrow().Context.HasPreviousPage);
        Assert.False(first.GetValueOrThrow().Context.CanUpload);
        Assert.False(first.GetValueOrThrow().Context.CanDeleteSelectedFile);
        Assert.Equal(account.Id, first.GetValueOrThrow().RetryRequest?.StorageAccountId);
        Assert.Equal(RemotePath.Root, first.GetValueOrThrow().RetryRequest?.Path);
        Assert.True(second.IsSuccess);
        Assert.Equal(["c.txt"], second.GetValueOrThrow().Items.Select(item => item.Name).ToArray());
        Assert.True(second.GetValueOrThrow().HasPreviousPage);
        Assert.False(second.GetValueOrThrow().HasNextPage);
    }

    [Fact]
    public async Task ListRemoteItems_ReturnsPathContextForBucketRoot()
    {
        var account = CreateAccount();
        var accounts = new SingleStorageAccountRepository(account);
        var providerFactory = new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
        var service = new RemoteBrowserAppService(accounts, providerFactory);
        var path = new RemotePath("assets", RemotePathKind.BucketRoot);

        var result = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            account.Id,
            path,
            new RemotePageRequest(50)));

        Assert.True(result.IsSuccess);
        var context = result.GetValueOrThrow().Context;
        Assert.Equal(path, context.CurrentPath);
        Assert.False(context.IsRoot);
        Assert.True(context.IsBucketRoot);
        Assert.True(context.CanUpload);
        Assert.True(context.CanDeleteSelectedFile);
    }

    [Fact]
    public async Task CreateUploadTasks_RejectsEmptyLocalPathList_BeforeSchedulerCall()
    {
        var scheduler = new CountingTransferTaskScheduler();
        var service = new TransferAppService(scheduler, new EmptyTransferStateStore(), new EmptyTransferTaskStore());

        var result = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            Array.Empty<LocalPath>(),
            RemotePath.Root,
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
        Assert.Equal(0, scheduler.SubmitCalls);
        Assert.Equal(0, scheduler.WakeCalls);
    }

    [Fact]
    public async Task CreateDownloadTasks_SubmitsPendingTaskDescription()
    {
        var scheduler = new CountingTransferTaskScheduler();
        var service = new TransferAppService(scheduler, new EmptyTransferStateStore(), new EmptyTransferTaskStore());

        var result = await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            StorageAccountId.New(),
            [new RemotePath("bucket/file.txt")],
            new LocalPath(@"C:\Downloads\file.txt"),
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, scheduler.SubmitCalls);
        Assert.Equal(1, scheduler.WakeCalls);
        var task = Assert.Single(result.GetValueOrThrow().Tasks);
        Assert.Equal(TransferDirection.Download, task.Direction);
        Assert.Equal(TransferStatus.Pending, task.Status);
    }

    [Fact]
    public async Task CreateBatchUploadTasks_SubmitsAllTargetsBeforeSingleWake()
    {
        var scheduler = new CountingTransferTaskScheduler();
        var service = new TransferAppService(scheduler, new EmptyTransferStateStore(), new EmptyTransferTaskStore());

        var result = await service.CreateBatchUploadTasksAsync(new CreateBatchUploadTasksRequest(
            StorageAccountId.New(),
            [
                new UploadTaskTarget(new LocalPath(@"C:\Uploads\a.txt"), new RemotePath("bucket/a.txt")),
                new UploadTaskTarget(new LocalPath(@"C:\Uploads\b.txt"), new RemotePath("bucket/b.txt"))
            ],
            TransferOverwritePolicy.Ask));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, scheduler.SubmitCalls);
        Assert.Equal(1, scheduler.WakeCalls);
        Assert.Equal(
            [new RemotePath("bucket/a.txt"), new RemotePath("bucket/b.txt")],
            scheduler.SubmittedTasks.Select(task => task.RemotePath).ToArray());
    }

    [Fact]
    public async Task CreateDownloadTasks_ExposesCompletedTaskInHistoryAfterWake()
    {
        var taskStore = new MemoryTransferTaskStore();
        var stateStore = new MemoryTransferStateStore(taskStore);
        var scheduler = new CompletingTransferTaskScheduler(taskStore, stateStore);
        var service = new TransferAppService(scheduler, stateStore, taskStore);

        var createResult = await service.CreateDownloadTasksAsync(new CreateDownloadTasksRequest(
            StorageAccountId.New(),
            [new RemotePath("bucket/file.txt")],
            new LocalPath(@"C:\Downloads\file.txt"),
            TransferOverwritePolicy.Ask));
        var historyResult = await service.GetHistoryAsync(new GetTransferHistoryRequest());

        Assert.True(createResult.IsSuccess);
        Assert.True(historyResult.IsSuccess);
        var historyTask = Assert.Single(historyResult.GetValueOrThrow().Tasks);
        Assert.Equal(createResult.GetValueOrThrow().Tasks.Single().Id, historyTask.Task.Id);
        Assert.Equal(TransferStatus.Succeeded, historyTask.Task.Status);
        Assert.Equal(100, historyTask.Progress?.Percent);
    }

    [Fact]
    public async Task CreateUploadTasks_ExposesCompletedTaskInHistoryAfterWake()
    {
        var taskStore = new MemoryTransferTaskStore();
        var stateStore = new MemoryTransferStateStore(taskStore);
        var scheduler = new CompletingTransferTaskScheduler(taskStore, stateStore);
        var service = new TransferAppService(scheduler, stateStore, taskStore);

        var createResult = await service.CreateUploadTasksAsync(new CreateUploadTasksRequest(
            StorageAccountId.New(),
            [new LocalPath(@"C:\Uploads\file.txt")],
            new RemotePath("bucket/file.txt"),
            TransferOverwritePolicy.Ask));
        var historyResult = await service.GetHistoryAsync(new GetTransferHistoryRequest());

        Assert.True(createResult.IsSuccess);
        Assert.True(historyResult.IsSuccess);
        var historyTask = Assert.Single(historyResult.GetValueOrThrow().Tasks);
        Assert.Equal(createResult.GetValueOrThrow().Tasks.Single().Id, historyTask.Task.Id);
        Assert.Equal(TransferDirection.Upload, historyTask.Task.Direction);
        Assert.Equal(TransferStatus.Succeeded, historyTask.Task.Status);
        Assert.Equal(100, historyTask.Progress?.Percent);
    }

    [Fact]
    public async Task CancelTransferTask_RoutesToScheduler_WhenTaskIdIsValid()
    {
        var scheduler = new CountingTransferTaskScheduler();
        var service = new TransferAppService(scheduler, new EmptyTransferStateStore(), new EmptyTransferTaskStore());

        var result = await service.CancelAsync(new CancelTransferTaskRequest(TransferTaskId.New()));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, scheduler.CancelCalls);
    }

    [Fact]
    public async Task RetryTransferTask_RoutesToScheduler_WhenTaskIdIsValid()
    {
        var scheduler = new CountingTransferTaskScheduler();
        var service = new TransferAppService(scheduler, new EmptyTransferStateStore(), new EmptyTransferTaskStore());

        var result = await service.RetryAsync(new RetryTransferTaskRequest(TransferTaskId.New()));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, scheduler.RetryCalls);
        Assert.Equal(1, scheduler.WakeCalls);
    }

    [Fact]
    public async Task ClearHistory_DeletesOnlyHistoricalTasks()
    {
        var taskStore = new MemoryTransferTaskStore();
        var succeeded = CreateTask(TransferStatus.Succeeded);
        var failed = CreateTask(TransferStatus.Failed);
        var pending = CreateTask(TransferStatus.Pending);
        await taskStore.SaveAsync(succeeded);
        await taskStore.SaveAsync(failed);
        await taskStore.SaveAsync(pending);
        var service = new TransferAppService(
            new CountingTransferTaskScheduler(),
            new MemoryTransferStateStore(taskStore),
            taskStore);

        var result = await service.ClearHistoryAsync(new ClearTransferHistoryRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal([pending.Id], taskStore.Tasks.Select(task => task.Id).ToArray());
    }

    private sealed class CountingStorageAccountRepository : IStorageAccountRepository
    {
        public int GetByIdCalls { get; private set; }

        public Task<OperationResult<StorageAccount>> GetByIdAsync(
            StorageAccountId accountId,
            CancellationToken cancellationToken = default)
        {
            GetByIdCalls++;
            return Task.FromResult(OperationResult<StorageAccount>.Failure(StorageError.NotFound("not found")));
        }

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<StorageAccount>>.Success(Array.Empty<StorageAccount>()));
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

    private sealed class CapturingStorageAccountRepository : IStorageAccountRepository
    {
        public StorageAccount? SavedAccount { get; private set; }

        public Task<OperationResult<StorageAccount>> GetByIdAsync(
            StorageAccountId accountId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SavedAccount is null
                ? OperationResult<StorageAccount>.Failure(StorageError.NotFound("not found"))
                : OperationResult<StorageAccount>.Success(SavedAccount));
        }

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(
                OperationResult<IReadOnlyList<StorageAccount>>.Success(SavedAccount is null ? [] : [SavedAccount]));
        }

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
        {
            SavedAccount = account;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
        {
            SavedAccount = account;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
        {
            SavedAccount = null;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class SingleStorageAccountRepository : IStorageAccountRepository
    {
        private readonly StorageAccount _account;

        public SingleStorageAccountRepository(StorageAccount account)
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

    private sealed class EmptyTransferTaskStore : ITransferTaskStore
    {
        public Task<OperationResult<TransferTask>> GetByIdAsync(
            TransferTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<TransferTask>.Failure(StorageError.NotFound("not found")));
        }

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<TransferTask>>.Success(Array.Empty<TransferTask>()));
        }

        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class CountingTransferTaskScheduler : ITransferTaskScheduler
    {
        private readonly List<TransferTask> _submittedTasks = [];

        public IReadOnlyList<TransferTask> SubmittedTasks => _submittedTasks;

        public int SubmitCalls { get; private set; }

        public int CancelCalls { get; private set; }

        public int RetryCalls { get; private set; }

        public int WakeCalls { get; private set; }

        public Task<OperationResult> SubmitAsync(TransferTask task, CancellationToken cancellationToken = default)
        {
            SubmitCalls++;
            _submittedTasks.Add(task);
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

    private sealed class CompletingTransferTaskScheduler : ITransferTaskScheduler
    {
        private readonly ITransferTaskStore _tasks;
        private readonly ITransferStateStore _state;

        public CompletingTransferTaskScheduler(ITransferTaskStore tasks, ITransferStateStore state)
        {
            _tasks = tasks;
            _state = state;
        }

        public Task<OperationResult> SubmitAsync(
            TransferTask task,
            CancellationToken cancellationToken = default)
        {
            return _tasks.SaveAsync(task, cancellationToken);
        }

        public Task<OperationResult> CancelAsync(
            TransferTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> RetryAsync(
            TransferTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public async Task<OperationResult> WakeAsync(CancellationToken cancellationToken = default)
        {
            var tasksResult = await _tasks.ListAsync(cancellationToken).ConfigureAwait(false);
            if (tasksResult.IsFailure)
            {
                return OperationResult.Failure(tasksResult.Error!);
            }

            foreach (var task in tasksResult.GetValueOrThrow().Where(task => task.Status == TransferStatus.Pending))
            {
                var completeResult = await _state.UpdateStatusAsync(
                    task.WithStatus(TransferStatus.Succeeded, DateTimeOffset.UtcNow),
                    new TransferProgress(1, 1, null),
                    cancellationToken).ConfigureAwait(false);
                if (completeResult.IsFailure)
                {
                    return completeResult;
                }
            }

            return OperationResult.Success();
        }
    }

    private sealed class EmptyTransferStateStore : ITransferStateStore
    {
        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(Array.Empty<TransferStateSnapshot>()));
        }

        public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(Array.Empty<TransferStateSnapshot>()));
        }

        public Task<OperationResult> UpdateStatusAsync(
            TransferTask task,
            TransferProgress? progress,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class MemoryTransferTaskStore : ITransferTaskStore
    {
        private readonly List<TransferTask> _tasks = [];

        public IReadOnlyList<TransferTask> Tasks => _tasks;

        public Task<OperationResult<TransferTask>> GetByIdAsync(
            TransferTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            var task = _tasks.FirstOrDefault(item => item.Id == taskId);
            return Task.FromResult(task is null
                ? OperationResult<TransferTask>.Failure(StorageError.NotFound("not found"))
                : OperationResult<TransferTask>.Success(task));
        }

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<TransferTask>>.Success(_tasks.ToArray()));
        }

        public Task<OperationResult> SaveAsync(
            TransferTask task,
            CancellationToken cancellationToken = default)
        {
            var index = _tasks.FindIndex(item => item.Id == task.Id);
            if (index < 0)
            {
                _tasks.Add(task);
            }
            else
            {
                _tasks[index] = task;
            }

            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> DeleteAsync(
            TransferTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            _tasks.RemoveAll(task => task.Id == taskId);
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class MemoryTransferStateStore : ITransferStateStore
    {
        private readonly ITransferTaskStore _tasks;
        private readonly Dictionary<TransferTaskId, TransferProgress?> _progress = [];

        public MemoryTransferStateStore(ITransferTaskStore tasks)
        {
            _tasks = tasks;
        }

        public async Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(
            CancellationToken cancellationToken = default)
        {
            var tasks = await _tasks.ListAsync(cancellationToken).ConfigureAwait(false);
            return tasks.IsFailure
                ? OperationResult<IReadOnlyList<TransferStateSnapshot>>.Failure(tasks.Error!)
                : OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(
                    tasks.GetValueOrThrow()
                        .Where(task => task.Status is TransferStatus.Pending or TransferStatus.Running or TransferStatus.Paused or TransferStatus.Interrupted)
                        .Select(task => new TransferStateSnapshot(task, GetProgress(task.Id)))
                        .ToArray());
        }

        public async Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var tasks = await _tasks.ListAsync(cancellationToken).ConfigureAwait(false);
            return tasks.IsFailure
                ? OperationResult<IReadOnlyList<TransferStateSnapshot>>.Failure(tasks.Error!)
                : OperationResult<IReadOnlyList<TransferStateSnapshot>>.Success(
                    tasks.GetValueOrThrow()
                        .Where(task => task.Status is TransferStatus.Succeeded or TransferStatus.Failed or TransferStatus.Canceled)
                        .Skip(skip)
                        .Take(take)
                        .Select(task => new TransferStateSnapshot(task, GetProgress(task.Id)))
                        .ToArray());
        }

        public Task<OperationResult> UpdateStatusAsync(
            TransferTask task,
            TransferProgress? progress,
            CancellationToken cancellationToken = default)
        {
            _progress[task.Id] = progress;
            return _tasks.SaveAsync(task, cancellationToken);
        }

        private TransferProgress? GetProgress(TransferTaskId taskId)
        {
            return _progress.GetValueOrDefault(taskId);
        }
    }

    private static TransferTask CreateTask(TransferStatus status)
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

    private static RemoteItem CreateRemoteItem(string name)
    {
        return new RemoteItem(name, new RemotePath(name), RemoteItemKind.File, 1, null);
    }

    private static StorageAccount CreateAccount(IReadOnlyDictionary<string, string>? providerConfig = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Aliyun OSS",
            "oss-cn-hangzhou.aliyuncs.com",
            null,
            new CredentialRef("cred-1"),
            now,
            now,
            providerConfig);
    }

    private sealed class ThrowingProviderFactory : IStorageProviderFactory
    {
        public int CreateCalls { get; private set; }

        public Task<OperationResult<IStorageProvider>> CreateAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            throw new InvalidOperationException("Provider factory should not be called.");
        }
    }

    private sealed class ProbeProviderFactory : IStorageProviderFactory
    {
        public ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>> probeResult)
        {
            Provider = new ProbeStorageProvider(probeResult);
        }

        public ProbeStorageProvider Provider { get; }

        public Task<OperationResult<IStorageProvider>> CreateAsync(
            StorageAccount account,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<IStorageProvider>.Success(Provider));
        }
    }

    private sealed class ProbeStorageProvider : IStorageProvider
    {
        private readonly OperationResult<IReadOnlyList<RemoteItem>> _probeResult;

        public ProbeStorageProvider(OperationResult<IReadOnlyList<RemoteItem>> probeResult)
        {
            _probeResult = probeResult;
        }

        public int ListCalls { get; private set; }

        public RemotePath? LastPath { get; private set; }

        public AtomBox.Core.Capabilities.StorageCapabilitySet Capabilities => AtomBox.Core.Capabilities.StorageCapabilitySet.Empty;

        public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(
            RemotePath path,
            CancellationToken cancellationToken = default)
        {
            ListCalls++;
            LastPath = path;
            return Task.FromResult(_probeResult);
        }

        public Task<OperationResult> DeleteAsync(RemotePath path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> UploadAsync(
            RemotePath path,
            Stream content,
            long? contentLength,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> DownloadAsync(
            RemotePath path,
            Stream destination,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
