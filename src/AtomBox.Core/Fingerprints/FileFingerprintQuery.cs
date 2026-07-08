using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Fingerprints;

public sealed record FileFingerprintQuery
{
    public FileFingerprintQuery(
        string hashAlgorithm,
        string hashValue,
        long fileSize,
        StorageAccountId storageAccountId)
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

        HashAlgorithm = hashAlgorithm.Trim().ToLowerInvariant();
        HashValue = hashValue.Trim().ToLowerInvariant();
        FileSize = fileSize;
        StorageAccountId = storageAccountId;
    }

    public string HashAlgorithm { get; }

    public string HashValue { get; }

    public long FileSize { get; }

    public StorageAccountId StorageAccountId { get; }
}
