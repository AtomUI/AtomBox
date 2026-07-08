using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Fingerprints;

public sealed record FileFingerprintRecord
{
    public FileFingerprintRecord(
        string hashAlgorithm,
        string hashValue,
        long fileSize,
        StorageAccountId storageAccountId,
        StorageProviderId providerId,
        RemotePath remotePath,
        DateTimeOffset uploadedAt,
        DateTimeOffset? lastSeenAt = null,
        string? etag = null)
    {
        if (string.IsNullOrWhiteSpace(hashAlgorithm))
        {
            throw new ArgumentException("Hash algorithm is required.", nameof(hashAlgorithm));
        }

        if (string.IsNullOrWhiteSpace(hashValue))
        {
            throw new ArgumentException("Hash value is required.", nameof(hashValue));
        }

        if (fileSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSize), "File size cannot be negative.");
        }

        if (storageAccountId.IsEmpty)
        {
            throw new ArgumentException("Storage account id is required.", nameof(storageAccountId));
        }

        if (providerId.IsEmpty)
        {
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        }

        if (remotePath.IsRoot)
        {
            throw new ArgumentException("Remote path is required.", nameof(remotePath));
        }

        HashAlgorithm = hashAlgorithm.Trim().ToLowerInvariant();
        HashValue = hashValue.Trim().ToLowerInvariant();
        FileSize = fileSize;
        StorageAccountId = storageAccountId;
        ProviderId = providerId;
        RemotePath = remotePath;
        UploadedAt = uploadedAt;
        LastSeenAt = lastSeenAt;
        ETag = string.IsNullOrWhiteSpace(etag) ? null : etag.Trim();
    }

    public string HashAlgorithm { get; }

    public string HashValue { get; }

    public long FileSize { get; }

    public StorageAccountId StorageAccountId { get; }

    public StorageProviderId ProviderId { get; }

    public RemotePath RemotePath { get; }

    public DateTimeOffset UploadedAt { get; }

    public DateTimeOffset? LastSeenAt { get; }

    public string? ETag { get; }

    public bool Matches(FileFingerprintQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return string.Equals(HashAlgorithm, query.HashAlgorithm, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(HashValue, query.HashValue, StringComparison.OrdinalIgnoreCase) &&
               FileSize == query.FileSize &&
               StorageAccountId == query.StorageAccountId;
    }

    public FileFingerprintRecord WithLastSeenAt(DateTimeOffset lastSeenAt)
    {
        return new FileFingerprintRecord(
            HashAlgorithm,
            HashValue,
            FileSize,
            StorageAccountId,
            ProviderId,
            RemotePath,
            UploadedAt,
            lastSeenAt,
            ETag);
    }
}
