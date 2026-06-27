using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.ObjectStorage.S3Compatible;

internal readonly record struct S3CompatiblePath(string BucketName, string KeyPrefix)
{
    public static OperationResult<S3CompatiblePath> FromRemotePath(RemotePath path)
    {
        if (path.IsRoot)
        {
            return OperationResult<S3CompatiblePath>.Failure(StorageError.Validation("Bucket path is required."));
        }

        var value = path.ToString().Trim('/');
        var separator = value.IndexOf('/', StringComparison.Ordinal);
        var bucketName = separator < 0 ? value : value[..separator];
        var keyPrefix = separator < 0 ? string.Empty : value[(separator + 1)..];

        return string.IsNullOrWhiteSpace(bucketName)
            ? OperationResult<S3CompatiblePath>.Failure(StorageError.Validation("Bucket name is required."))
            : OperationResult<S3CompatiblePath>.Success(new S3CompatiblePath(bucketName, keyPrefix));
    }

    public string ToFolderPrefix()
    {
        if (string.IsNullOrWhiteSpace(KeyPrefix))
        {
            return string.Empty;
        }

        return KeyPrefix.EndsWith("/", StringComparison.Ordinal) ? KeyPrefix : $"{KeyPrefix}/";
    }
}
