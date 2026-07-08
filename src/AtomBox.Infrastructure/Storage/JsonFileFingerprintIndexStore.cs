using AtomBox.Core.Fingerprints;
using AtomBox.Core.Results;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Infrastructure.Storage;

public sealed class JsonFileFingerprintIndexStore : IFileFingerprintIndexStore
{
    private readonly JsonFileStore<FileFingerprintIndexDocument> _store;
    private readonly string _path;

    public JsonFileFingerprintIndexStore(AtomBoxStoragePaths paths)
    {
        _path = paths.FileFingerprintIndexFile;
        _store = new JsonFileStore<FileFingerprintIndexDocument>(_path);
    }

    public async Task<OperationResult<IReadOnlyList<FileFingerprintRecord>>> FindAsync(
        FileFingerprintQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var documentResult = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (documentResult.IsFailure)
        {
            return OperationResult<IReadOnlyList<FileFingerprintRecord>>.Failure(documentResult.Error!);
        }

        var now = DateTimeOffset.UtcNow;
        var document = documentResult.GetValueOrThrow();
        var matches = document.Records
            .Where(record => record.Matches(query))
            .Select(record => record.WithLastSeenAt(now))
            .ToArray();

        if (matches.Length == 0)
        {
            return OperationResult<IReadOnlyList<FileFingerprintRecord>>.Success(Array.Empty<FileFingerprintRecord>());
        }

        var updatedRecords = document.Records
            .Select(record =>
            {
                var updated = matches.FirstOrDefault(match =>
                    SameTarget(match, record) &&
                    string.Equals(match.HashAlgorithm, record.HashAlgorithm, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(match.HashValue, record.HashValue, StringComparison.OrdinalIgnoreCase) &&
                    match.FileSize == record.FileSize);
                return updated ?? record;
            })
            .ToArray();
        var writeResult = await _store
            .WriteAsync(new FileFingerprintIndexDocument(document.SchemaVersion, updatedRecords), cancellationToken)
            .ConfigureAwait(false);

        return writeResult.IsFailure
            ? OperationResult<IReadOnlyList<FileFingerprintRecord>>.Failure(writeResult.Error!)
            : OperationResult<IReadOnlyList<FileFingerprintRecord>>.Success(matches);
    }

    public async Task<OperationResult> AddOrUpdateAsync(
        FileFingerprintRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var documentResult = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (documentResult.IsFailure)
        {
            return OperationResult.Failure(documentResult.Error!);
        }

        var document = documentResult.GetValueOrThrow();
        var records = document.Records.ToList();
        var normalized = record.LastSeenAt is null ? record.WithLastSeenAt(record.UploadedAt) : record;
        var index = records.FindIndex(existing =>
            SameTarget(existing, normalized) &&
            string.Equals(existing.HashAlgorithm, normalized.HashAlgorithm, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.HashValue, normalized.HashValue, StringComparison.OrdinalIgnoreCase) &&
            existing.FileSize == normalized.FileSize);

        if (index < 0)
        {
            records.Add(normalized);
        }
        else
        {
            records[index] = normalized;
        }

        return await _store
            .WriteAsync(new FileFingerprintIndexDocument(document.SchemaVersion, records), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<FileFingerprintIndexStatistics>> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var documentResult = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (documentResult.IsFailure)
        {
            return OperationResult<FileFingerprintIndexStatistics>.Failure(documentResult.Error!);
        }

        var records = documentResult.GetValueOrThrow().Records;
        DateTimeOffset? lastUpdatedAt = records.Count == 0
            ? null
            : records.Max(record => record.LastSeenAt ?? record.UploadedAt);
        return OperationResult<FileFingerprintIndexStatistics>.Success(
            new FileFingerprintIndexStatistics(_path, records.Count, lastUpdatedAt));
    }

    public Task<OperationResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        return _store.WriteAsync(FileFingerprintIndexDocument.Empty, cancellationToken);
    }

    private Task<OperationResult<FileFingerprintIndexDocument>> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        return _store.ReadAsync(FileFingerprintIndexDocument.Empty, cancellationToken);
    }

    private static bool SameTarget(FileFingerprintRecord left, FileFingerprintRecord right)
    {
        return left.StorageAccountId == right.StorageAccountId &&
               left.ProviderId == right.ProviderId &&
               left.RemotePath == right.RemotePath;
    }

    private sealed record FileFingerprintIndexDocument(
        int SchemaVersion,
        IReadOnlyList<FileFingerprintRecord> Records)
    {
        public static FileFingerprintIndexDocument Empty { get; } = new(1, Array.Empty<FileFingerprintRecord>());
    }
}
