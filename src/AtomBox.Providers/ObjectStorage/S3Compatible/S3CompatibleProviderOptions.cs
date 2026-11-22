using AtomBox.Core.Errors;

namespace AtomBox.Providers.ObjectStorage.S3Compatible;

internal sealed record S3CompatibleProviderOptions(
    string CursorNamespace,
    string? RootBucket = null,
    Func<string, StorageError?>? ValidateBucketName = null)
{
    public static S3CompatibleProviderOptions Default { get; } = new("s3-compatible");
}
