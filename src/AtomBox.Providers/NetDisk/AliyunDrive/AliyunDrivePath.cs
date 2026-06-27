using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.NetDisk.AliyunDrive;

internal readonly record struct AliyunDriveUploadPath(string ParentFileId, string Name)
{
    public static OperationResult<AliyunDriveUploadPath> FromRemotePath(RemotePath path, string rootFileId)
    {
        if (path.IsRoot)
        {
            return OperationResult<AliyunDriveUploadPath>.Failure(StorageError.Validation("Upload target path is required."));
        }

        var value = path.Value.Trim('/');
        var separator = value.LastIndexOf("/", StringComparison.Ordinal);
        if (separator < 0)
        {
            return string.IsNullOrWhiteSpace(value)
                ? OperationResult<AliyunDriveUploadPath>.Failure(StorageError.Validation("Upload target file name is required."))
                : OperationResult<AliyunDriveUploadPath>.Success(
                    new AliyunDriveUploadPath(rootFileId, value));
        }

        if (separator == 0 || separator == value.Length - 1)
        {
            return OperationResult<AliyunDriveUploadPath>.Failure(
                StorageError.Validation("Upload target path must include parent file id and file name."));
        }

        return OperationResult<AliyunDriveUploadPath>.Success(
            new AliyunDriveUploadPath(value[..separator], value[(separator + 1)..]));
    }
}
