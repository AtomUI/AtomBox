using AtomBox.Core.Results;

namespace AtomBox.Core.Fingerprints;

public interface IFileFingerprintIndexStore
{
    Task<OperationResult<IReadOnlyList<FileFingerprintRecord>>> FindAsync(
        FileFingerprintQuery query,
        CancellationToken cancellationToken = default);

    Task<OperationResult> AddOrUpdateAsync(
        FileFingerprintRecord record,
        CancellationToken cancellationToken = default);

    Task<OperationResult<FileFingerprintIndexStatistics>> GetStatisticsAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult> ClearAsync(
        CancellationToken cancellationToken = default);
}
