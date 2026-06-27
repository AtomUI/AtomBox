using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Accounts;

public sealed class AccountAppService
{
    private readonly IStorageAccountRepository _accounts;
    private readonly ITransferTaskStore _transferTasks;
    private readonly IStorageProviderFactory _providerFactory;

    public AccountAppService(
        IStorageAccountRepository accounts,
        ITransferTaskStore transferTasks,
        IStorageProviderFactory providerFactory)
    {
        _accounts = accounts;
        _transferTasks = transferTasks;
        _providerFactory = providerFactory;
    }

    public async Task<OperationResult<StorageAccountSummary>> AddAsync(
        AddStorageAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProviderId.IsEmpty)
        {
            return OperationResult<StorageAccountSummary>.Failure(StorageError.Validation("Storage provider id is required."));
        }

        if (request.CredentialRef.IsEmpty)
        {
            return OperationResult<StorageAccountSummary>.Failure(StorageError.Validation("Credential reference is required."));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return OperationResult<StorageAccountSummary>.Failure(StorageError.Validation("Storage account display name is required."));
        }

        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            request.ProviderCategory,
            request.ProviderId,
            request.DisplayName,
            request.Endpoint,
            request.Region,
            request.CredentialRef,
            now,
            now,
            request.ProviderConfig);

        var addResult = await _accounts.AddAsync(account, cancellationToken).ConfigureAwait(false);
        return addResult.IsFailure
            ? OperationResult<StorageAccountSummary>.Failure(addResult.Error!)
            : OperationResult<StorageAccountSummary>.Success(StorageAccountSummary.From(account));
    }

    public async Task<OperationResult<StorageAccountSummary>> UpdateAsync(
        UpdateStorageAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Id.IsEmpty)
        {
            return OperationResult<StorageAccountSummary>.Failure(StorageError.Validation("Storage account id is required."));
        }

        if (request.CredentialRef.IsEmpty)
        {
            return OperationResult<StorageAccountSummary>.Failure(StorageError.Validation("Credential reference is required."));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return OperationResult<StorageAccountSummary>.Failure(StorageError.Validation("Storage account display name is required."));
        }

        var currentResult = await _accounts.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (currentResult.IsFailure)
        {
            return OperationResult<StorageAccountSummary>.Failure(currentResult.Error!);
        }

        var current = currentResult.GetValueOrThrow();
        var updated = current.UpdateConfiguration(
            request.DisplayName,
            request.Endpoint,
            request.Region,
            request.CredentialRef,
            DateTimeOffset.UtcNow,
            request.ProviderConfig);

        var updateResult = await _accounts.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        return updateResult.IsFailure
            ? OperationResult<StorageAccountSummary>.Failure(updateResult.Error!)
            : OperationResult<StorageAccountSummary>.Success(StorageAccountSummary.From(updated));
    }

    public async Task<OperationResult> DeleteAsync(
        DeleteStorageAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Id.IsEmpty)
        {
            return OperationResult.Failure(StorageError.Validation("Storage account id is required."));
        }

        var tasksResult = await _transferTasks.ListAsync(cancellationToken).ConfigureAwait(false);
        if (tasksResult.IsFailure)
        {
            return OperationResult.Failure(tasksResult.Error!);
        }

        var hasUnfinishedTask = tasksResult.GetValueOrThrow().Any(task =>
            task.StorageAccountId == request.Id &&
            task.Status is TransferStatus.Pending or TransferStatus.Running or TransferStatus.Paused or TransferStatus.Interrupted);

        if (hasUnfinishedTask)
        {
            return OperationResult.Failure(new StorageError(
                StorageErrorCode.Conflict,
                "Storage account is still referenced by unfinished transfer tasks.",
                StorageErrorCategory.Conflict));
        }

        return await _accounts.DeleteAsync(request.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult<IReadOnlyList<StorageAccountSummary>>> ListAsync(
        ListStorageAccountsRequest request,
        CancellationToken cancellationToken = default)
    {
        var accountsResult = await _accounts.ListAsync(cancellationToken).ConfigureAwait(false);
        if (accountsResult.IsFailure)
        {
            return OperationResult<IReadOnlyList<StorageAccountSummary>>.Failure(accountsResult.Error!);
        }

        var summaries = accountsResult.GetValueOrThrow()
            .Where(account => request.ProviderCategory is null || account.ProviderCategory == request.ProviderCategory)
            .Select(StorageAccountSummary.From)
            .ToArray();

        return OperationResult<IReadOnlyList<StorageAccountSummary>>.Success(summaries);
    }

    public async Task<OperationResult<TestConnectionResult>> TestConnectionAsync(
        TestConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Id.IsEmpty)
        {
            return OperationResult<TestConnectionResult>.Failure(StorageError.Validation("Storage account id is required."));
        }

        var accountResult = await _accounts.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return OperationResult<TestConnectionResult>.Failure(accountResult.Error!);
        }

        return await TestConnectionAsync(accountResult.GetValueOrThrow(), cancellationToken).ConfigureAwait(false);
    }

    public Task<OperationResult<TestConnectionResult>> TestConnectionDraftAsync(
        TestConnectionDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProviderId.IsEmpty)
        {
            return Task.FromResult(OperationResult<TestConnectionResult>.Failure(
                StorageError.Validation("Storage provider id is required.")));
        }

        if (request.CredentialRef.IsEmpty)
        {
            return Task.FromResult(OperationResult<TestConnectionResult>.Failure(
                StorageError.Validation("Credential reference is required.")));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Task.FromResult(OperationResult<TestConnectionResult>.Failure(
                StorageError.Validation("Storage account display name is required.")));
        }

        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            request.ProviderCategory,
            request.ProviderId,
            request.DisplayName,
            request.Endpoint,
            request.Region,
            request.CredentialRef,
            now,
            now,
            request.ProviderConfig);

        return TestConnectionAsync(account, cancellationToken);
    }

    private async Task<OperationResult<TestConnectionResult>> TestConnectionAsync(
        StorageAccount account,
        CancellationToken cancellationToken)
    {
        var providerResult = await _providerFactory.CreateAsync(account, cancellationToken).ConfigureAwait(false);
        if (providerResult.IsFailure)
        {
            return OperationResult<TestConnectionResult>.Failure(providerResult.Error!);
        }

        await using var provider = providerResult.GetValueOrThrow();
        var probePath = GetConnectionProbePath(account);
        var probeResult = await provider.ListAsync(probePath, cancellationToken).ConfigureAwait(false);
        if (probeResult.IsFailure)
        {
            return OperationResult<TestConnectionResult>.Failure(probeResult.Error!);
        }

        return OperationResult<TestConnectionResult>.Success(new TestConnectionResult(
            true,
            provider.Capabilities,
            account.ProviderId,
            probePath.ToString(),
            account.Endpoint,
            account.Region,
            account.GetProviderConfigValue("bucket"),
            provider is IRemoteHomePathProvider homePathProvider ? homePathProvider.HomePath : null));
    }

    private static RemotePath GetConnectionProbePath(StorageAccount account)
    {
        return account.ProviderCategory == StorageProviderCategory.ObjectStorage &&
            account.GetProviderConfigValue("bucket") is { Length: > 0 } bucket
            ? new RemotePath(bucket, RemotePathKind.BucketRoot)
            : RemotePath.Root;
    }
}
