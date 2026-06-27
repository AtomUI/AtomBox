using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.ObjectStorage.Upyun;

internal readonly record struct UpyunPath(string BucketName, string KeyPrefix)
{
    public static OperationResult<UpyunPath> FromRemotePath(RemotePath path)
    {
        if (path.IsRoot)
        {
            return OperationResult<UpyunPath>.Failure(StorageError.Validation("Bucket path is required."));
        }

        var value = path.ToString().Trim('/');
        var separator = value.IndexOf('/', StringComparison.Ordinal);
        var bucketName = separator < 0 ? value : value[..separator];
        var keyPrefix = separator < 0 ? string.Empty : value[(separator + 1)..];

        return string.IsNullOrWhiteSpace(bucketName)
            ? OperationResult<UpyunPath>.Failure(StorageError.Validation("Bucket name is required."))
            : OperationResult<UpyunPath>.Success(new UpyunPath(bucketName, keyPrefix));
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
