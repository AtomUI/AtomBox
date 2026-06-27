using AtomBox.Application.Accounts;
using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Tests;

public sealed class AccountAppServiceContractTests
{
    private static readonly CancellationToken CT = CancellationToken.None;

    [Fact]
    public async Task AddAsync_EmptyProviderId_ReturnsValidationFailure()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            default(StorageProviderId),
            "Test",
            null,
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task AddAsync_EmptyCredentialRef_ReturnsValidationFailure()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Test",
            null,
            null,
            default(CredentialRef)));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task AddAsync_EmptyDisplayName_ReturnsValidationFailure()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "   ",
            null,
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task AddAsync_ValidRequest_ReturnsSuccessWithSummary()
    {
        var repo = new CaptureStorageAccountRepository();
        var service = new AccountAppService(
            repo,
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "My OSS",
            "oss-cn-hangzhou.aliyuncs.com",
            "cn-hangzhou",
            new CredentialRef("cred-1"),
            new Dictionary<string, string> { ["bucket"] = "assets" }));

        Assert.True(result.IsSuccess);
        Assert.NotNull(repo.SavedAccount);
        Assert.Equal("My OSS", repo.SavedAccount!.DisplayName);
        var summary = result.GetValueOrThrow();
        Assert.Equal("My OSS", summary.DisplayName);
        Assert.Equal("aliyun-oss", summary.ProviderId.Value);
        Assert.Equal("oss-cn-hangzhou.aliyuncs.com", summary.Endpoint);
    }

    [Fact]
    public async Task AddAsync_RepositoryFailure_PropagatesError()
    {
        var service = new AccountAppService(
            new FailStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "My OSS",
            null,
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Infrastructure, result.Error?.Category);
    }

    [Fact]
    public async Task UpdateAsync_EmptyId_ReturnsValidationFailure()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.UpdateAsync(new UpdateStorageAccountRequest(
            default(StorageAccountId),
            "Updated",
            null,
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task UpdateAsync_EmptyCredentialRef_ReturnsValidationFailure()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.UpdateAsync(new UpdateStorageAccountRequest(
            StorageAccountId.New(),
            "Updated",
            null,
            null,
            default(CredentialRef)));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task UpdateAsync_EmptyDisplayName_ReturnsValidationFailure()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.UpdateAsync(new UpdateStorageAccountRequest(
            StorageAccountId.New(),
            "",
            null,
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentAccount_ReturnsNotFound()
    {
        var service = new AccountAppService(
            new NotFoundStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.UpdateAsync(new UpdateStorageAccountRequest(
            StorageAccountId.New(),
            "Updated",
            null,
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_ReturnsUpdatedSummary()
    {
        var accountId = StorageAccountId.New();
        var repo = new SingleStorageAccountRepository(CreateAccount(accountId));
        var service = new AccountAppService(
            repo,
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.UpdateAsync(new UpdateStorageAccountRequest(
            accountId,
            "Updated Name",
            "new-endpoint.com",
            "new-region",
            new CredentialRef("cred-2")));

        Assert.True(result.IsSuccess);
        var summary = result.GetValueOrThrow();
        Assert.Equal("Updated Name", summary.DisplayName);
        Assert.Equal("new-endpoint.com", summary.Endpoint);
        Assert.Equal("cred-2", summary.CredentialRef.Value);
    }

    [Fact]
    public async Task UpdateAsync_RepositoryUpdateFailure_PropagatesError()
    {
        var repo = new FailOnUpdateStorageAccountRepository(CreateAccount(StorageAccountId.New()));
        var service = new AccountAppService(
            repo,
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.UpdateAsync(new UpdateStorageAccountRequest(
            repo.ExistingAccount.Id,
            "Updated",
            null,
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Infrastructure, result.Error?.Category);
    }

    [Fact]
    public async Task DeleteAsync_EmptyId_ReturnsValidationFailure()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.DeleteAsync(new DeleteStorageAccountRequest(default(StorageAccountId)));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task DeleteAsync_HasUnfinishedTasks_ReturnsConflict()
    {
        var accountId = StorageAccountId.New();
        var taskStore = new TaskStoreWithUnfinishedTasks(accountId);
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            taskStore,
            new ThrowingProviderFactory());

        var result = await service.DeleteAsync(new DeleteStorageAccountRequest(accountId));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, result.Error?.Category);
    }

    [Fact]
    public async Task DeleteAsync_NoUnfinishedTasks_CallsRepositoryDelete()
    {
        var accountId = StorageAccountId.New();
        var repo = new CountingStorageAccountRepository();
        var taskStore = new TaskStoreWithFinishedTasksOnly(accountId);
        var service = new AccountAppService(
            repo,
            taskStore,
            new ThrowingProviderFactory());

        var result = await service.DeleteAsync(new DeleteStorageAccountRequest(accountId));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repo.DeleteCalls);
    }

    [Fact]
    public async Task DeleteAsync_RepositoryFailure_PropagatesError()
    {
        var service = new AccountAppService(
            new FailStorageAccountRepository(),
            new CompletedTasksOnlyStore(),
            new ThrowingProviderFactory());

        var result = await service.DeleteAsync(new DeleteStorageAccountRequest(StorageAccountId.New()));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ListAsync_NoFilter_ReturnsAllAccounts()
    {
        var repo = new TwoAccountsRepository();
        var service = new AccountAppService(
            repo,
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.ListAsync(new ListStorageAccountsRequest());

        Assert.True(result.IsSuccess);
        var accounts = result.GetValueOrThrow();
        Assert.Equal(2, accounts.Count);
    }

    [Fact]
    public async Task ListAsync_FilterByCategory_ReturnsOnlyMatching()
    {
        var repo = new TwoAccountsRepository();
        var service = new AccountAppService(
            repo,
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.ListAsync(new ListStorageAccountsRequest(StorageProviderCategory.ObjectStorage));

        Assert.True(result.IsSuccess);
        var accounts = result.GetValueOrThrow();
        Assert.Single(accounts);
        Assert.Equal(StorageProviderCategory.ObjectStorage, accounts[0].ProviderCategory);
    }

    [Fact]
    public async Task ListAsync_NoAccounts_ReturnsEmpty()
    {
        var service = new AccountAppService(
            new EmptyStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.ListAsync(new ListStorageAccountsRequest());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task ListAsync_RepositoryFailure_PropagatesError()
    {
        var service = new AccountAppService(
            new FailStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ThrowingProviderFactory());

        var result = await service.ListAsync(new ListStorageAccountsRequest());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task TestConnectionAsync_EmptyId_ReturnsValidationFailure()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = await service.TestConnectionAsync(new TestConnectionRequest(default(StorageAccountId)));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task TestConnectionAsync_NonExistentAccount_ReturnsNotFound()
    {
        var service = new AccountAppService(
            new NotFoundStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = await service.TestConnectionAsync(new TestConnectionRequest(StorageAccountId.New()));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task TestConnectionAsync_ProviderCreationFailure_PropagatesError()
    {
        var accountId = StorageAccountId.New();
        var service = new AccountAppService(
            new SingleStorageAccountRepository(CreateAccount(accountId)),
            new EmptyTransferTaskStore(),
            new FailProviderFactory());

        var result = await service.TestConnectionAsync(new TestConnectionRequest(accountId));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Provider, result.Error?.Category);
    }

    [Fact]
    public async Task TestConnectionAsync_ProbeSuccess_ReturnsAvailable()
    {
        var accountId = StorageAccountId.New();
        var service = new AccountAppService(
            new SingleStorageAccountRepository(CreateAccount(accountId)),
            new EmptyTransferTaskStore(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = await service.TestConnectionAsync(new TestConnectionRequest(accountId));

        Assert.True(result.IsSuccess);
        Assert.True(result.GetValueOrThrow().IsAvailable);
    }

    [Fact]
    public async Task TestConnectionAsync_ProbeFailure_PropagatesError()
    {
        var accountId = StorageAccountId.New();
        var service = new AccountAppService(
            new SingleStorageAccountRepository(CreateAccount(accountId)),
            new EmptyTransferTaskStore(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Failure(
                new StorageError(StorageErrorCode.ProviderUnavailable, "probe failed", StorageErrorCategory.Provider, isRetryable: true))));

        var result = await service.TestConnectionAsync(new TestConnectionRequest(accountId));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Provider, result.Error?.Category);
    }

    [Fact]
    public async Task TestConnectionDraftAsync_EmptyProviderId_ReturnsValidationFailure()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = await service.TestConnectionDraftAsync(new TestConnectionDraftRequest(
            StorageProviderCategory.ObjectStorage,
            default(StorageProviderId),
            "Test",
            null,
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task TestConnectionDraftAsync_EmptyCredentialRef_ReturnsValidationFailure()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = await service.TestConnectionDraftAsync(new TestConnectionDraftRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Test",
            null,
            null,
            default(CredentialRef)));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task TestConnectionDraftAsync_EmptyDisplayName_ReturnsValidationFailure()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([])));

        var result = await service.TestConnectionDraftAsync(new TestConnectionDraftRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "   ",
            null,
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Validation, result.Error?.Category);
    }

    [Fact]
    public async Task TestConnectionDraftAsync_ValidRequest_ProbesProvider()
    {
        var providerFactory = new ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>>.Success([]));
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            providerFactory);

        var result = await service.TestConnectionDraftAsync(new TestConnectionDraftRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "My OSS",
            "oss-cn-hangzhou.aliyuncs.com",
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsSuccess);
        Assert.True(result.GetValueOrThrow().IsAvailable);
        Assert.Equal(1, providerFactory.Provider.ListCalls);
        Assert.Equal(RemotePath.Root, providerFactory.Provider.LastPath);
    }

    [Fact]
    public async Task TestConnectionDraftAsync_ProviderCreationFailure_PropagatesError()
    {
        var service = new AccountAppService(
            new SuccessStorageAccountRepository(),
            new EmptyTransferTaskStore(),
            new FailProviderFactory());

        var result = await service.TestConnectionDraftAsync(new TestConnectionDraftRequest(
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "My OSS",
            null,
            null,
            new CredentialRef("cred-1")));

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Provider, result.Error?.Category);
    }

    private static StorageAccount CreateAccount(StorageAccountId? id = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            id ?? StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Aliyun OSS",
            "oss-cn-hangzhou.aliyuncs.com",
            null,
            new CredentialRef("cred-1"),
            now,
            now);
    }

    private sealed class SuccessStorageAccountRepository : IStorageAccountRepository
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

    private sealed class CaptureStorageAccountRepository : IStorageAccountRepository
    {
        public StorageAccount? SavedAccount { get; private set; }

        public Task<OperationResult<StorageAccount>> GetByIdAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<StorageAccount>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(OperationResult<IReadOnlyList<StorageAccount>>.Success(SavedAccount is null ? [] : [SavedAccount]));

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

    private sealed class FailStorageAccountRepository : IStorageAccountRepository
    {
        public Task<OperationResult<StorageAccount>> GetByIdAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<StorageAccount>.Failure(new StorageError(StorageErrorCode.InfrastructureUnavailable, "repo failure", StorageErrorCategory.Infrastructure)));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(OperationResult<IReadOnlyList<StorageAccount>>.Failure(new StorageError(StorageErrorCode.InfrastructureUnavailable, "repo failure", StorageErrorCategory.Infrastructure)));

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure(new StorageError(StorageErrorCode.InfrastructureUnavailable, "repo failure", StorageErrorCategory.Infrastructure)));

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure(new StorageError(StorageErrorCode.InfrastructureUnavailable, "repo failure", StorageErrorCategory.Infrastructure)));

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure(new StorageError(StorageErrorCode.InfrastructureUnavailable, "repo failure", StorageErrorCategory.Infrastructure)));
    }

    private sealed class EmptyStorageAccountRepository : IStorageAccountRepository
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

    private sealed class NotFoundStorageAccountRepository : IStorageAccountRepository
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

    private sealed class SingleStorageAccountRepository : IStorageAccountRepository
    {
        public SingleStorageAccountRepository(StorageAccount account) { ExistingAccount = account; }
        public StorageAccount ExistingAccount { get; }

        public Task<OperationResult<StorageAccount>> GetByIdAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(accountId == ExistingAccount.Id
                ? OperationResult<StorageAccount>.Success(ExistingAccount)
                : OperationResult<StorageAccount>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(OperationResult<IReadOnlyList<StorageAccount>>.Success([ExistingAccount]));

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class FailOnUpdateStorageAccountRepository : IStorageAccountRepository
    {
        public FailOnUpdateStorageAccountRepository(StorageAccount existingAccount) { ExistingAccount = existingAccount; }
        public StorageAccount ExistingAccount { get; }

        public Task<OperationResult<StorageAccount>> GetByIdAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(accountId == ExistingAccount.Id
                ? OperationResult<StorageAccount>.Success(ExistingAccount)
                : OperationResult<StorageAccount>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(OperationResult<IReadOnlyList<StorageAccount>>.Success([]));

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure(new StorageError(StorageErrorCode.InfrastructureUnavailable, "update failed", StorageErrorCategory.Infrastructure)));

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class CountingStorageAccountRepository : IStorageAccountRepository
    {
        public int DeleteCalls { get; private set; }

        public Task<OperationResult<StorageAccount>> GetByIdAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<StorageAccount>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(OperationResult<IReadOnlyList<StorageAccount>>.Success([]));

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class TwoAccountsRepository : IStorageAccountRepository
    {
        private readonly StorageAccount _objectAccount;
        private readonly StorageAccount _fileAccount;

        public TwoAccountsRepository()
        {
            var now = DateTimeOffset.UtcNow;
            _objectAccount = new StorageAccount(
                StorageAccountId.New(), StorageProviderCategory.ObjectStorage,
                new StorageProviderId("aliyun-oss"), "OSS", null, null,
                new CredentialRef("cred-1"), now, now);
            _fileAccount = new StorageAccount(
                StorageAccountId.New(), StorageProviderCategory.FileTransfer,
                new StorageProviderId("sftp"), "SFTP", null, null,
                new CredentialRef("cred-2"), now, now);
        }

        public Task<OperationResult<StorageAccount>> GetByIdAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<StorageAccount>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<StorageAccount>>>(OperationResult<IReadOnlyList<StorageAccount>>.Success(
                new[] { _objectAccount, _fileAccount }));

        public Task<OperationResult> AddAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> UpdateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(StorageAccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class EmptyTransferTaskStore : ITransferTaskStore
    {
        public Task<OperationResult<TransferTask>> GetByIdAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<TransferTask>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferTask>>>(OperationResult<IReadOnlyList<TransferTask>>.Success([]));

        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class TaskStoreWithUnfinishedTasks : ITransferTaskStore
    {
        private readonly TransferTask _unfinished;

        public TaskStoreWithUnfinishedTasks(StorageAccountId accountId)
        {
            var now = DateTimeOffset.UtcNow;
            _unfinished = new TransferTask(
                TransferTaskId.New(), accountId, TransferDirection.Upload,
                new LocalPath(@"C:\test.txt"), new RemotePath("bucket/test.txt"),
                TransferStatus.Running, new TransferOptions(TransferOverwritePolicy.Ask),
                now, now);
        }

        public Task<OperationResult<TransferTask>> GetByIdAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<TransferTask>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferTask>>>(OperationResult<IReadOnlyList<TransferTask>>.Success([_unfinished]));

        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class TaskStoreWithFinishedTasksOnly : ITransferTaskStore
    {
        private readonly TransferTask _finished;

        public TaskStoreWithFinishedTasksOnly(StorageAccountId accountId)
        {
            var now = DateTimeOffset.UtcNow;
            _finished = new TransferTask(
                TransferTaskId.New(), accountId, TransferDirection.Upload,
                new LocalPath(@"C:\test.txt"), new RemotePath("bucket/test.txt"),
                TransferStatus.Succeeded, new TransferOptions(TransferOverwritePolicy.Ask),
                now, now);
        }

        public Task<OperationResult<TransferTask>> GetByIdAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<TransferTask>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferTask>>>(OperationResult<IReadOnlyList<TransferTask>>.Success([_finished]));

        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class CompletedTasksOnlyStore : ITransferTaskStore
    {
        private readonly TransferTask _finished;

        public CompletedTasksOnlyStore()
        {
            var now = DateTimeOffset.UtcNow;
            _finished = new TransferTask(
                TransferTaskId.New(), StorageAccountId.New(), TransferDirection.Upload,
                new LocalPath(@"C:\test.txt"), new RemotePath("bucket/test.txt"),
                TransferStatus.Succeeded, new TransferOptions(TransferOverwritePolicy.Ask),
                now, now);
        }

        public Task<OperationResult<TransferTask>> GetByIdAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<TransferTask>.Failure(StorageError.NotFound("not found")));

        public Task<OperationResult<IReadOnlyList<TransferTask>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<OperationResult<IReadOnlyList<TransferTask>>>(OperationResult<IReadOnlyList<TransferTask>>.Success([_finished]));

        public Task<OperationResult> SaveAsync(TransferTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> DeleteAsync(TransferTaskId taskId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class ThrowingProviderFactory : IStorageProviderFactory
    {
        public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Provider factory should not be called.");
    }

    private sealed class ProbeProviderFactory : IStorageProviderFactory
    {
        public ProbeProviderFactory(OperationResult<IReadOnlyList<RemoteItem>> probeResult)
        {
            Provider = new ProbeStorageProvider(probeResult);
        }

        public ProbeStorageProvider Provider { get; }

        public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IStorageProvider>.Success(Provider));
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
        public StorageCapabilitySet Capabilities => StorageCapabilitySet.Empty;

        public Task<OperationResult<IReadOnlyList<RemoteItem>>> ListAsync(RemotePath path, CancellationToken cancellationToken = default)
        {
            ListCalls++;
            LastPath = path;
            return Task.FromResult(_probeResult);
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

    private sealed class FailProviderFactory : IStorageProviderFactory
    {
        public Task<OperationResult<IStorageProvider>> CreateAsync(StorageAccount account, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<IStorageProvider>.Failure(
                new StorageError(StorageErrorCode.ProviderUnavailable, "provider creation failed", StorageErrorCategory.Provider)));
    }
}
