namespace AtomBox.Providers.Common;

internal static class RemoteTransferTemporaryPath
{
    public static string CreateUploadPath(string targetPath)
    {
        return CreateSiblingPath(targetPath, $".atombox-upload-{Guid.NewGuid():N}.part");
    }

    public static string CreateBackupPath(string targetPath)
    {
        return CreateSiblingPath(targetPath, $".atombox-backup-{Guid.NewGuid():N}.part");
    }

    private static string CreateSiblingPath(string targetPath, string temporaryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var normalized = targetPath.Replace('\\', '/');
        var separatorIndex = normalized.LastIndexOf('/');
        var parent = separatorIndex < 0 ? string.Empty : normalized[..(separatorIndex + 1)];
        return $"{parent}{temporaryName}";
    }
}
