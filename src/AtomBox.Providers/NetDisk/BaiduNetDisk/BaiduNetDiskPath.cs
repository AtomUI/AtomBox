using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.NetDisk.BaiduNetDisk;

internal static class BaiduNetDiskPath
{
    public static string NormalizeDirectoryRoot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == "/")
        {
            return "/";
        }

        return ToAbsolute(value);
    }

    public static string ResolvePath(RemotePath path, string rootPath)
    {
        var normalizedRoot = NormalizeDirectoryRoot(rootPath);
        if (path.IsRoot)
        {
            return normalizedRoot;
        }

        var relative = path.Value.Trim('/');
        if (string.IsNullOrWhiteSpace(relative))
        {
            return normalizedRoot;
        }

        return ApplyRoot(ToAbsolute(relative), normalizedRoot);
    }

    public static OperationResult<string> ResolveUploadPath(RemotePath path, string rootPath)
    {
        if (path.IsRoot)
        {
            return OperationResult<string>.Failure(StorageError.Validation("Upload target path is required."));
        }

        var value = path.Value.Trim('/');
        if (string.IsNullOrWhiteSpace(value) || value.EndsWith("/", StringComparison.Ordinal))
        {
            return OperationResult<string>.Failure(StorageError.Validation("Upload target file path is required."));
        }

        return OperationResult<string>.Success(ApplyRoot(ToAbsolute(value), NormalizeDirectoryRoot(rootPath)));
    }

    public static RemotePath ToRemotePath(string path, bool isFolder)
    {
        var normalized = path.Trim();
        if (normalized == "/")
        {
            return RemotePath.Root;
        }

        return new RemotePath(normalized, isFolder ? RemotePathKind.Folder : RemotePathKind.ObjectPath);
    }

    private static string ToAbsolute(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.StartsWith("/", StringComparison.Ordinal)
            ? normalized
            : "/" + normalized;
    }

    private static string ApplyRoot(string path, string rootPath)
    {
        if (rootPath == "/" ||
            path == rootPath ||
            path.StartsWith(rootPath + "/", StringComparison.Ordinal))
        {
            return path;
        }

        return rootPath.TrimEnd('/') + path;
    }
}
