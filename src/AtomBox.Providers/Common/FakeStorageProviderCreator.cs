using AtomBox.Core.Accounts;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;

namespace AtomBox.Providers.Common;

public sealed class FakeStorageProviderCreator : IStorageProviderCreator
{
    public FakeStorageProviderCreator(ProviderDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
    }

    public ProviderDescriptor Descriptor { get; }

    public Task<OperationResult<IStorageProvider>> CreateAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        IStorageProvider provider = new FakeStorageProvider(Descriptor);
        return Task.FromResult(OperationResult<IStorageProvider>.Success(provider));
    }
}
