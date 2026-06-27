using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.ObjectStorage.QiniuKodo;

internal readonly record struct QiniuKodoPath(string BucketName, string KeyPrefix)
{
    public static OperationResult<QiniuKodoPath> FromRemotePath(RemotePath path)
    {
        if (path.IsRoot)
        {
            return OperationResult<QiniuKodoPath>.Failure(StorageError.Validation("Bucket path is required."));
        }

        var value = path.ToString().Trim('/');
        var separator = value.IndexOf('/', StringComparison.Ordinal);
        var bucketName = separator < 0 ? value : value[..separator];
        var keyPrefix = separator < 0 ? string.Empty : value[(separator + 1)..];

        if (string.IsNullOrWhiteSpace(bucketName))
        {
            return OperationResult<QiniuKodoPath>.Failure(StorageError.Validation("Bucket name is required."));
        }

        return OperationResult<QiniuKodoPath>.Success(new QiniuKodoPath(bucketName, keyPrefix));
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
