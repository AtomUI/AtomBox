namespace AtomBox.Core.Fingerprints;

public sealed record FileFingerprintIndexStatistics(
    string IndexFilePath,
    int RecordCount,
    DateTimeOffset? LastUpdatedAt);
