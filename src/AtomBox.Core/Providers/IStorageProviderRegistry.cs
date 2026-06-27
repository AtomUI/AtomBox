using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Providers;

public interface IStorageProviderRegistry
{
    IReadOnlyList<ProviderDescriptor> GetAll();

    OperationResult<ProviderDescriptor> GetById(StorageProviderId providerId);
}
