using AtomBox.Application.Browsing;
using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Application.Tests;

public sealed class RemoteBrowserAppServiceDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.App.RemoteData", Guid.NewGuid().ToString("N"));
    private static readonly CancellationToken CT = CancellationToken.None;

    [Fact]
    public async Task ResolveEntryAsync_WithNoAccounts_ReturnsEmpty()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var service = new RemoteBrowserAppService(
            new StorageAccountRepository(paths),
            new ThrowingProviderFactory());

        var result = await service.ResolveEntryAsync(
            new ResolveRemoteEntryRequest(StorageProviderCategory.ObjectStorage));

        Assert.True(result.IsSuccess);
        Assert.Equal(RemoteEntryState.Empty, result.GetValueOrThrow().State);
    }

    [Fact]
    public async Task ResolveEntryAsync_WithOneAccount_ReturnsSingleAccount()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var accountId = StorageAccountId.New();
        var now = DateTimeOffset.UtcNow;
        await repo.AddAsync(new StorageAccount(
            accountId, StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"), "My OSS",
            null, null, new CredentialRef("cred-1"), now, now));

        var service = new RemoteBrowserAppService(repo, new ThrowingProviderFactory());

        var result = await service.ResolveEntryAsync(
            new ResolveRemoteEntryRequest(StorageProviderCategory.ObjectStorage));

        Assert.True(result.IsSuccess);
        Assert.Equal(RemoteEntryState.SingleAccount, result.GetValueOrThrow().State);
        Assert.Equal([accountId], result.GetValueOrThrow().AccountIds);
    }

    [Fact]
    public async Task ResolveEntryAsync_WithMultipleAccounts_ReturnsAccountSelection()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var now = DateTimeOffset.UtcNow;

        await repo.AddAsync(new StorageAccount(
            StorageAccountId.New(), StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"), "OSS 1",
            null, null, new CredentialRef("cred-1"), now, now));
        await repo.AddAsync(new StorageAccount(
            StorageAccountId.New(), StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"), "OSS 2",
            null, null, new CredentialRef("cred-2"), now, now));

        var service = new RemoteBrowserAppService(repo, new ThrowingProviderFactory());

        var result = await service.ResolveEntryAsync(
            new ResolveRemoteEntryRequest(StorageProviderCategory.ObjectStorage));

        Assert.True(result.IsSuccess);
        Assert.Equal(RemoteEntryState.AccountSelection, result.GetValueOrThrow().State);
        Assert.Equal(2, result.GetValueOrThrow().AccountIds.Count);
    }

    [Fact]
    public async Task ListRemoteItemsAsync_WithPersistedAccount_ListsViaProvider()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var accountId = StorageAccountId.New();
        var now = DateTimeOffset.UtcNow;
        await repo.AddAsync(new StorageAccount(
            accountId, StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"), "My OSS",
            "oss-cn-hangzhou.aliyuncs.com", null,
            new CredentialRef("cred-1"), now, now));

        var items = new List<RemoteItem>
        {
            new("file1.txt", RemotePath.Root.Combine("file1.txt"), RemoteItemKind.File, 100, null),
            new("file2.txt", RemotePath.Root.Combine("file2.txt"), RemoteItemKind.File, 200, null),
        };
        var providerFactory = new ProbeProviderFactory(
            OperationResult<IReadOnlyList<RemoteItem>>.Success(items));
        var service = new RemoteBrowserAppService(repo, providerFactory);

        var result = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            accountId, RemotePath.Root, RemotePageRequest.FirstPage));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.GetValueOrThrow().Items.Count);
        Assert.Equal("file1.txt", result.GetValueOrThrow().Items[0].Name);
    }

    [Fact]
    public async Task ListRemoteItemsAsync_SubdirectoryPath_ReturnsFolderItems()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var accountId = StorageAccountId.New();
        var now = DateTimeOffset.UtcNow;
        await repo.AddAsync(new StorageAccount(
            accountId, StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"), "My OSS",
            "oss-cn-hangzhou.aliyuncs.com", null,
            new CredentialRef("cred-1"), now, now));

        var folderPath = new RemotePath("my-bucket/subdir", RemotePathKind.Folder);
        var items = new List<RemoteItem>
        {
            new("nested.txt", folderPath.Combine("nested.txt"), RemoteItemKind.File, 500, null),
            new("deeply", folderPath.Combine("deeply"), RemoteItemKind.Folder, null, null),
        };
        var providerFactory = new ProbeProviderFactory(
            OperationResult<IReadOnlyList<RemoteItem>>.Success(items));
        var service = new RemoteBrowserAppService(repo, providerFactory);

        var result = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            accountId, folderPath, RemotePageRequest.FirstPage));

        Assert.True(result.IsSuccess);
        var listResult = result.GetValueOrThrow();
        Assert.Equal(2, listResult.Items.Count);
        Assert.Equal("nested.txt", listResult.Items[0].Name);
        Assert.Equal(RemoteItemKind.Folder, listResult.Items[1].Kind);

        var ctx = listResult.Context;
        Assert.Equal(folderPath, ctx.CurrentPath);
        Assert.False(ctx.IsRoot);
        Assert.False(ctx.IsBucketRoot);
        Assert.True(ctx.CanUpload);
    }

    [Fact]
    public async Task ListRemoteItemsAsync_Pagination_ReturnsCorrectCursors()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var accountId = StorageAccountId.New();
        var now = DateTimeOffset.UtcNow;
        await repo.AddAsync(new StorageAccount(
            accountId, StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"), "My OSS",
            null, null, new CredentialRef("cred-1"), now, now));

        var items = new List<RemoteItem>();
        for (var i = 0; i < 10; i++)
        {
            items.Add(new RemoteItem(
                $"file{i}.txt",
                RemotePath.Root.Combine($"file{i}.txt"),
                RemoteItemKind.File, i * 100, null));
        }

        var providerFactory = new ProbeProviderFactory(
            OperationResult<IReadOnlyList<RemoteItem>>.Success(items));
        var service = new RemoteBrowserAppService(repo, providerFactory);

        var page1 = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            accountId, RemotePath.Root, new RemotePageRequest(3)));

        Assert.True(page1.IsSuccess);
        var r1 = page1.GetValueOrThrow();
        Assert.Equal(3, r1.Items.Count);
        Assert.Equal("file0.txt", r1.Items[0].Name);
        Assert.Equal("file1.txt", r1.Items[1].Name);
        Assert.Equal("file2.txt", r1.Items[2].Name);
        Assert.True(r1.HasNextPage);
        Assert.False(r1.HasPreviousPage);
        Assert.NotNull(r1.NextCursor);
        Assert.Null(r1.PreviousCursor);

        var page2 = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            accountId, RemotePath.Root, new RemotePageRequest(3, r1.NextCursor)));

        Assert.True(page2.IsSuccess);
        var r2 = page2.GetValueOrThrow();
        Assert.Equal(3, r2.Items.Count);
        Assert.Equal("file3.txt", r2.Items[0].Name);
        Assert.True(r2.HasNextPage);
        Assert.True(r2.HasPreviousPage);

        var page4 = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            accountId, RemotePath.Root, new RemotePageRequest(3, RemotePageCursor.FromOffset(9))));

        Assert.True(page4.IsSuccess);
        var r4 = page4.GetValueOrThrow();
        Assert.Single(r4.Items);
        Assert.Equal("file9.txt", r4.Items[0].Name);
        Assert.False(r4.HasNextPage);
        Assert.True(r4.HasPreviousPage);
    }

    [Fact]
    public async Task DeleteRemoteItemAsync_WithPersistedAccount_DeletesViaProvider()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var accountId = StorageAccountId.New();
        var now = DateTimeOffset.UtcNow;
        await repo.AddAsync(new StorageAccount(
            accountId, StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"), "My OSS",
            null, null, new CredentialRef("cred-1"), now, now));

        var providerFactory = new ProbeProviderFactory(
            OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
        var service = new RemoteBrowserAppService(repo, providerFactory);

        var result = await service.DeleteRemoteItemAsync(new DeleteRemoteItemRequest(
            accountId, new RemotePath("bucket/file.txt"), RemoteItemKind.File));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetPathContext_WithRealPaths_ReturnsCorrectContext()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var service = new RemoteBrowserAppService(repo, new ThrowingProviderFactory());

        var rootContext = service.GetPathContext(new GetRemotePathContextRequest(RemotePath.Root));
        Assert.True(rootContext.IsSuccess);
        Assert.True(rootContext.GetValueOrThrow().IsRoot);

        var bucketPath = new RemotePath("my-bucket", RemotePathKind.BucketRoot);
        var bucketContext = service.GetPathContext(new GetRemotePathContextRequest(bucketPath));
        Assert.True(bucketContext.IsSuccess);
        Assert.True(bucketContext.GetValueOrThrow().IsBucketRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class ThrowingProviderFactory : IStorageProviderFactory
    {
        public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Should not be called.");
    }

    private sealed class ProbeProviderFactory : IStorageProviderFactory
    {
        public ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>> probeResult)
            { Provider = new ProbeStorageProvider(probeResult); }
        public ProbeStorageProvider Provider { get; }

        public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IStorageProvider>.Success(Provider));
    }

    private sealed class ProbeStorageProvider : IStorageProvider
    {
        private readonly OperationResult<IReadOnlyList<RemoteItem>> _probeResult;
        public ProbeStorageProvider(OperationResult<IReadOnlyList<RemoteItem>> probeResult) { _probeResult = probeResult; }
        public StorageCapabilitySet Capabilities => StorageCapabilitySet.Empty;

        public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(RemotePath path, CancellationToken cancellationToken = default)
            => Task.FromResult(_probeResult);
        public Task<OperationResult> DeleteAsync(RemotePath path, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> UploadAsync(RemotePath path, Stream content, long? contentLength,
            IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> DownloadAsync(RemotePath path, Stream destination,
            IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
