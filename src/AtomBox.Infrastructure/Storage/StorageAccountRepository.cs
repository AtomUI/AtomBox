using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Infrastructure.Storage;

public sealed class StorageAccountRepository : IStorageAccountRepository
{
    private readonly JsonFileStore<List<StorageAccount>> _store;

    public StorageAccountRepository(AtomBoxStoragePaths paths)
    {
        _store = new JsonFileStore<List<StorageAccount>>(paths.AccountsFile);
    }

    public async Task<OperationResult<StorageAccount>> GetByIdAsync(
        StorageAccountId accountId,
        CancellationToken cancellationToken = default)
    {
        var accountsResult = await ReadAccountsAsync(cancellationToken).ConfigureAwait(false);
        if (accountsResult.IsFailure)
        {
            return OperationResult<StorageAccount>.Failure(accountsResult.Error!);
        }

        var account = accountsResult.GetValueOrThrow().FirstOrDefault(item => item.Id == accountId);
        return account is null
            ? OperationResult<StorageAccount>.Failure(StorageError.NotFound("Storage account was not found."))
            : OperationResult<StorageAccount>.Success(account);
    }

    public async Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var accountsResult = await ReadAccountsAsync(cancellationToken).ConfigureAwait(false);
        return accountsResult.IsFailure
            ? OperationResult<IReadOnlyList<StorageAccount>>.Failure(accountsResult.Error!)
            : OperationResult<IReadOnlyList<StorageAccount>>.Success(accountsResult.GetValueOrThrow());
    }

    public async Task<OperationResult> AddAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default)
    {
        var accountsResult = await ReadAccountsAsync(cancellationToken).ConfigureAwait(false);
        if (accountsResult.IsFailure)
        {
            return OperationResult.Failure(accountsResult.Error!);
        }

        var accounts = accountsResult.GetValueOrThrow();
        if (accounts.Any(item => item.Id == account.Id))
        {
            return OperationResult.Failure(new StorageError(
                StorageErrorCode.Conflict,
                "Storage account already exists.",
                StorageErrorCategory.Conflict));
        }

        accounts.Add(account);
        return await _store.WriteAsync(accounts, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> UpdateAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default)
    {
        var accountsResult = await ReadAccountsAsync(cancellationToken).ConfigureAwait(false);
        if (accountsResult.IsFailure)
        {
            return OperationResult.Failure(accountsResult.Error!);
        }

        var accounts = accountsResult.GetValueOrThrow();
        var index = accounts.FindIndex(item => item.Id == account.Id);
        if (index < 0)
        {
            return OperationResult.Failure(StorageError.NotFound("Storage account was not found."));
        }

        accounts[index] = account;
        return await _store.WriteAsync(accounts, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> DeleteAsync(
        StorageAccountId accountId,
        CancellationToken cancellationToken = default)
    {
        var accountsResult = await ReadAccountsAsync(cancellationToken).ConfigureAwait(false);
        if (accountsResult.IsFailure)
        {
            return OperationResult.Failure(accountsResult.Error!);
        }

        var accounts = accountsResult.GetValueOrThrow();
        var removed = accounts.RemoveAll(item => item.Id == accountId);
        return removed == 0
            ? OperationResult.Failure(StorageError.NotFound("Storage account was not found."))
            : await _store.WriteAsync(accounts, cancellationToken).ConfigureAwait(false);
    }

    private Task<OperationResult<List<StorageAccount>>> ReadAccountsAsync(CancellationToken cancellationToken)
    {
        return _store.ReadAsync([], cancellationToken);
    }
}
