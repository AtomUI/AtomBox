using AtomBox.Core.Accounts;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;

namespace AtomBox.Providers.Common;

public interface IStorageProviderCreator
{
    ProviderDescriptor Descriptor { get; }

    Task<OperationResult<IStorageProvider>> CreateAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default);
}
