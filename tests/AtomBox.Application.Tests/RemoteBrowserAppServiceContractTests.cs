using AtomBox.Application.Browsing;
using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Tests;

public sealed class RemoteBrowserAppServiceContractTests
{
    private static readonly CancellationToken CT = CancellationToken.None;

    [Fact]
    public async Task ResolveEntryAsync_NoAccounts_ReturnsEmpty()
    {
        var service = new RemoteBrowserAppService(
            new EmptyAccountRepository(),
            new ThrowingProviderFactory());

        var result = await service.ResolveEntryAsync(
            new ResolveRemoteEntryRequest(StorageProviderCategory.ObjectStorage));

        Assert.True(result.IsSuccess);
        Assert.Equal(RemoteEntryState.Empty, result.GetValueOrThrow().State);
        Assert.Empty(result.GetValueOrThrow().AccountIds);
    }

    [Fact]
    public async Task ResolveEntryAsync_OneAccount_ReturnsSingleAccount()
    {
        var accountId = StorageAccountId.New();
        var service = new RemoteBrowserAppService(
            new SingleAccountRepository(accountId, StorageProviderCategory.ObjectStorage),
            new ThrowingProviderFactory());

        var result = await service.ResolveEntryAsync(
            new ResolveRemoteEntryRequest(StorageProviderCategory.ObjectStorage));

        Assert.True(result.IsSuccess);
        Assert.Equal(RemoteEntryState.SingleAccount, result.GetValueOrThrow().State);
        Assert.Equal([accountId], result.GetValueOrThrow().AccountIds);
    }

    [Fact]
    public async Task ResolveEntryAsync_MultipleAccounts_ReturnsAccountSelection()
    {
        var service = new RemoteBrowserAppService(
            new MultipleAccountsRepository(),
            new ThrowingProviderFactory());

        var result = await service.ResolveEntryAsync(
            new ResolveRemoteEntryRequest(StorageProviderCategory.ObjectStorage));

        Assert.True(result.IsSuccess);
        Assert.Equal(RemoteEntryState.AccountSelection, result.GetValueOrThrow().State);
        Assert.Equal(2, result.GetValueOrThrow().AccountIds.Count);
    }

    [Fact]
    public async Task ResolveEntryAsync_RepositoryFailure_PropagatesError()
    {
        var service = new RemoteBrowserAppService(
            new FailAccountRepository(),
            new ThrowingProviderFactory());

        var result = await service.ResolveEntryAsync(
            new ResolveRemoteEntryRequest(StorageProviderCategory.ObjectStorage));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ListRemoteItemsAsync_EmptyStorageAccountId_ReturnsValidationFailure()
    {
        var service = new RemoteBrowserAppService(
            new EmptyAccountRepository(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            default(StorageAccountId),
            RemotePath.Root,
            RemotePageRequest.FirstPage));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task ListRemoteItemsAsync_NonExistentAccount_ReturnsNotFound()
    {
        var service = new RemoteBrowserAppService(
            new EmptyAccountRepository(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            StorageAccountId.New(),
            RemotePath.Root,
            RemotePageRequest.FirstPage));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task ListRemoteItemsAsync_ProviderCreationFailure_PropagatesError()
    {
        var accountId = StorageAccountId.New();
        var service = new RemoteBrowserAppService(
            new SingleAccountRepository(accountId, StorageProviderCategory.ObjectStorage),
            new FailProviderFactory());

        var result = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            accountId,
            RemotePath.Root,
            RemotePageRequest.FirstPage));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Provider, result.Error?.Category);
    }

    [Fact]
    public async Task ListRemoteItemsAsync_ListSuccess_ReturnsItems()
    {
        var accountId = StorageAccountId.New();
        var items = new List<RemoteItem>
        {
            new("a.txt", RemotePath.Root.Combine("a.txt"), RemoteItemKind.File, 100, null),
            new("b.txt", RemotePath.Root.Combine("b.txt"), RemoteItemKind.File, 200, null),
        };
        var providerFactory = new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success(items));
        var service = new RemoteBrowserAppService(
            new SingleAccountRepository(accountId, StorageProviderCategory.ObjectStorage),
            providerFactory);

        var result = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            accountId,
            RemotePath.Root,
            new RemotePageRequest(50)));

        Assert.True(result.IsSuccess);
        var listResult = result.GetValueOrThrow();
        Assert.Equal(2, listResult.Items.Count);
        Assert.Equal("a.txt", listResult.Items[0].Name);
        Assert.Equal("b.txt", listResult.Items[1].Name);
    }

    [Fact]
    public async Task ListRemoteItemsAsync_PaginationWithCursor_ReturnsCorrectCursors()
    {
        var accountId = StorageAccountId.New();
        var items = new List<RemoteItem>
        {
            new("a.txt", RemotePath.Root.Combine("a.txt"), RemoteItemKind.File, 100, null),
        };
        var providerFactory = new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success(items),
            previousCursor: null, nextCursor: RemotePageCursor.FromOffset(2));
        var service = new RemoteBrowserAppService(
            new SingleAccountRepository(accountId, StorageProviderCategory.ObjectStorage),
            providerFactory);

        var result = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            accountId,
            RemotePath.Root,
            new RemotePageRequest(1)));

        Assert.True(result.IsSuccess);
        var listResult = result.GetValueOrThrow();
        Assert.Null(listResult.PreviousCursor);
        Assert.NotNull(listResult.NextCursor);
        Assert.True(listResult.HasNextPage);
        Assert.False(listResult.HasPreviousPage);
    }

    [Fact]
    public async Task ListRemoteItemsAsync_PassesSearchPrefixToProviderPageRequest()
    {
        var accountId = StorageAccountId.New();
        var providerFactory = new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
        var service = new RemoteBrowserAppService(
            new SingleAccountRepository(accountId, StorageProviderCategory.ObjectStorage),
            providerFactory);

        var result = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            accountId,
            new RemotePath("bucket-a/folder", RemotePathKind.Folder),
            new RemotePageRequest(50),
            SearchPrefix: "re"));

        Assert.True(result.IsSuccess);
        Assert.Equal("re", providerFactory.Provider.LastPageRequest?.SearchPrefix);
        Assert.Equal(50, providerFactory.Provider.LastPageRequest?.PageSize);
    }

    [Fact]
    public async Task ListRemoteItemsAsync_ProviderFailure_PropagatesError()
    {
        var accountId = StorageAccountId.New();
        var providerFactory = new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Failure(
            new StorageError(StorageErrorCode.ProviderUnavailable, "list failed", StorageErrorCategory.Provider, isRetryable: true)));
        var service = new RemoteBrowserAppService(
            new SingleAccountRepository(accountId, StorageProviderCategory.ObjectStorage),
            providerFactory);

        var result = await service.ListRemoteItemsAsync(new ListRemoteItemsRequest(
            accountId,
            RemotePath.Root,
            RemotePageRequest.FirstPage));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Provider, result.Error?.Category);
    }

    [Fact]
    public async Task DeleteRemoteItemAsync_EmptyStorageAccountId_ReturnsValidationFailure()
    {
        var service = new RemoteBrowserAppService(
            new EmptyAccountRepository(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = await service.DeleteRemoteItemAsync(new DeleteRemoteItemRequest(
            default(StorageAccountId),
            new RemotePath("file.txt"),
            RemoteItemKind.File));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task DeleteRemoteItemAsync_FolderKind_UsesAccountLookup()
    {
        var service = new RemoteBrowserAppService(
            new EmptyAccountRepository(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = await service.DeleteRemoteItemAsync(new DeleteRemoteItemRequest(
            StorageAccountId.New(),
            new RemotePath("folder"),
            RemoteItemKind.Folder));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task DeleteRemoteItemAsync_NonExistentAccount_ReturnsNotFound()
    {
        var service = new RemoteBrowserAppService(
            new EmptyAccountRepository(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = await service.DeleteRemoteItemAsync(new DeleteRemoteItemRequest(
            StorageAccountId.New(),
            new RemotePath("file.txt"),
            RemoteItemKind.File));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task DeleteRemoteItemAsync_ValidFileDelete_Succeeds()
    {
        var accountId = StorageAccountId.New();
        var providerFactory = new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
        var service = new RemoteBrowserAppService(
            new SingleAccountRepository(accountId, StorageProviderCategory.ObjectStorage),
            providerFactory);

        var result = await service.DeleteRemoteItemAsync(new DeleteRemoteItemRequest(
            accountId,
            new RemotePath("file.txt"),
            RemoteItemKind.File));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteRemoteItemAsync_ProviderCreationFailure_PropagatesError()
    {
        var accountId = StorageAccountId.New();
        var service = new RemoteBrowserAppService(
            new SingleAccountRepository(accountId, StorageProviderCategory.ObjectStorage),
            new FailProviderFactory());

        var result = await service.DeleteRemoteItemAsync(new DeleteRemoteItemRequest(
            accountId,
            new RemotePath("file.txt"),
            RemoteItemKind.File));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void GetPathContext_RootPath_ReturnsIsRoot()
    {
        var service = new RemoteBrowserAppService(
            new EmptyAccountRepository(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = service.GetPathContext(new GetRemotePathContextRequest(RemotePath.Root));

        Assert.True(result.IsSuccess);
        var ctx = result.GetValueOrThrow();
        Assert.True(ctx.IsRoot);
        Assert.False(ctx.IsBucketRoot);
        Assert.False(ctx.CanUpload);
        Assert.False(ctx.CanDeleteSelectedFile);
        Assert.Null(ctx.ParentPath);
    }

    [Fact]
    public void GetPathContext_BucketRoot_ReturnsIsBucketRoot()
    {
        var service = new RemoteBrowserAppService(
            new EmptyAccountRepository(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var path = new RemotePath("assets", RemotePathKind.BucketRoot);
        var result = service.GetPathContext(new GetRemotePathContextRequest(path));

        Assert.True(result.IsSuccess);
        var ctx = result.GetValueOrThrow();
        Assert.True(ctx.IsBucketRoot);
        Assert.False(ctx.IsRoot);
        Assert.True(ctx.CanUpload);
        Assert.True(ctx.CanDeleteSelectedFile);
    }

    [Fact]
    public void GetPathContext_NestedPath_HasParent()
    {
        var service = new RemoteBrowserAppService(
            new EmptyAccountRepository(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var path = RemotePath.Root.Combine("folder").Combine("file.txt");
        var result = service.GetPathContext(new GetRemotePathContextRequest(path));

        Assert.True(result.IsSuccess);
        var ctx = result.GetValueOrThrow();
        Assert.False(ctx.IsRoot);
        Assert.False(ctx.IsBucketRoot);
        Assert.True(ctx.CanUpload);
        Assert.True(ctx.CanDeleteSelectedFile);
        Assert.NotNull(ctx.ParentPath);
    }

    [Fact]
    public void GetPathContext_WithCursors_SetsPageNavigation()
    {
        var service = new RemoteBrowserAppService(
            new EmptyAccountRepository(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var path = new RemotePath("folder", RemotePathKind.Folder);
        var result = RemotePathContextResult.From(path,
            previousCursor: new RemotePageCursor("prev"),
            nextCursor: new RemotePageCursor("next"));

        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
        Assert.Equal(path, result.CurrentPath);
        Assert.NotNull(result.ParentPath);
    }

    private sealed class EmptyAccountRepository : IStorageAccountRepository
    {
        public Task<OperationResult<StorageAccount>> GetByIdAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<StorageAccount>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(OperationResult<IReadOnlyList<StorageAccount>>.Success([]));

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class SingleAccountRepository : IStorageAccountRepository
    {
        private readonly StorageAccount _account;

        public SingleAccountRepository(StorageAccountId accountId, StorageProviderCategory category)
        {
            var now = DateTimeOffset.UtcNow;
            _account = new StorageAccount(
                accountId, category, new StorageProviderId("test-provider"),
                "Test Account", null, null, new CredentialRef("cred-1"), now, now);
        }

        public Task<OperationResult<StorageAccount>> GetByIdAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(accountId == _account.Id
                ? OperationResult<StorageAccount>.Success(_account)
                : OperationResult<StorageAccount>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(OperationResult<IReadOnlyList<StorageAccount>>.Success([_account]));

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class MultipleAccountsRepository : IStorageAccountRepository
    {
        public Task<OperationResult<StorageAccount>> GetByIdAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<StorageAccount>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var accounts = new[]
            {
                new StorageAccount(StorageAccountId.New(), StorageProviderCategory.ObjectStorage,
                    new StorageProviderId("aliyun-oss"), "OSS 1", null, null,
                    new CredentialRef("cred-1"), now, now),
                new StorageAccount(StorageAccountId.New(), StorageProviderCategory.ObjectStorage,
                    new StorageProviderId("aliyun-oss"), "OSS 2", null, null,
                    new CredentialRef("cred-2"), now, now),
                new StorageAccount(StorageAccountId.New(), StorageProviderCategory.FileTransfer,
                    new StorageProviderId("sftp"), "SFTP", null, null,
                    new CredentialRef("cred-3"), now, now),
            };
            return Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(
                OperationResult<IReadOnlyList<StorageAccount>>.Success(accounts));
        }

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class FailAccountRepository : IStorageAccountRepository
    {
        public Task<OperationResult<StorageAccount>> GetByIdAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<StorageAccount>.Failure(
                new StorageError(StorageErrorCode.InfrastructureUnavailable, "fail", StorageErrorCategory.Infrastructure)));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(
                OperationResult<IReadOnlyList<StorageAccount>>.Failure(
                    new StorageError(StorageErrorCode.InfrastructureUnavailable, "fail", StorageErrorCategory.Infrastructure)));

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class ThrowingProviderFactory : IStorageProviderFactory
    {
        public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Should not be called.");
    }

    private sealed class FailProviderFactory : IStorageProviderFactory
    {
        public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IStorageProvider>.Failure(
                new StorageError(StorageErrorCode.ProviderUnavailable, "fail", StorageErrorCategory.Provider)));
    }

    private sealed class ProbeProviderFactory : IStorageProviderFactory
    {
        private readonly ProbeStorageProvider _provider;

        public ProbeProviderFactory(
            OperationResult<IReadOnlyList<RemoteItem>> probeResult,
            RemotePageCursor? previousCursor = null,
            RemotePageCursor? nextCursor = null)
        {
            _provider = new ProbeStorageProvider(probeResult, previousCursor, nextCursor);
        }

        public ProbeStorageProvider Provider => _provider;

        public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IStorageProvider>.Success(_provider));
    }

    private sealed class ProbeStorageProvider : IStorageProvider
    {
        private readonly OperationResult<IReadOnlyList<RemoteItem>> _probeResult;
        private readonly RemotePageCursor? _previousCursor;
        private readonly RemotePageCursor? _nextCursor;

        public ProbeStorageProvider(
            OperationResult<IReadOnlyList<RemoteItem>> probeResult,
            RemotePageCursor? previousCursor = null,
            RemotePageCursor? nextCursor = null)
        {
            _probeResult = probeResult;
            _previousCursor = previousCursor;
            _nextCursor = nextCursor;
        }

        public StorageCapabilitySet Capabilities => StorageCapabilitySet.Empty;

        public RemotePageRequest? LastPageRequest { get; private set; }

        public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(RemotePath path, CancellationToken cancellationToken = default)
            => Task.FromResult(_probeResult);

        public Task<OperationResult<RemoteItemPage>> ListPageAsync(RemotePath path, RemotePageRequest pageRequest, CancellationToken cancellationToken = default)
        {
            LastPageRequest = pageRequest;
            if (_probeResult.IsFailure)
            {
                return Task.FromResult(OperationResult<RemoteItemPage>.Failure(_probeResult.Error!));
            }

            var page = new RemoteItemPage(
                path,
                _probeResult.GetValueOrThrow(),
                _previousCursor,
                _nextCursor,
                pageRequest.PageSize);
            return Task.FromResult(OperationResult<RemoteItemPage>.Success(page));
        }

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
