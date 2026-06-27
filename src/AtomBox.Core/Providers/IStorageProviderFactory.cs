using AtomBox.Core.Accounts;
using AtomBox.Core.Results;

namespace AtomBox.Core.Providers;

public interface IStorageProviderFactory
{
    Task<OperationResult<IStorageProvider>> CreateAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default);
}
