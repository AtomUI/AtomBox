using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Transfers;

public interface ILocalTransferFileStore
{
    Task<OperationResult<LocalTransferReadHandle>> OpenReadAsync(
        LocalPath path,
        CancellationToken cancellationToken = default);

    Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
        LocalPath path,
        CancellationToken cancellationToken = default);

    Task<OperationResult<LocalTransferWriteHandle>> OpenWriteAsync(
        LocalPath path,
        TransferOverwritePolicy overwritePolicy,
        CancellationToken cancellationToken = default)
    {
        return OpenWriteAsync(path, cancellationToken);
    }
}
