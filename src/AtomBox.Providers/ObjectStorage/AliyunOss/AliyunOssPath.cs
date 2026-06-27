using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.ObjectStorage.AliyunOss;

internal readonly record struct AliyunOssPath(string BucketName, string KeyPrefix)
{
    public static OperationResult<AliyunOssPath> FromRemotePath(RemotePath path)
    {
        if (path.IsRoot)
        {
            return OperationResult<AliyunOssPath>.Failure(StorageError.Validation("Bucket path is required."));
        }

        var value = path.ToString().Trim('/');
        var separator = value.IndexOf('/', StringComparison.Ordinal);
        var bucketName = separator < 0 ? value : value[..separator];
        var keyPrefix = separator < 0 ? string.Empty : value[(separator + 1)..];

        if (string.IsNullOrWhiteSpace(bucketName))
        {
            return OperationResult<AliyunOssPath>.Failure(StorageError.Validation("Bucket name is required."));
        }

        return OperationResult<AliyunOssPath>.Success(new AliyunOssPath(bucketName, keyPrefix));
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
