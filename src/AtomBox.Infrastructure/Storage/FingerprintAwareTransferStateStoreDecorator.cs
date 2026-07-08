using AtomBox.Core.Accounts;
using AtomBox.Core.Fingerprints;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;

namespace AtomBox.Infrastructure.Storage;

public sealed class FingerprintAwareTransferStateStoreDecorator : ITransferStateStore
{
    private readonly TransferStateStore _inner;
    private readonly IFileFingerprintIndexStore _fingerprints;
    private readonly IApplicationSettingsRepository _settings;
    private readonly IStorageAccountRepository _accounts;

    public FingerprintAwareTransferStateStoreDecorator(
        TransferStateStore inner,
        IFileFingerprintIndexStore fingerprints,
        IApplicationSettingsRepository settings,
        IStorageAccountRepository accounts)
    {
        _inner = inner;
        _fingerprints = fingerprints;
        _settings = settings;
        _accounts = accounts;
    }

    public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListQueueAsync(
        CancellationToken cancellationToken = default)
    {
        return _inner.ListQueueAsync(cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<TransferStateSnapshot>>> ListHistoryAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        return _inner.ListHistoryAsync(skip, take, cancellationToken);
    }

    public async Task<OperationResult> UpdateStatusAsync(
        TransferTask task,
        TransferProgress? progress,
        CancellationToken cancellationToken = default)
    {
        var saveResult = await _inner.UpdateStatusAsync(task, progress, cancellationToken).ConfigureAwait(false);
        if (saveResult.IsFailure || !ShouldWriteFingerprint(task))
        {
            return saveResult;
        }

        var settingsResult = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (settingsResult.IsFailure || !settingsResult.GetValueOrThrow().EnableUploadFingerprintIndex)
        {
            return saveResult;
        }

        var accountResult = await _accounts.GetByIdAsync(task.StorageAccountId, cancellationToken).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return saveResult;
        }

        var account = accountResult.GetValueOrThrow();
        var record = new FileFingerprintRecord(
            task.FingerprintHashAlgorithm!,
            task.FingerprintHashValue!,
            task.FingerprintFileSize!.Value,
            task.StorageAccountId,
            account.ProviderId,
            task.RemotePath,
            task.UpdatedAt,
            task.UpdatedAt);

        _ = await _fingerprints.AddOrUpdateAsync(record, cancellationToken).ConfigureAwait(false);
        return saveResult;
    }

    private static bool ShouldWriteFingerprint(TransferTask task)
    {
        return task.Direction == TransferDirection.Upload &&
               task.Status == TransferStatus.Succeeded &&
               task.HasCompleteFingerprintMetadata &&
               !task.RemotePath.IsRoot;
    }
}
