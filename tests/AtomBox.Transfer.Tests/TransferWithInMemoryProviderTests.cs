using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Transfer.Workers;
using System.Text;

namespace AtomBox.Transfer.Tests;

public sealed class TransferWithInMemoryProviderTests
{
    [Fact]
    public async Task UploadDownloadRoundtrip()
    {
        var store = new MemoryTransferStore();
        var buckets = new Dictionary<string, Dictionary<string, byte[]>>
        {
            ["test-bucket"] = new(StringComparer.Ordinal)
        };

        var payload = Encoding.UTF8.GetBytes("Hello AtomBox!");
        var task = CreateUploadTask("test-bucket/hello.txt");
        var localFile = new MemoryLocalTransferFileStoreWithContent(payload);
        var uploadWorker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(new InMemoryObjectStorageProvider(buckets)),
            localFile,
            store);

        var uploadResult = await uploadWorker.ExecuteAsync(task);

        Assert.True(uploadResult.IsSuccess);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Succeeded, saved.Status);

        var downloadStore = new MemoryTransferStore();
        var downloadTask = CreateDownloadTask("test-bucket/hello.txt");
        using var downloadStream = new MemoryStream();
        var localWrite = new CapturingLocalTransferFileStore(downloadStream);

        var downloadWorker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(new InMemoryObjectStorageProvider(buckets)),
            localWrite,
            downloadStore);

        var downloadResult = await downloadWorker.ExecuteAsync(downloadTask);

        Assert.True(downloadResult.IsSuccess);
        Assert.Equal(payload, downloadStream.ToArray());
    }

    [Fact]
    public async Task DeleteAfterUpload()
    {
        var store = new MemoryTransferStore();
        var buckets = new Dictionary<string, Dictionary<string, byte[]>>
        {
            ["test-bucket"] = new(StringComparer.Ordinal)
        };
        var payload = Encoding.UTF8.GetBytes("delete me");
        var localFile = new MemoryLocalTransferFileStoreWithContent(payload);
        var uploadWorker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(new InMemoryObjectStorageProvider(buckets)),
            localFile,
            store);

        var task = CreateUploadTask("test-bucket/to-delete.txt");
        var uploadResult = await uploadWorker.ExecuteAsync(task);

        Assert.True(uploadResult.IsSuccess);
        Assert.True(buckets["test-bucket"].ContainsKey("to-delete.txt"));

        var deleteProvider = new InMemoryObjectStorageProvider(buckets);
        var deleteResult = await deleteProvider.DeleteAsync(
            new RemotePath("test-bucket/to-delete.txt", RemotePathKind.ObjectPath));

        Assert.True(deleteResult.IsSuccess);
        Assert.False(buckets["test-bucket"].ContainsKey("to-delete.txt"));
    }

    [Fact]
    public async Task ListAfterUpload_ShowsObject()
    {
        var store = new MemoryTransferStore();
        var buckets = new Dictionary<string, Dictionary<string, byte[]>>
        {
            ["test-bucket"] = new(StringComparer.Ordinal)
        };
        var payload = Encoding.UTF8.GetBytes("list me");
        var localFile = new MemoryLocalTransferFileStoreWithContent(payload);
        var uploadWorker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(new InMemoryObjectStorageProvider(buckets)),
            localFile,
            store);

        var task = CreateUploadTask("test-bucket/visible.txt");
        var uploadResult = await uploadWorker.ExecuteAsync(task);

        Assert.True(uploadResult.IsSuccess);

        var listProvider = new InMemoryObjectStorageProvider(buckets);
        var listResult = await listProvider.ListAsync(
            new RemotePath("test-bucket", RemotePathKind.BucketRoot));

        Assert.True(listResult.IsSuccess);
        var items = listResult.GetValueOrThrow();
        Assert.Contains(items, item => item.Name == "visible.txt");
    }

    [Fact]
    public async Task ProviderAuthFailure()
    {
        var store = new MemoryTransferStore();
        var error = new StorageError(StorageErrorCode.AuthenticationFailed, "bad auth", StorageErrorCategory.Authentication);
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FailingProviderFactory(error),
            new MemoryLocalTransferFileStore(),
            store);

        var task = CreateUploadTask("bucket/key.txt");
        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Authentication, result.Error?.Category);
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Failed, saved.Status);
    }

    [Fact]
    public async Task ProviderNotFound()
    {
        var store = new MemoryTransferStore();
        var buckets = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal);
        var provider = new InMemoryObjectStorageProvider(buckets);
        var payload = Encoding.UTF8.GetBytes("content");
        var localFile = new MemoryLocalTransferFileStoreWithContent(payload);
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(provider),
            localFile,
            store);

        var task = CreateUploadTask("nonexistent-bucket/key.txt");
        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsFailure);
        Assert.True(
            result.Error?.Category == StorageErrorCategory.NotFound,
            $"Expected NotFound but got {result.Error?.Category}");
        var saved = store.Tasks.Single(t => t.Id == task.Id);
        Assert.Equal(TransferStatus.Failed, saved.Status);
    }

    [Fact]
    public async Task ProgressReportedCorrectly()
    {
        var store = new MemoryTransferStore();
        var buckets = new Dictionary<string, Dictionary<string, byte[]>>
        {
            ["test-bucket"] = new(StringComparer.Ordinal)
        };
        var provider = new InMemoryObjectStorageProvider(buckets);
        var payload = Encoding.UTF8.GetBytes("AtomBox progress check payload!");
        var localFile = new MemoryLocalTransferFileStoreWithContent(payload);
        var worker = new TransferWorker(
            new AnyAccountRepository(),
            new FixedProviderFactory(provider),
            localFile,
            store);

        var task = CreateUploadTask("test-bucket/progress.txt");
        var result = await worker.ExecuteAsync(task);

        Assert.True(result.IsSuccess);
        var update = store.ProgressUpdates.LastOrDefault(u => u.TaskId == task.Id);
        Assert.NotNull(update.Progress);
        Assert.Equal(payload.Length, update.Progress.BytesTransferred);
    }

    private static TransferTask CreateUploadTask(string remotePath)
    {
        return new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\test.txt"),
            new RemotePath(remotePath, RemotePathKind.ObjectPath),
            TransferStatus.Pending,
            new TransferOptions(TransferOverwritePolicy.Overwrite),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private static TransferTask CreateDownloadTask(string remotePath)
    {
        return new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Download,
            new LocalPath(@"C:\test.txt"),
            new RemotePath(remotePath, RemotePathKind.ObjectPath),
            TransferStatus.Pending,
            new TransferOptions(TransferOverwritePolicy.Overwrite),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private sealed class MemoryLocalTransferFileStoreWithContent : ILocalTransferFileStore
    {
        private readonly byte[] _content;

        public MemoryLocalTransferFileStoreWithContent(byte[] content)
        {
            _content = content;
        }

        public Task<OperationResult<LocalTransferReadHandle>> OpenReadAsync(
            LocalPath path, CancellationToken cancellationToken = default)
        {
            var stream = new MemoryStream(_content);
            return Task.FromResult(OperationResult<LocalTransferReadHandle>.Success(
                new LocalTransferReadHandle(stream, _content.Length)));
        }

        public Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
            LocalPath path, CancellationToken cancellationToken = default)
        {
            var stream = new MemoryStream();
            return Task.FromResult(OperationResult<LocalTransferWriteHandle>.Success(
                new LocalTransferWriteHandle(stream)));
        }
    }

    private sealed class CapturingLocalTransferFileStore : ILocalTransferFileStore
    {
        private readonly MemoryStream _capture;

        public CapturingLocalTransferFileStore(MemoryStream capture)
        {
            _capture = capture;
        }

        public Task<OperationResult<LocalTransferReadHandle>> OpenReadAsync(
            LocalPath path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<LocalTransferReadHandle>.Failure(
                StorageError.NotFound("Not supported for download test")));
        }

        public Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
            LocalPath path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<LocalTransferWriteHandle>.Success(
                new LocalTransferWriteHandle(_capture)));
        }
    }
}
