using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.ObjectStorage.TencentCos;

internal readonly record struct TencentCosPath(string BucketName, string KeyPrefix)
{
    public static OperationResult<TencentCosPath> FromRemotePath(RemotePath path)
    {
        if (path.IsRoot)
        {
            return OperationResult<TencentCosPath>.Failure(StorageError.Validation("Bucket path is required."));
        }

        var value = path.ToString().Trim('/');
        var separator = value.IndexOf('/', StringComparison.Ordinal);
        var bucketName = separator < 0 ? value : value[..separator];
        var keyPrefix = separator < 0 ? string.Empty : value[(separator + 1)..];

        if (string.IsNullOrWhiteSpace(bucketName))
        {
            return OperationResult<TencentCosPath>.Failure(StorageError.Validation("Bucket name is required."));
        }

        return OperationResult<TencentCosPath>.Success(new TencentCosPath(bucketName, keyPrefix));
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
