using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Accounts;

public interface IStorageAccountRepository
{
    Task<OperationResult<StorageAccount>> GetByIdAsync(
        StorageAccountId accountId,
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<StorageAccount>>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult> AddAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAsync(
        StorageAccountId accountId,
        CancellationToken cancellationToken = default);
}
